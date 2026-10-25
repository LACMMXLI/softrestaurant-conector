import type { Subscription } from '../types'

/** Desde cuántos días antes del vencimiento se muestra el aviso emergente. */
export const EXPIRY_WARNING_DAYS = 7

export type SubscriptionExpiry = {
  expiresAt: string
  expiryTimestamp: number
  /** Días de calendario (hora local) que faltan: 0 = vence hoy, 1 = mañana. */
  daysRemaining: number
  isTrial: boolean
}

const startOfLocalDay = (timestamp: number) => {
  const day = new Date(timestamp)
  day.setHours(0, 0, 0, 0)
  return day.getTime()
}

/** Fecha en la que termina el acceso: fin de la prueba o fin del plan pagado. */
export function getSubscriptionExpiry(subscription: Subscription, now = Date.now()): SubscriptionExpiry | null {
  const expiresAt = subscription.status === 'ACTIVE'
    ? subscription.paidUntil
    : subscription.status === 'TRIAL' ? subscription.trialEndsAt : null
  if (!expiresAt) return null

  const expiryTimestamp = Date.parse(expiresAt)
  if (!Number.isFinite(expiryTimestamp)) return null
  return {
    expiresAt,
    expiryTimestamp,
    // round (no floor/ceil) absorbe los días de 23/25 h por cambio de horario.
    daysRemaining: Math.max(0, Math.round((startOfLocalDay(expiryTimestamp) - startOfLocalDay(now)) / 86_400_000)),
    isTrial: subscription.status === 'TRIAL',
  }
}

/** Solo avisa a cuentas que todavía tienen acceso: una vencida o suspendida ya ve el muro de "Cuenta inactiva". */
export function getExpiryWarning(subscription: Subscription | null, now = Date.now()): SubscriptionExpiry | null {
  if (!subscription || !subscription.canAccessContent) return null
  const expiry = getSubscriptionExpiry(subscription, now)
  return expiry && expiry.daysRemaining <= EXPIRY_WARNING_DAYS ? expiry : null
}

export function describeRemainingDays(daysRemaining: number): string {
  if (daysRemaining === 0) return 'hoy'
  if (daysRemaining === 1) return 'mañana'
  return `en ${daysRemaining} días`
}
