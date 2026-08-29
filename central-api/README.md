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
- `PUT /api/admin/branches/{branchCode}`
- `POST /api/admin/branches/{branchCode}/activation-keys`
- `GET /api/admin/branches/{branchCode}/connectors`
- `POST /api/admin/connectors/{connectorId}/revoke`
- `POST /api/admin/connectors/{connectorId}/rotate-token`
- `POST /api/admin/branches/{branchCode}/legacy-auth/disable`

Los conectores nuevos envían `X-Connector-Id` y `Authorization: Bearer <token>`.
PostgreSQL conserva solamente los hashes de claves de activación y tokens. Los endpoints
`/api/admin/*` requieren `X-Admin-Key`; esa llave interna vive únicamente en Coolify.

`X-Agent-Token` permanece temporalmente como mecanismo **legacy** si la sucursal tiene
`legacy_auth_enabled=true`. No se admiten listas de contraseñas compartidas.

Los endpoints `/api/web/*` usan una cookie de sesión `HttpOnly`, `SameSite=Lax` y segura
bajo HTTPS. No exponen la llave administrativa ni reutilizan la credencial del conector.

## Coolify

Desplegar `docker-compose.coolify.yml` desde el repositorio y definir al menos:

- `SERVICE_PASSWORD_64_POSTGRES`
- `SERVICE_PASSWORD_64_CONNECTOR_ADMIN` (32 caracteres o más)
- `DASHBOARD_OWNER_EMAIL`
- `SERVICE_PASSWORD_64_DASHBOARD_OWNER` (12 caracteres o más)

Opcionalmente ajustar `BOOTSTRAP_BRANCH_CODE`, `BOOTSTRAP_BRANCH_NAME`,
`DASHBOARD_SESSION_HOURS` y `DASHBOARD_STALE_MINUTES`.

Durante la migración puede conservarse `SERVICE_PASSWORD_64_AGENT`; la API lo recibe como
`LEGACY_BOOTSTRAP_AGENT_TOKEN`. Después de migrar y desactivar legacy, debe eliminarse.

Asignar el dominio HTTPS de Coolify al puerto `8080` del servicio `web`. Nginx sirve la PWA
y reenvía `/api/*` al servicio interno `api`. El esquema y el usuario propietario se crean
de forma idempotente durante el arranque; cambiar la variable de contraseña después no
reemplaza automáticamente la contraseña del usuario ya existente.

## Alta y administración

1. Crear/actualizar una sucursal con `PUT /api/admin/branches/{branchCode}`.
2. Generar una clave de un solo uso con
   `POST /api/admin/branches/{branchCode}/activation-keys`.
3. Introducir esa clave en el instalador del equipo. El agente llama una sola vez a
   `POST /api/connectors/activate`.
4. Consultar conectores con `GET /api/admin/branches/{branchCode}/connectors`.
5. Revocar únicamente uno con `POST /api/admin/connectors/{id}/revoke`.
6. Rotar la credencial de un conector activo con
   `POST /api/admin/connectors/{id}/rotate-token`; el nuevo token se muestra una sola vez.
   Guardar esa respuesta como JSON en el equipo, ejecutar
   `SoftRestaurant.Extractor.exe --import-connector-credential credencial.json` y reiniciar
   el servicio. El agente cifra el valor con DPAPI y elimina el JSON en texto plano.
