export type Tab = 'home' | 'sales' | 'operations' | 'cancellations' | 'products' | 'businesses' | 'more' | 'expense-categories'

/** Pestañas que consultan una sucursal concreta; no existen en el resumen general del negocio. */
const branchOnlyTabs: ReadonlySet<Tab> = new Set(['sales', 'operations', 'cancellations', 'expense-categories'])

export const consolidatedScopePrefix = 'all:'

export function isConsolidatedCode(code: string): boolean {
  return code.startsWith(consolidatedScopePrefix)
}

export function businessIdFromCode(code: string): string | null {
  return isConsolidatedCode(code) ? code.slice(consolidatedScopePrefix.length) : null
}

export function isTabAvailable(tab: Tab, consolidated: boolean): boolean {
  return !(consolidated && branchOnlyTabs.has(tab))
}
