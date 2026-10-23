/** Texto en español de las claves de acción que registra la API (ver central-api/ActivityClassifier.cs). */
const ACTION_LABELS: Record<string, string> = {
  app_open: 'Abrió la aplicación',
  'auth.login': 'Inició sesión',
  'auth.login_failed': 'Intento de inicio de sesión fallido',
  'auth.register': 'Creó su cuenta',
  'auth.register_failed': 'Registro rechazado',
  'auth.logout': 'Cerró sesión',
  'dashboard.home': 'Consultó el dashboard de la sucursal',
  'dashboard.business_home': 'Consultó el resumen general del negocio',
  'sales.list': 'Revisó la lista de tickets',
  'ticket.open': 'Abrió el detalle de un ticket',
  'open_account.open': 'Abrió una cuenta abierta',
  'operations.cash_movements': 'Revisó movimientos de caja',
  'operations.expense_summary': 'Consultó el resumen de gastos',
  'cancellations.list': 'Revisó productos cancelados',
  'expense_categories.list': 'Abrió las categorías de gastos',
  'expense_category.create': 'Creó una categoría de gastos',
  'expense_category.update': 'Editó una categoría de gastos',
  'expense_category.delete': 'Eliminó una categoría de gastos',
  'expense.assign_category': 'Corrigió la categoría de un gasto',
  'expense.reset_category': 'Devolvió un gasto a clasificación automática',
  'businesses.list': 'Abrió Mis negocios',
  'business.create': 'Creó un negocio',
  'business.branches': 'Consultó las sucursales de un negocio',
  'branch.create': 'Creó una sucursal',
  'connector.view': 'Consultó el conector de una sucursal',
  'connector.link': 'Vinculó un conector',
  'connector.replace': 'Reemplazó un conector',
  'connector.revoke': 'Revocó un conector',
  'sync.request': 'Solicitó sincronizar una sucursal',
  'installer.latest': 'Consultó el instalador del conector',
  'ui.tab': 'Cambió de pantalla',
  'ui.scope': 'Cambió de sucursal',
  'ui.filter': 'Cambió fecha o turno',
  'ui.refresh': 'Actualizó los datos',
  'api.other': 'Otra consulta',
}

export const EVENT_TYPE_LABELS: Record<string, string> = {
  auth: 'Acceso',
  session: 'Visita',
  view: 'Consulta',
  action: 'Acción',
  context: 'Navegación',
  api: 'Otro',
}

export const VIEW_LABELS: Record<string, string> = {
  home: 'Inicio',
  sales: 'Ventas',
  operations: 'Operación',
  cancellations: 'Cancelados',
  products: 'Productos',
  businesses: 'Negocios',
  more: 'Más',
  'expense-categories': 'Categorías de gastos',
}

export function actionLabel(action: string): string {
  return ACTION_LABELS[action] ?? action
}
