/** Resultado de comparar lo que los cajeros declararon contra lo que debía haber en caja. */
export type CashVerdict = 'balanced' | 'short' | 'over' | 'pending'

/** Por debajo de medio centavo es redondeo, no una diferencia real. */
const CENT = 0.005

/**
 * `difference` llega del servidor como declarado − esperado: negativa = faltante (los cajeros
 * entregaron menos de lo que debía haber), positiva = sobrante.
 */
export function cashVerdict(difference: number | null): CashVerdict {
  if (difference === null) return 'pending'
  if (Math.abs(difference) < CENT) return 'balanced'
  return difference < 0 ? 'short' : 'over'
}
