# SoftRestaurant Central API

API mínima para recibir lotes del agente local y consolidarlos en PostgreSQL.
No se conecta a SQL Server ni acepta consultas SQL remotas.

## Endpoints

- `GET /api/health/live`
- `GET /api/health/ready`
- `POST /api/ingestion/batches`
- `GET /api/dashboard/today?branchCode=sucursal-piloto&date=2026-08-27`
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

## Coolify

Desplegar el `docker-compose.yml` de la raíz y definir al menos:

- `POSTGRES_PASSWORD`
- `SERVICE_PASSWORD_64_CONNECTOR_ADMIN`
- `BOOTSTRAP_BRANCH_CODE`
- `BOOTSTRAP_BRANCH_NAME`

Durante la migración puede conservarse `SERVICE_PASSWORD_64_AGENT`; la API lo recibe como
`LEGACY_BOOTSTRAP_AGENT_TOKEN`. Después de migrar y desactivar legacy, debe eliminarse.

Asignar el dominio HTTPS de Coolify al puerto `8080` del servicio `api`. El esquema
se crea de forma idempotente durante el arranque.

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
