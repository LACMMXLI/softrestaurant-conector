# SoftRestaurant Central API

API para recibir lotes del agente local, consolidarlos en PostgreSQL y servir el dashboard.
No se conecta a SQL Server ni acepta consultas SQL remotas.

## Endpoints

- `GET /api/health/live`
- `GET /api/health/ready`
- `POST /api/ingestion/batches`
- `GET /api/dashboard/today?branchCode=sucursal-piloto&date=2026-08-27`
- `POST /api/web/auth/login`
- `POST /api/web/auth/logout`
- `GET /api/web/auth/me`
- `GET /api/web/branches`
- `GET /api/web/dashboard/home?branchCode=sucursal-piloto&date=2026-08-27`
- `GET /api/web/sales?branchCode=sucursal-piloto&date=2026-08-27&page=1&pageSize=25`
- `GET /api/web/sales/{branchCode}/{folio}`
- `GET /api/branches/sucursal-piloto/sync-status`
- `POST /api/connectors/activate`
- `GET /api/admin/branches`
- `GET /api/admin/branches/{branchCode}`
- `POST /api/admin/branches`
- `PUT /api/admin/branches/{branchCode}`
- `POST /api/admin/branches/{branchCode}/status`
- `POST /api/admin/branches/{branchCode}/activation-keys`
- `GET /api/admin/branches/{branchCode}/connectors`
- `POST /api/admin/connectors/{connectorId}/revoke`
- `POST /api/admin/connectors/{connectorId}/rotate-token`
- `POST /api/admin/branches/{branchCode}/legacy-auth/disable`
- `GET /api/admin/users`
- `GET /api/admin/users/{id}`
- `POST /api/admin/users`
- `PUT /api/admin/users/{id}`
- `POST /api/admin/users/{id}/status`
- `POST /api/admin/users/{id}/password`
- `POST /api/admin/users/{id}/branches`
- `DELETE /api/admin/users/{id}/branches/{branchCode}`

Los conectores nuevos envían `X-Connector-Id` y `Authorization: Bearer <token>`.
PostgreSQL conserva solamente los hashes de claves de activación y tokens. Los endpoints
`/api/admin/*` aceptan **cualquiera** de dos credenciales: el header `X-Admin-Key` (llave
interna estática, para scripts) o una cookie de sesión de `/api/web/auth/login` cuyo usuario
tenga rol `SUPERADMIN` (usada por `admin-web`, el panel de administración del SaaS).

`X-Agent-Token` permanece temporalmente como mecanismo **legacy** si la sucursal tiene
`legacy_auth_enabled=true`. No se admiten listas de contraseñas compartidas.

Los endpoints `/api/web/*` usan una cookie de sesión `HttpOnly`, `SameSite=Lax` y segura
bajo HTTPS. No exponen la llave administrativa ni reutilizan la credencial del conector.

**Regla de acceso a sucursales (`/api/web/*`)**: `SUPERADMIN` ve todas las sucursales activas
sin necesitar filas en `app_user_branches`. Cualquier otro rol —incluido `OWNER`— solo ve las
sucursales que tenga asignadas explícitamente en esa tabla; tener rol `OWNER` por sí solo ya
**no** implica acceso global (antes de esta fase sí lo hacía; ver `DashboardReportService` y
`BranchAccess`/`BranchAccessTests`).

## Coolify

Desplegar `docker-compose.coolify.yml` desde el repositorio y definir al menos:

- `SERVICE_PASSWORD_64_POSTGRES`
- `SERVICE_PASSWORD_64_CONNECTOR_ADMIN` (32 caracteres o más)
- `DASHBOARD_OWNER_EMAIL`
- `SERVICE_PASSWORD_64_DASHBOARD_OWNER` (12 caracteres o más)
- `DASHBOARD_ADMIN_EMAIL`
- `SERVICE_PASSWORD_64_DASHBOARD_ADMIN` (12 caracteres o más) — cuenta `SUPERADMIN` para `admin-web`

Opcionalmente ajustar `BOOTSTRAP_BRANCH_CODE`, `BOOTSTRAP_BRANCH_NAME`,
`DASHBOARD_SESSION_HOURS` y `DASHBOARD_STALE_MINUTES`.

Durante la migración puede conservarse `SERVICE_PASSWORD_64_AGENT`; la API lo recibe como
`LEGACY_BOOTSTRAP_AGENT_TOKEN`. Después de migrar y desactivar legacy, debe eliminarse.

Asignar el dominio HTTPS de Coolify al puerto `8080` del servicio `web`. Nginx sirve la PWA
y reenvía `/api/*` al servicio interno `api`. El esquema y el usuario propietario se crean
de forma idempotente durante el arranque; cambiar la variable de contraseña después no
reemplaza automáticamente la contraseña del usuario ya existente.

El servicio `admin` (`admin-web/`) sirve el panel de administración del SaaS en un dominio
HTTPS separado (o el mismo con otra ruta, según cómo se configure el proxy en Coolify).
Usa las mismas cookies de sesión que `web`, pero solo deja entrar cuentas `SUPERADMIN`.

## Alta y administración

1. Crear una sucursal con `POST /api/admin/branches` (falla con 409 si el código ya existe).
2. Editar nombre/zona horaria con `PUT /api/admin/branches/{branchCode}` (falla con 404 si
   no existe; no crea sucursales nuevas). Consultar el listado completo con
   `GET /api/admin/branches` o el detalle de una con `GET /api/admin/branches/{branchCode}`.
3. Activar o desactivar con `POST /api/admin/branches/{branchCode}/status` (`{"active": bool}`).
   Ninguna operación borra la fila: una sucursal desactivada conserva sus ventas, turnos y
   conectores, solo deja de aparecer en `/api/web/*` y de aceptar nuevas activaciones.
4. Generar una clave de un solo uso con
   `POST /api/admin/branches/{branchCode}/activation-keys`.
5. Introducir esa clave en el instalador del equipo. El agente llama una sola vez a
   `POST /api/connectors/activate`.
6. Consultar conectores con `GET /api/admin/branches/{branchCode}/connectors`.
7. Revocar únicamente uno con `POST /api/admin/connectors/{id}/revoke`.
8. Rotar la credencial de un conector activo con
   `POST /api/admin/connectors/{id}/rotate-token`; el nuevo token se muestra una sola vez.
   Guardar esa respuesta como JSON en el equipo, ejecutar
   `SoftRestaurant.Extractor.exe --import-connector-credential credencial.json` y reiniciar
   el servicio. El agente cifra el valor con DPAPI y elimina el JSON en texto plano.

## Usuarios y permisos

1. Crear una cuenta con `POST /api/admin/users` (`email`, `displayName`, `password` ≥12
   caracteres, `role` en `SUPERADMIN|OWNER|MANAGER|VIEWER`, y opcionalmente `branchCodes` para
   asignarle sucursales desde el alta — se ignoran si `role` es `SUPERADMIN`). Falla con 409 si
   el correo ya existe.
2. Editar nombre/rol con `PUT /api/admin/users/{id}`. Activar/desactivar con
   `POST /api/admin/users/{id}/status` (`{"active": bool}`) — nunca se borra una cuenta, solo
   se desactiva; conserva `audit_log` y `last_login_at`. Ambas operaciones responden 409 si
   dejarían al sistema sin ningún `SUPERADMIN` activo (ver `SuperAdminGuard`).
3. Restablecer contraseña con `POST /api/admin/users/{id}/password` (mismo hasher que el
   login). Desactivar o restablecer contraseña revoca de inmediato todas las sesiones de esa
   cuenta (`app_sessions`), no solo hacia adelante.
4. Asignar sucursales con `POST /api/admin/users/{id}/branches` (`{"branchCodes": [...]}`,
   transaccional, sin duplicados gracias a `ON CONFLICT`). Quitar una con
   `DELETE /api/admin/users/{id}/branches/{branchCode}`.
5. Toda mutación sobre un usuario (`PUT`, `.../status`, `.../password`) responde
   `{"user": ..., "selfAffected": bool}`: `selfAffected` es `true` cuando quien llama modificó
   su propia cuenta, para que el panel pueda avisar con claridad que su sesión quedó inválida
   en vez de dejarlo con una UI que de repente empieza a fallar con 401.
