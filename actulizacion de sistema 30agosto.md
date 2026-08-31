Quiero hacer el cambio definitivo del conector actual a un modelo SaaS basado en cuentas. Implementa el cambio completo en el proyecto.

No necesito conservar compatibilidad con el sistema actual de códigos de activación ni con instalaciones existentes. Podemos romper esquema/configuración anterior. Voy a desinstalar y reinstalar los agentes actuales desde cero.

OBJETIVO

Eliminar la activación por código.

El nuevo flujo debe ser:

Usuario se registra/inicia sesión en la plataforma web → crea o administra su negocio → crea sus sucursales → entra a una sucursal → descarga el instalador universal del conector → instala el agente → abre la GUI local → inicia sesión con su cuenta del SaaS → selecciona negocio/sucursal → vincula esa computadora → el backend genera una identidad propia para esa instalación → el servicio Windows queda funcionando permanentemente con esa identidad.

AUTENTICACIÓN

Separar completamente:

1. Autenticación humana.
2. Autenticación del conector/dispositivo.

La cuenta del usuario se utiliza únicamente para entrar al SaaS y autorizar el vínculo de una computadora.

Después de “Vincular este equipo”, el backend debe generar una identidad/credencial independiente para esa instalación.

El servicio Windows NO debe guardar email, contraseña, sesión web ni refresh token del usuario.

Debe guardar únicamente las credenciales necesarias de su propia identidad de dispositivo, protegidas correctamente con DPAPI y disponibles para el contexto del servicio Windows.

Cambiar contraseña, cerrar sesiones del usuario o cerrar la GUI NO debe detener ni desvincular el servicio.

MODELO SAAS

Mantener una jerarquía limpia:

Account/User
→ Business
→ Branch
→ ConnectorInstallation

Una cuenta puede tener acceso a varios negocios.
Un negocio puede tener varias sucursales.
Una sucursal puede tener historial de varias instalaciones/conectores.

No modeles físicamente Branch → un único Connector.

Debe poder existir historial de equipos reemplazados/revocados, aunque inicialmente solo permitamos un conector principal/activo encargado de la extracción por sucursal.

Cada ConnectorInstallation debe tener una identidad propia y manejar los campos necesarios para:

installation/connector ID
branch
device identity
machine information
version
created/linked/revoked timestamps
last heartbeat
last successful sync
sync state
last error
syncRequestedAt
estado activo/revocado
y demás información técnica que ya utilice el sistema.

Evita duplicar columnas/conceptos que ya existan si pueden reutilizarse limpiamente.

VINCULACIÓN

Después del login en extractor-ui:

Mostrar únicamente negocios/sucursales que ese usuario tenga permiso de administrar.

El usuario selecciona una sucursal.

Mostrar “Vincular este equipo”.

Antes de vincular, detectar si esa sucursal ya tiene un conector activo.

Si existe, NO crear silenciosamente otro extractor activo.

Mostrar el estado y permitir un flujo explícito de reemplazo/revinculación del equipo.

Al confirmar el vínculo, central-api crea la identidad del dispositivo y devuelve las credenciales correspondientes.

El agente las persiste mediante DPAPI.

A partir de ahí toda comunicación agente → API utiliza identidad del dispositivo, no identidad del usuario.

REVOCACIÓN

Desde dashboard/admin debe poder verse qué computadora está vinculada a cada sucursal.

Permitir:

Ver estado.
Ver versión.
Ver último heartbeat.
Ver última sincronización.
Ver último error.
Revocar/desvincular equipo.
Reemplazar equipo.

Una credencial revocada debe dejar de funcionar inmediatamente desde central-api aunque la computadora esté offline y todavía conserve físicamente su token.

SOFT RESTAURANT

Este conector está diseñado específicamente para la estructura de datos compatible con Soft Restaurant 11.

NO necesito implementar compatibilidad con Soft Restaurant 12 ni crear un sistema complejo de múltiples versiones en este momento.

Nuestro sistema es independiente de Soft Restaurant. No necesitamos consultar, validar ni almacenar información relacionada con el estado de licencia de Soft Restaurant.

El agente realmente trabaja contra SQL Server y la base de datos utilizada por Soft Restaurant.

Durante onboarding/configuración, automatiza todo lo razonablemente posible.

Si existe configuración local de Soft Restaurant desde donde podamos obtener servidor/instancia/base u otros datos necesarios, reutilízala.

Usa las credenciales/configuración SQL que ya maneja el proyecto cuando corresponda.

Antes de aceptar una base como válida, realiza una validación ligera del esquema para confirmar que corresponde a la estructura que nuestro extractor soporta.

No modifiques la lógica probada de extracción/reconciliación salvo donde sea necesario para integrarla con el nuevo onboarding.

INSTALADOR

Debe existir un instalador universal.

No generar instaladores distintos por cliente, negocio o sucursal.

El instalador:

Instala el Windows Service.
Instala extractor-ui.
Configura correctamente el servicio.
Configura recuperación automática ante fallos.
Instala/accede a la GUI de bandeja.
No requiere código de activación.
No requiere conocer previamente negocio ni sucursal.

Después de instalar, el usuario abre la GUI y realiza el login/vinculación.

El servicio debe poder arrancar automáticamente con Windows aunque ningún usuario haya iniciado sesión.

La GUI es independiente.

Cerrar la GUI NO detiene el servicio.

“Salir” solamente termina extractor-ui.

Mantén AgentControlServer limitado a loopback.

Mantén la separación actual entre GUI y servicio.

ARQUITECTURA EXISTENTE

Conservar las partes que acabamos de implementar y que siguen siendo válidas:

AgentStatusStore.
AgentLog.
SyncCoordinator.
AgentControlServer.
HeartbeatWorker independiente.
SyncWorker.
request-sync remoto.
estado online/offline.
diagnósticos.
logs.
dashboard de conectores.

Adapta estas piezas a la nueva identidad de dispositivo en lugar de la activación anterior.

Heartbeat y sincronización deben utilizar la nueva autenticación del ConnectorInstallation.

SINCRONIZACIÓN REMOTA

Mantener:

Panel web → Sincronizar ahora → central-api registra syncRequestedAt → heartbeat del agente detecta la solicitud → SyncCoordinator ejecuta sincronización.

No abrir puertos entrantes en el restaurante.

No exponer AgentControlServer fuera de 127.0.0.1.

MULTI-TENANT

Revisa especialmente autorización y aislamiento.

Todo endpoint que trabaje con:

businessId
branchCode/branchId
connectorId/installationId

debe validar en backend que el usuario tenga acceso real a ese recurso.

Nunca depender únicamente de lo que oculta/muestra el frontend.

Una cuenta de un cliente jamás debe poder consultar, vincular, sincronizar, revocar o acceder a información de otro tenant manipulando IDs manualmente.

DESCARGA

Desde el dashboard agrega una sección clara para instalar el conector.

El usuario debe poder entrar a su sucursal y encontrar una acción tipo:

“Instalar conector”

Desde ahí descargar el instalador universal actual.

Deja la arquitectura preparada para mostrar:

versión actual
versión instalada
actualización disponible

No implementes un sistema excesivamente complejo de deployment si todavía no hace falta.

ACTUALIZACIONES

Mantén/prepara el mecanismo de actualización del agente de forma que posteriormente podamos administrar muchas instalaciones.

Una actualización no debe borrar:

identidad del dispositivo
vinculación
configuración SQL
configuración necesaria del agente

La desinstalación completa sí puede ofrecer la posibilidad de eliminar estos datos.

SAAS FUTURO

Deja el modelo preparado para posteriormente agregar:

planes
suscripciones
límites de sucursales
límites de conectores
suspensión por falta de pago

NO implementes billing ni pasarela de pago ahora.

No metas complejidad que todavía no necesitamos.

LIMPIEZA DEL SISTEMA ANTERIOR

Elimina/refactoriza el sistema de activation codes donde deje de tener sentido.

No quiero mantener dos sistemas paralelos:

activation code + account linking.

La nueva fuente de verdad debe ser Account/Business/Branch/ConnectorInstallation.

Si existen tablas, endpoints, UI, configuración o código muerto perteneciente exclusivamente al sistema anterior de activaciones, elimínalos si ya no tienen función.

No hagas parches para mantener retrocompatibilidad.

PRUEBAS

Al terminar quiero validar el flujo E2E real:

Registro de cuenta.
Login web.
Creación/acceso a negocio.
Creación/acceso a sucursal.
Descarga del instalador.
Instalación limpia.
Windows Service funcionando.
Apertura de extractor-ui.
Login.
Listado correcto de negocios/sucursales autorizados.
Selección de sucursal.
Detección/configuración SQL.
Validación del esquema.
Vinculación.
Creación de ConnectorInstallation.
Persistencia segura de identidad.
Heartbeat.
Primera sincronización.
Visualización desde dashboard.
Sincronizar ahora desde web.
Ejecución mediante SyncCoordinator.
Cerrar GUI y comprobar que servicio continúa.
Reiniciar Windows y comprobar funcionamiento sin login.
Pérdida y recuperación de Internet.
Revocación desde dashboard.
Comprobar que credencial revocada es rechazada.
Reemplazar equipo.
Comprobar que no quedan dos extractores principales sincronizando la misma sucursal.
Actualizar agente y comprobar persistencia de configuración/vinculación.

Finalmente ejecuta build/test de todos los proyectos afectados y corrige cualquier regresión.

No reescribas componentes que ya funcionan solamente por cambiar arquitectura. Haz el cambio de autenticación/onboarding de forma limpia sobre lo que acabamos de construir y conserva la lógica estable de extracción y reconciliación.