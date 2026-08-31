# RestaurantAgent Central API

API para recibir lotes del agente local, consolidarlos en PostgreSQL y servir el dashboard.
No se conecta a SQL Server ni acepta consultas SQL remotas.

Modelo SaaS: `Account/User → Business → Branch → ConnectorInstallation`. Un dispositivo se
vincula desde una sesión de usuario autenticada (nunca por código de activación pegado en el
instalador); a partir de ahí usa una identidad propia, completamente separada de la cuenta
humana que autorizó el vínculo.

## Endpoints

- `GET /api/health/live`
- `GET /api/health/ready`
- `POST /api/ingestion/batches` — agente (identidad de dispositivo)
- `GET /api/dashboard/today?branchCode=sucursal-piloto&date=2026-08-27` — agente
- `POST /api/agents/heartbeat` — agente
- `GET /api/branches/{branchCode}/sync-status` — agente
- `POST /api/web/auth/register`
- `POST /api/web/auth/login`
- `POST /api/web/auth/logout`
- `GET /api/web/auth/me`
- `GET /api/web/businesses`
- `POST /api/web/businesses`
- `GET /api/web/businesses/{businessId}/branches`
- `POST /api/web/businesses/{businessId}/branches`
- `GET /api/web/branches`
- `GET /api/web/branches/{branchCode}/connector`
- `POST /api/web/branches/{branchCode}/link-device` — 409 con la instalación activa si ya hay una
- `POST /api/web/branches/{branchCode}/replace-device`
- `GET /api/web/branches/{branchCode}/connector-installations`
- `POST /api/web/connector-installations/{id}/revoke?branchCode=...`
- `GET /api/web/agent/latest` — versión y URL de descarga del instalador universal
- `GET /api/web/dashboard/home?branchCode=sucursal-piloto&date=2026-08-27`
- `POST /api/web/branches/{branchCode}/request-sync`
- `GET /api/web/sales?branchCode=sucursal-piloto&date=2026-08-27&page=1&pageSize=25`
- `GET /api/web/sales/{branchCode}/{folio}`
- `GET /api/web/cash-movements?branchCode=...&date=...`
- `GET /api/admin/businesses`
- `GET /api/admin/branches`
- `GET /api/admin/branches/{branchCode}`
- `POST /api/admin/branches`
- `PUT /api/admin/branches/{branchCode}`
- `POST /api/admin/branches/{branchCode}/status`
- `GET /api/admin/branches/{branchCode}/connector-installations`
- `POST /api/admin/connector-installations/{id}/revoke`
- `POST /api/admin/branches/{branchCode}/replace-device` — soporte: reemplaza el equipo en nombre de un tenant
- `POST /api/admin/branches/{branchCode}/request-sync`
- `GET /api/admin/users`
- `GET /api/admin/users/{id}`
- `POST /api/admin/users`
- `PUT /api/admin/users/{id}`
- `POST /api/admin/users/{id}/status`
- `POST /api/admin/users/{id}/password`
- `POST /api/admin/users/{id}/businesses`
- `DELETE /api/admin/users/{id}/businesses/{businessId}`

Los conectores (`ConnectorInstallation`) envían `X-Connector-Id` + `Authorization: Bearer
<token>`. PostgreSQL conserva solamente el hash del token, nunca el valor en claro. No existe
mecanismo legacy de token compartido por sucursal ni activación por código: se eliminaron por
completo, sin compatibilidad hacia atrás.

Los endpoints `/api/admin/*` aceptan **cualquiera** de dos credenciales: el header
`X-Admin-Key` (llave interna estática, para scripts) o una cookie de sesión de
`/api/web/auth/login` cuyo usuario tenga rol de cuenta `SUPERADMIN` (usada por `admin-web`).

Los endpoints `/api/web/*` usan una cookie de sesión `HttpOnly`, `SameSite=Lax` y segura bajo
HTTPS. No exponen la llave administrativa ni reutilizan la credencial del conector.

**Regla de acceso a negocios/sucursales (`/api/web/*`)**: nunca hay atajo incondicional para
`SUPERADMIN` ahí — ni siquiera un operador de plataforma ve un negocio del que no es miembro
explícito en `business_members`. El acceso "ve todo" queda reservado exclusivamente a
`/api/admin/*` (ver `BusinessAccess`/`BusinessAccessTests`). El rol de cuenta (`app_users.role`,
`SUPERADMIN`/`USER`) y el rol de negocio (`business_members.role`, `OWNER`/`MANAGER`/`VIEWER`)
son conceptos separados.

## Coolify

Desplegar `docker-compose.coolify.yml` desde el repositorio y definir al menos:

- `SERVICE_PASSWORD_64_POSTGRES`
- `SERVICE_PASSWORD_64_CONNECTOR_ADMIN` (32 caracteres o más)
Las cuentas, incluyendo SUPERADMIN, se crean desde la aplicación mediante sus endpoints
protegidos; no se leen contraseñas de usuarios desde variables de entorno. Son opcionales
`INSTALLER_DOWNLOAD_URL`/`INSTALLER_VERSION` para la sección "Instalar conector".

Asignar el dominio HTTPS de Coolify al puerto `8080` del servicio `web`. Nginx sirve la PWA
y reenvía `/api/*` al servicio interno `api`. El esquema, el negocio bootstrap y el usuario
propietario se crean de forma idempotente durante el arranque; cambiar la variable de
contraseña después no reemplaza automáticamente la contraseña del usuario ya existente.

El servicio `admin` (`admin-web/`) sirve el panel de administración del SaaS en un dominio
HTTPS separado (o el mismo con otra ruta, según cómo se configure el proxy en Coolify).
Usa las mismas cookies de sesión que `web`, pero solo deja entrar cuentas `SUPERADMIN`.

Para desplegar la API del panel como un recurso aislado, sin reiniciar el Compose central,
usar `docker-compose.admin-api.yml`. Este Compose contiene solamente `admin-api` y se une
como consumidor a la red externa del stack central para reutilizar PostgreSQL; no crea ni
reinicia la base de datos, el dashboard operativo ni el agente. Deben definirse
`POSTGRES_PASSWORD` y `CONNECTOR_ADMIN_KEY` en el recurso independiente. Las cuentas se
crean desde la aplicación y no se leen credenciales de usuario desde el entorno.

## Flujo normal (autoservicio, sin intervención de operador)

1. El usuario se registra (`POST /api/web/auth/register`) o inicia sesión.
2. Crea un negocio (`POST /api/web/businesses`) y una o más sucursales
   (`POST /api/web/businesses/{businessId}/branches`).
3. Descarga el instalador universal (`GET /api/web/agent/latest`) y lo instala en el equipo de
   la sucursal — el instalador ya no pide ningún código.
4. Abre la GUI del agente (`extractor-ui`), inicia sesión con la misma cuenta, elige la
   sucursal y confirma "Vincular este equipo" — esto llama
   `POST /api/web/branches/{branchCode}/link-device` y entrega la credencial resultante al
   servicio local, que la persiste vía DPAPI.
5. Si la sucursal ya tenía un conector activo, `link-device` responde 409 con esa instalación;
   la GUI ofrece "Reemplazar equipo" (`POST /api/web/branches/{branchCode}/replace-device`),
   que revoca el anterior y emite uno nuevo atómicamente.
6. Revocar un equipo desde el dashboard:
   `POST /api/web/connector-installations/{id}/revoke?branchCode=...` (requiere rol OWNER o
   MANAGER en el negocio dueño de la sucursal).

## Operación (SUPERADMIN / admin-web)

- Ver todas las sucursales/negocios sin restricción vía `/api/admin/*`.
- Ver el historial completo de instalaciones de una sucursal:
  `GET /api/admin/branches/{branchCode}/connector-installations`.
- Revocar en nombre de un tenant: `POST /api/admin/connector-installations/{id}/revoke`.
- Reemplazar el equipo en nombre de un tenant que no puede hacerlo por sí mismo:
  `POST /api/admin/branches/{branchCode}/replace-device`.
- Crear/editar/activar-desactivar sucursales igual que antes, ahora con `businessId`
  obligatorio en la creación.

## Usuarios y permisos

1. Crear una cuenta con `POST /api/admin/users` (`email`, `displayName`, `password` ≥12
   caracteres, `role` en `SUPERADMIN|USER`). Falla con 409 si el correo ya existe. La cuenta se
   crea sin ninguna membresía de negocio — se asigna después.
2. Editar nombre/rol con `PUT /api/admin/users/{id}`. Activar/desactivar con
   `POST /api/admin/users/{id}/status` (`{"active": bool}`) — nunca se borra una cuenta, solo
   se desactiva; conserva `audit_log` y `last_login_at`. Ambas operaciones responden 409 si
   dejarían al sistema sin ningún `SUPERADMIN` activo (ver `SuperAdminGuard`).
3. Restablecer contraseña con `POST /api/admin/users/{id}/password` (mismo hasher que el
   login). Desactivar o restablecer contraseña revoca de inmediato todas las sesiones de esa
   cuenta (`app_sessions`), no solo hacia adelante.
4. Asignar negocios con `POST /api/admin/users/{id}/businesses`
   (`{"businessIds": [...], "role": "OWNER|MANAGER|VIEWER"}`, transaccional, sin duplicados
   gracias a `ON CONFLICT`). Quitar uno con
   `DELETE /api/admin/users/{id}/businesses/{businessId}`.
5. Toda mutación sobre un usuario (`PUT`, `.../status`, `.../password`) responde
   `{"user": ..., "selfAffected": bool}`: `selfAffected` es `true` cuando quien llama modificó
   su propia cuenta, para que el panel pueda avisar con claridad que su sesión quedó inválida
   en vez de dejarlo con una UI que de repente empieza a fallar con 401.
