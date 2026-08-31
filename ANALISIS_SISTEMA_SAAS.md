# RestaurantAgent Sync Agent — Análisis técnico del sistema

**Fecha del análisis:** 31 de agosto de 2026
**Versión del modelo descrito:** SaaS Account → Business → Branch → ConnectorInstallation (post-migración, instalador v2.0.0)
**Repositorio:** `D:\base de datos softres\respaldo`

---

## 1. Descripción general

El sistema es una plataforma SaaS que extrae información operativa de **RestaurantAgent 11**
(punto de venta de restaurantes, sobre SQL Server) y la pone disponible en un panel web, sin
modificar ni depender del sistema de licencias de RestaurantAgent. Está compuesto por un agente
de Windows que lee la base de datos en modo solo lectura, una API central que consolida los
datos en PostgreSQL, y dos aplicaciones web (panel de cliente y panel de administración).

El objetivo del sistema **no es reemplazar RestaurantAgent**, sino ofrecer visibilidad remota
de la operación (ventas, cortes de caja, cancelaciones, turnos) desde cualquier dispositivo,
para dueños y gerentes de restaurantes que administran una o varias sucursales.

### 1.1 Modelo de datos / jerarquía

```
Account (cuenta humana)
  └── Business (negocio) ──┬── membresía por negocio: OWNER / MANAGER / VIEWER
                            │
                            └── Branch (sucursal) ──── ConnectorInstallation (equipo vinculado)
                                                              │
                                                              └── historial: activo actual +
                                                                  instalaciones revocadas/reemplazadas
```

- Una cuenta puede pertenecer a varios negocios.
- Un negocio puede tener varias sucursales.
- Cada sucursal tiene **como máximo un conector activo a la vez** (garantizado a nivel de base
  de datos con un índice único parcial), pero conserva el historial completo de equipos
  anteriores (reemplazados o revocados).

---

## 2. Componentes del sistema y lenguajes utilizados

| Componente | Carpeta | Lenguaje / stack | Rol |
|---|---|---|---|
| Agente extractor | `extractor/` | C# / .NET 8 (Windows Service) | Lee RestaurantAgent vía SQL Server (solo `SELECT`), concilia y envía los datos |
| Panel local (bandeja) | `extractor-ui/` | C# / .NET 8, WinForms | GUI para login, vinculación del equipo y monitoreo del servicio |
| API central | `central-api/` | C# / .NET 8 (ASP.NET Core Minimal APIs) | Recibe lotes del agente, gestiona cuentas/negocios/sucursales/conectores, sirve el dashboard |
| Base de datos | PostgreSQL 18 | SQL (esquema propio, sin ORM/EF Core) | Almacena ventas, turnos, cuentas, negocios y conectores |
| Panel del cliente | `dashboard-web/` | TypeScript / React 19 + Vite | Registro, negocios, sucursales, "instalar conector", consulta operativa |
| Panel de administración | `admin-web/` | TypeScript / React 19 + Vite | Herramientas de operador (SUPERADMIN): sucursales, usuarios, historial de conectores |
| Contrato compartido | `sync-contracts/` | C# / .NET 8 (librería) | Define el formato del lote (`SyncBatch`) que comparten agente y API |
| Instalador | `installer/` | Inno Setup 6 (script `.iss`) + PowerShell | Empaqueta agente + GUI en un instalador único para Windows |
| Pruebas | `auth-tests/` | C# / xUnit | 82 pruebas automatizadas de autenticación, autorización y validación |

**Base de datos de origen:** SQL Server (la de RestaurantAgent, típicamente `restaurant11`),
accedida en modo **solo lectura** — el agente nunca ejecuta `INSERT`/`UPDATE`/`DELETE`/`ALTER`
contra ella.

**Base de datos de la plataforma:** PostgreSQL, con esquema definido a mano en
`central-api/schema.sql` (sin ORM). El esquema se aplica de forma idempotente en cada arranque
de la API (`CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`).

**Despliegue:** Docker (un `Dockerfile` por servicio) orquestado con `docker-compose.yml` /
`docker-compose.coolify.yml` (para Coolify) / `docker-compose.admin-api.yml` (despliegue
aislado del panel admin).

---

## 3. Funciones y características principales

### 3.1 Extracción de datos (agente)
- Extrae, en modo `--watch` (servicio continuo), una ventana móvil de días recientes.
- Entidades extraídas: ventas (`cheques`), líneas de venta, pagos, turnos, declaraciones de
  cajero, movimientos de caja, cancelaciones.
- **Reconciliación obligatoria**: antes de enviar un lote, compara los totales extraídos contra
  los totales de control de RestaurantAgent; si no coinciden, el lote no se envía (evita
  reportar datos inconsistentes).
- **Cola local (outbox)** en SQLite: si no hay Internet o la API no responde, los lotes quedan
  en cola y se reintentan con backoff, sin perder información.
- Nunca abre puertos de entrada en el restaurante: toda la comunicación es saliente (agente →
  API central).

### 3.2 Autoservicio (dashboard-web)
- Registro y login de cuentas.
- Creación y administración de negocios y sucursales.
- Visualización de ventas del día, histórico, tickets individuales, movimientos de caja,
  cobertura/conciliación de la sincronización.
- Botón "Sincronizar ahora" (solicitud remota de sincronización).
- Sección **"Instalar conector"**: descarga el instalador universal y muestra el estado del
  conector de cada sucursal (vinculado / sin instalar / última sincronización / errores).

### 3.3 Administración de plataforma (admin-web)
- Solo para cuentas con rol de plataforma `SUPERADMIN`.
- Gestión de todas las sucursales y negocios sin restricción.
- Gestión de cuentas de usuario y su membresía en negocios (rol OWNER/MANAGER/VIEWER por
  negocio).
- Historial completo de `ConnectorInstallation` por sucursal: máquina, versión del agente,
  último latido, última sincronización correcta, último error.
- Acciones de soporte: **revocar** un conector o **reemplazar el equipo** en nombre de un
  cliente que no puede hacerlo por sí mismo.

### 3.4 Panel local del agente (extractor-ui)
- Ícono de bandeja del sistema, independiente del servicio de Windows.
- Ventana de estado: conectividad SQL/API, última sincronización, pendientes, errores.
- Flujo de vinculación (login + selección de negocio/sucursal + "Vincular este equipo").
- Botones "Sincronizar ahora", "Diagnóstico", "Ver logs".
- Cerrar la GUI **no** detiene el servicio — son procesos independientes.

---

## 4. Autenticación: dos identidades completamente separadas

Este es el punto de diseño más importante del sistema. Existen **dos sistemas de
autenticación distintos que nunca se mezclan**:

### 4.1 Autenticación humana (cuentas)
- Registro/login con correo y contraseña (`POST /api/web/auth/register` /
  `POST /api/web/auth/login`).
- Contraseñas con hash `PasswordHasher<T>` de ASP.NET Core Identity (nunca en texto plano).
- Sesión: token opaco aleatorio, guardado como **hash SHA-256** en la base de datos, entregado
  al navegador como cookie `HttpOnly`, `Secure`, `SameSite=Lax` (`sr_dashboard_session`).
- El rol de la cuenta (`app_users.role`) solo distingue **SUPERADMIN** (operador de la
  plataforma) de **USER** (cuenta normal). El permiso real sobre cada negocio
  (OWNER/MANAGER/VIEWER) vive en una tabla aparte (`business_members`), no en la cuenta misma.
- Esta identidad **solo sirve para usar la plataforma web y autorizar la vinculación de un
  equipo**. Nunca viaja hacia el servicio de Windows.

### 4.2 Autenticación de dispositivo (agente)
- Cada equipo vinculado tiene su propia identidad: un `ConnectorInstallation` con un token
  independiente (prefijo `sra_conn_...`), generado con 32 bytes aleatorios criptográficamente
  seguros.
- La API central **nunca guarda el token en texto plano**, solo su hash SHA-256
  (`TokenHasher.Hash`), igual que las contraseñas y las sesiones humanas.
- Cada llamada del agente a la API lleva dos cabeceras:
  - `X-Connector-Id: <id de la instalación>`
  - `Authorization: Bearer <token del dispositivo>`
- El servicio de Windows **nunca almacena** el correo, la contraseña, la sesión web ni ningún
  dato de la cuenta humana que hizo la vinculación — solo su propia credencial de dispositivo.
- Esa credencial se protege en disco con **DPAPI** (`ProtectedData.Protect`, ámbito
  `DataProtectionScope.LocalMachine`), en un archivo cifrado en
  `%ProgramData%\RestaurantAgentSyncAgent\agent-settings.dpapi`, con permisos NTFS restringidos
  únicamente a `SYSTEM` y `Administradores` (vía `icacls`, configurado por el instalador).
- Cambiar la contraseña de la cuenta, cerrar todas sus sesiones web, o cerrar la GUI **no
  afecta en nada** al servicio: sigue sincronizando con su propia identidad.

### 4.3 Autorización por endpoint

| Grupo de endpoints | Quién puede llamarlos | Mecanismo |
|---|---|---|
| `/api/web/*` (autoservicio) | Cuenta humana con sesión | Cookie de sesión; nunca da acceso incondicional a SUPERADMIN — incluso un operador de plataforma solo ve los negocios de los que es miembro explícito |
| `/api/admin/*` (operador) | Operador de plataforma | Cookie de sesión con rol `SUPERADMIN` |
| `/api/ingestion/*`, `/api/agents/*`, `/api/branches/*/sync-status` | El agente (dispositivo) | `X-Connector-Id` + `Authorization: Bearer` validado contra `connector_installations` |

---

## 5. Cómo se vincula un equipo (reemplaza a la activación por código)

Antes del 30 de agosto de 2026, un equipo se "activaba" pegando una clave de un solo uso
generada por un administrador. **Eso se eliminó por completo.** El flujo actual:

1. El usuario abre `dashboard-web`, se registra o inicia sesión.
2. Crea un **negocio** y al menos una **sucursal**.
3. Descarga el instalador universal (mismo instalador para todos los clientes, no hay uno por
   sucursal) desde la sección "Instalar conector".
4. Instala el agente en la computadora de la sucursal. El instalador:
   - Detecta automáticamente la instancia SQL Server y la base de datos de RestaurantAgent
     (busca `restaurant.ini` de SR 9.5/10/11 en las rutas típicas de instalación).
   - Pide usuario/contraseña SQL (el mismo que ya usa RestaurantAgent).
   - **No pide ningún código.**
   - Crea el servicio de Windows con arranque automático y recuperación ante fallos
     (`sc.exe failure ... restart/60000`), de modo que arranca solo aunque nadie inicie sesión
     en Windows.
5. El servicio arranca en estado **"no vinculado"**: extrae y concilia datos localmente, pero
   no envía nada hasta tener identidad de dispositivo.
6. El usuario abre el panel del agente (ícono de bandeja), inicia sesión con la misma cuenta,
   elige negocio y sucursal, y confirma **"Vincular este equipo"**.
7. La GUI llama a `POST /api/web/branches/{sucursal}/link-device`. Si la sucursal ya tiene un
   conector activo, la API responde **409** con los datos del equipo actual — la GUI no crea
   silenciosamente un segundo conector, ofrece **"Reemplazar equipo"** en su lugar
   (`POST /api/web/branches/{sucursal}/replace-device`), que revoca el anterior y emite uno
   nuevo en una sola transacción atómica.
8. La API devuelve la credencial del nuevo `ConnectorInstallation`. La GUI la entrega al
   servicio local (que corre elevado, dueño del archivo protegido) vía
   `POST http://127.0.0.1:<puerto>/link`.
9. El servicio persiste la credencial con DPAPI y la aplica **en caliente** — sin reiniciarse —
   y desde ese momento envía heartbeats y sincroniza con su identidad de dispositivo.

### 5.1 Revocación
- Desde `dashboard-web` (dueño/gerente del negocio) o `admin-web` (operador), se puede revocar
  el conector activo de una sucursal en cualquier momento.
- La revocación es efectiva **de inmediato en el servidor**: aunque el equipo esté offline y
  todavía conserve el token en su disco, la próxima llamada que haga es rechazada con 401. El
  agente detecta esto y pasa a un estado visible de "credencial revocada" en vez de reintentar
  indefinidamente.

---

## 6. Método de instalación

- **Instalador único (Inno Setup 6)**, sin variantes por cliente/negocio/sucursal:
  `RestaurantAgent-Sync-Agent-<versión>-x64.exe`.
- Requiere permisos de administrador de Windows (crea un servicio).
- Arquitectura x64 únicamente.
- Contenido: agente (`RestaurantAgent.Extractor.exe`) y GUI (`RestaurantAgent.Extractor.Ui.exe`),
  ambos publicados como ejecutables **autocontenidos** de .NET (no requieren tener el .NET
  Runtime preinstalado en la máquina destino).
- Pasos del asistente: detección/confirmación de conexión SQL → credenciales SQL → resumen →
  instalación. Ya no hay paso de activación.
- Post-instalación: crea el servicio de Windows (`RestaurantAgentSyncAgent`), accesos directos
  ("Estado del servicio", "Panel del agente") y un acceso de autoarranque de la GUI para
  cualquier usuario que inicie sesión en esa máquina.
- En una actualización sobre una instalación existente, el instalador detecta si ya hay una
  configuración SQL válida y **no vuelve a pedirla**; tampoco toca la identidad de dispositivo
  ya vinculada (nunca escribe esas claves) — una actualización preserva vínculo y configuración.
- Compilación reproducible vía `installer/build-installer.ps1 -Version X -ApiUrl https://...`,
  que hace `dotnet publish` de ambos proyectos e invoca `ISCC.exe` (Inno Setup) — verificado de
  punta a punta generando el instalador de producción real.
- El instalador **no está firmado digitalmente** (Authenticode); Windows SmartScreen puede
  advertir en una instalación limpia, lo cual es esperado hasta que se adquiera un certificado
  de firma de código.

---

## 7. Ciclo de vida técnico: de la construcción al uso

1. **Construcción**: código fuente C#/.NET 8 (backend, agente, GUI) y TypeScript/React
   (paneles web) en el mismo repositorio Git. Sin ORM en el backend (SQL escrito a mano vía
   Npgsql, parametrizado contra inyección SQL); sin backend framework pesado, ASP.NET Core
   Minimal APIs. Front-end compilado con Vite/TypeScript en modo estricto.
2. **Pruebas**: 82 pruebas automatizadas (xUnit) cubren autenticación, autorización
   (`BusinessAccess`), validaciones y el ciclo de vida de la credencial de dispositivo
   (DPAPI, generación/hash de tokens). `dotnet build`/`dotnet test` y `npm run build`
   (TypeScript estricto) se ejecutan como verificación antes de cada entrega.
3. **Empaquetado**: `dotnet publish` (self-contained, single-file) para agente y GUI; `npm run
   build` para los paneles web; Docker build por servicio; Inno Setup para el instalador de
   Windows.
4. **Despliegue del backend**: contenedores Docker (`postgres`, `api`, `web`, `admin`)
   orquestados con Docker Compose, típicamente detrás de Coolify con dominios HTTPS propios
   para el panel de cliente y el panel de administración.
5. **Distribución del agente**: el instalador se descarga desde el propio panel web
   (`GET /api/web/agent/latest`), no se distribuye por otro canal.
6. **Uso en el restaurante**: instalación única del agente por sucursal, vinculación una sola
   vez desde la GUI, y operación desatendida en adelante (servicio de Windows con
   auto-arranque y auto-recuperación).
7. **Operación y soporte**: el panel de administración permite a un operador de la plataforma
   monitorear todas las instalaciones, revocar o reemplazar equipos, y gestionar cuentas y
   negocios sin necesidad de acceso físico a las sucursales.

---

## 8. Seguridad — puntos clave

- Ningún secreto (contraseña, token de sesión, token de dispositivo) se guarda nunca en texto
  plano en la base de datos: todo se guarda como hash SHA-256 o hash de contraseña de ASP.NET
  Core Identity.
- Separación estricta entre identidad humana e identidad de dispositivo (ver sección 4).
- Aislamiento multi-tenant: todo endpoint que opera sobre un negocio/sucursal/conector valida
  en el servidor que la cuenta (o el dispositivo) tiene acceso real a ese recurso — nunca se
  confía en lo que el navegador oculta o muestra. Una cuenta de un cliente no puede ver, vincular,
  sincronizar ni revocar recursos de otro cliente manipulando IDs.
- El agente solo ejecuta consultas `SELECT` contra RestaurantAgent; no tiene permisos ni código
  para modificar la base origen.
- `AgentControlServer` (la API local del servicio) solo escucha en `127.0.0.1`, nunca es
  accesible desde la red del restaurante.
- Solo un conector activo por sucursal, garantizado a nivel de base de datos (índice único
  parcial), no solo a nivel de aplicación.

---

## 9. Estado de este análisis

Este documento describe el sistema tal como quedó tras la migración del 30–31 de agosto de
2026 (eliminación completa de la activación por código, sin compatibilidad hacia atrás). El
backend y los paneles web compilan y pasan sus pruebas automatizadas; el instalador fue
verificado compilando de punta a punta con Inno Setup real. Pendiente de verificación manual
por el usuario: instalación en máquina limpia, despliegue del backend a producción, y el
recorrido completo end-to-end (heartbeat, primera sincronización, revocación, reemplazo de
equipo, recuperación tras pérdida de Internet, reinicio de Windows sin sesión iniciada).
