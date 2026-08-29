# Panel de administración SoftRestaurant Sync (SaaS)

SPA React separada de `dashboard-web`. Consume `/api/web/auth/*` para iniciar sesión y
`/api/admin/*` para operar el negocio: alta, edición y activación/desactivación de
sucursales; llaves de activación de conectores, revocarlos, rotar su token y desactivar la
autenticación legacy; y alta, edición, activación/desactivación, restablecimiento de
contraseña y asignación de sucursales de cuentas de usuario.

Solo entran cuentas con rol `SUPERADMIN` (`app_users.role`). Cualquier otra cuenta que
inicie sesión aquí se desconecta de inmediato con un mensaje de error; los endpoints
`/api/admin/*` además verifican el rol del lado del servidor, así que ese filtro del
cliente es solo para dar mensajes claros, no la única defensa.

## Desarrollo

Con la API corriendo en `http://localhost:5080` y una cuenta `SUPERADMIN` de bootstrap
(`DASHBOARD_ADMIN_EMAIL` / `DASHBOARD_ADMIN_PASSWORD` en el entorno de `central-api`):

```powershell
npm install
npm run dev
```

Para validar la entrega:

```powershell
npm run lint
npm run build
```

## Producción

El Dockerfile compila la aplicación y la sirve mediante Nginx en el puerto `8080`,
reenviando `/api/*` al servicio `api` (mismo patrón que `dashboard-web/Dockerfile`).
Desplegar como el servicio `admin` de `docker-compose.yml` / `docker-compose.coolify.yml`,
idealmente detrás de un dominio o subdominio distinto al del dashboard operativo, ya que
da acceso a operaciones sensibles (llaves de activación, revocación de conectores).

## Alcance de esta versión

Cubre el CRUD completo de sucursales (alta, edición, activar/desactivar — nunca borrado
físico), llaves de activación y gestión de conectores dentro del detalle de cada sucursal, y
el CRUD completo de usuarios: alta (con contraseña y sucursales iniciales), edición de
nombre/rol, activar/desactivar, restablecer contraseña, y agregar/quitar sucursales
asignadas. Ninguna operación borra filas: todo pasa por el estado `active`.

Para `OWNER`, `MANAGER` y `VIEWER` el detalle de usuario muestra sus sucursales asignadas y
permite editarlas. Para `SUPERADMIN` muestra "Acceso global" — no depende de
`app_user_branches` porque `SUPERADMIN` nunca lo necesitó (ver [central-api/README.md](../central-api/README.md#usuarios-y-permisos)).

Si una cuenta `SUPERADMIN` se desactiva, se restablece su propia contraseña, o se le quita el
rol `SUPERADMIN` a sí misma, la API responde `selfAffected: true` y el panel cierra la sesión
localmente con un mensaje explícito en vez de dejarla con una pantalla que de repente
empieza a fallar con 401. No es posible desactivar ni degradar a la última cuenta
`SUPERADMIN` activa del sistema — la API responde 409 y el panel muestra el motivo.
