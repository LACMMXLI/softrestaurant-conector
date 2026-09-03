import { useState } from 'react'
import { Check, Sparkles, X } from 'lucide-react'

const dismissedKey = 'sr-dashboard:v1:pricing-dismissed'

type Plan = {
  key: string
  name: string
  price: string
  period: string
  tag?: string
  highlight?: boolean
  features: string[]
}

const PLANS: Plan[] = [
  {
    key: 'basic',
    name: 'Basic',
    price: '$199',
    period: 'MXN / mes',
    features: [
      '1 sucursal',
      'Historial de hasta 3 días',
      'Sincronización en vivo (cuentas abiertas y corte de caja)',
      'Corte de caja estimado y desglose por forma de pago',
      'Tickets recientes y cancelaciones',
    ],
  },
  {
    key: 'plus',
    name: 'Plus',
    price: '$499',
    period: 'MXN / mes',
    tag: 'Más completo',
    highlight: true,
    features: [
      'Hasta 5 sucursales',
      'Historial extendido de hasta 7 días',
      'Analítica avanzada: tendencias, productos top y comparativos entre sucursales',
      'Alertas automáticas de diferencias de caja y cancelaciones inusuales',
      'Exportación de reportes a Excel y PDF',
      'Soporte prioritario',
    ],
  },
]

export function PricingBanner() {
  const [dismissed, setDismissed] = useState(() => localStorage.getItem(dismissedKey) === '1')

  if (dismissed) return null

  function dismiss() {
    localStorage.setItem(dismissedKey, '1')
    setDismissed(true)
  }

  return (
    <section className="content-card pricing-banner" aria-labelledby="pricing-title">
      <button className="pricing-dismiss" type="button" onClick={dismiss} aria-label="Ocultar planes">
        <X size={16} />
      </button>

      <div className="pricing-trial">
        <span className="pricing-trial-badge"><Sparkles size={15} /> Prueba gratuita</span>
        <h2 id="pricing-title">15 días con todos los beneficios, sin costo</h2>
        <p>Tienes acceso completo sin costo durante 15 días. Estos son los planes disponibles cuando termine tu prueba:</p>
      </div>

      <div className="pricing-grid">
        {PLANS.map((plan) => (
          <article className={plan.highlight ? 'pricing-card pricing-card-highlight' : 'pricing-card'} key={plan.key}>
            {plan.tag ? <span className="pricing-tag">{plan.tag}</span> : null}
            <p className="utility-label">Plan {plan.name}</p>
            <div className="pricing-amount">
              <strong>{plan.price}</strong>
              <span>{plan.period}</span>
            </div>
            <ul className="pricing-features">
              {plan.features.map((feature) => (
                <li key={feature}><Check size={15} aria-hidden="true" /><span>{feature}</span></li>
              ))}
            </ul>
          </article>
        ))}
      </div>

      <p className="data-note">Precios de referencia en pesos mexicanos. No se realiza ningún cobro desde aquí — esta información es solo para que conozcas los planes disponibles.</p>
    </section>
  )
}
