import { LockKeyhole, LogOut } from 'lucide-react'
import type { Subscription } from '../types'

const planNames: Record<Subscription['plan'], string> = { BASIC: 'Estándar', PLUS: 'Plus', UNLIMITED: 'Ilimitado' }

export function SubscriptionSummary({ subscription }: { subscription: Subscription }) {
  const expiresAt = subscription.status === 'ACTIVE'
    ? subscription.paidUntil
    : subscription.status === 'TRIAL' ? subscription.trialEndsAt : null
  if (!expiresAt) return null

  const expiryTimestamp = Date.parse(expiresAt)
  if (!Number.isFinite(expiryTimestamp)) return null
  const daysRemaining = Math.max(0, Math.ceil((expiryTimestamp - Date.now()) / 86_400_000))
  const dateLabel = new Date(expiryTimestamp).toLocaleDateString('es-MX', { day: 'numeric', month: 'short', year: 'numeric' })
  const remainingLabel = daysRemaining === 0 ? 'vence hoy' : `vence en ${daysRemaining} ${daysRemaining === 1 ? 'día' : 'días'}`

  return (
    <aside className="subscription-summary" aria-label="Información del plan">
      <span>{subscription.status === 'TRIAL' ? 'Periodo de prueba' : `Plan ${planNames[subscription.plan]}`}</span>
      <span className="subscription-summary-divider" aria-hidden="true">·</span>
      <span>{remainingLabel} <time dateTime={expiresAt}>({dateLabel})</time></span>
    </aside>
  )
}

export function SubscriptionAccessNotice({ onLogout }: { onLogout: () => void }) {
  return (
    <main className="subscription-wall">
      <section className="subscription-wall-card" role="alert" aria-labelledby="subscription-title">
        <span className="subscription-wall-icon"><LockKeyhole size={28} strokeWidth={2} /></span>
        <h1 id="subscription-title">Cuenta inactiva</h1>
        <p className="subscription-wall-message">Tu acceso al sistema se encuentra temporalmente suspendido porque no tienes un plan activo.</p>
        <div className="subscription-wall-info">
          <strong>Acceso temporalmente restringido</strong>
          <p>Tu información permanece guardada. Para continuar utilizando el sistema, solicita la activación o renovación de tu cuenta.</p>
        </div>
        <button className="subscription-wall-button" type="button" onClick={onLogout}><LogOut size={17} /> Cerrar sesión</button>
      </section>
    </main>
  )
}
