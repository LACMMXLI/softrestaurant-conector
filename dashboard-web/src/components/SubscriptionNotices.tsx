import { useEffect, useRef } from 'react'
import { CalendarClock, LockKeyhole, LogOut, X } from 'lucide-react'
import { describeRemainingDays, getSubscriptionExpiry } from '../lib/subscription'
import type { SubscriptionExpiry } from '../lib/subscription'
import type { Subscription } from '../types'

const planNames: Record<Subscription['plan'], string> = { BASIC: 'Estándar', PLUS: 'Plus', UNLIMITED: 'Ilimitado' }

export function SubscriptionSummary({ subscription }: { subscription: Subscription }) {
  const expiry = getSubscriptionExpiry(subscription)
  if (!expiry) return null

  const dateLabel = new Date(expiry.expiryTimestamp).toLocaleDateString('es-MX', { day: 'numeric', month: 'short', year: 'numeric' })
  const remainingLabel = expiry.daysRemaining === 0 ? 'vence hoy' : `vence en ${expiry.daysRemaining} ${expiry.daysRemaining === 1 ? 'día' : 'días'}`

  return (
    <aside className="subscription-summary" aria-label="Información del plan">
      <span>{expiry.isTrial ? 'Periodo de prueba' : `Plan ${planNames[subscription.plan]}`}</span>
      <span className="subscription-summary-divider" aria-hidden="true">·</span>
      <span>{remainingLabel} <time dateTime={expiry.expiresAt}>({dateLabel})</time></span>
    </aside>
  )
}

/**
 * Aviso emergente de cuenta por vencer. Es un diálogo modal nativo (foco atrapado, Esc cierra).
 * Se puede cerrar, pero el estado vive solo en memoria: vuelve a salir al iniciar sesión o recargar.
 */
export function ExpiryNoticeDialog({ expiry, onClose }: { expiry: SubscriptionExpiry; onClose: () => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const acceptRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const dialog = dialogRef.current
    if (dialog && !dialog.open) dialog.showModal()
    // showModal() enfoca el primer control (la X); el foco útil es la acción principal.
    acceptRef.current?.focus()
  }, [])

  const urgent = expiry.daysRemaining <= 1
  const when = describeRemainingDays(expiry.daysRemaining)
  const dateLabel = new Date(expiry.expiryTimestamp).toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })

  return (
    <dialog
      ref={dialogRef}
      className={urgent ? 'expiry-dialog expiry-dialog--urgent' : 'expiry-dialog'}
      aria-labelledby="expiry-title"
      aria-describedby="expiry-message"
      onCancel={(event) => { event.preventDefault(); onClose() }}
      onClick={(event) => { if (event.target === event.currentTarget) onClose() }}
    >
      <button className="icon-button expiry-close" type="button" onClick={onClose} aria-label="Cerrar aviso">
        <X size={18} />
      </button>
      <span className="expiry-icon" aria-hidden="true"><CalendarClock size={28} /></span>
      <h2 id="expiry-title">{expiry.isTrial ? 'Tu periodo de prueba está por terminar' : 'Tu cuenta está por vencer'}</h2>
      <p className="expiry-when">
        {expiry.daysRemaining === 0 ? 'Vence hoy' : `Vence ${when}`}
        <small>{dateLabel}</small>
      </p>
      <p id="expiry-message">
        Contacta con el administrador de la plataforma para renovar tu plan y no perder el acceso a tu sistema ni a tu información.
      </p>
      <button ref={acceptRef} className="primary-button expiry-accept" type="button" onClick={onClose}>Entendido</button>
    </dialog>
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
