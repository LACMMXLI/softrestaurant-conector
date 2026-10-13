import { useEffect, useState } from 'react'
import { Check, LoaderCircle, PanelsTopLeft, Save } from 'lucide-react'
import { api, ApiError } from '../api'
import type { SubscriptionNoticeDesign } from '../types'

export function SubscriptionNoticeScreen({ onUnauthorized }: { onUnauthorized: () => void }) {
  const [design, setDesign] = useState<SubscriptionNoticeDesign>('A')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    let active = true
    api.subscriptionNoticeDesign().then(({ design: current }) => {
      if (active) setDesign(current)
    }).catch((reason) => {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      if (active) setError(reason instanceof Error ? reason.message : 'No se pudo cargar el diseño guardado.')
    }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [onUnauthorized])

  async function save() {
    setSaving(true)
    setError(null)
    setSaved(false)
    try {
      await api.setSubscriptionNoticeDesign(design)
      setSaved(true)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No se pudo guardar el diseño.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="panel-stack notice-settings">
      <header className="page-heading">
        <div><p className="eyebrow">Experiencia del cliente</p><h1>Avisos de acceso</h1><p>Elige cómo se verá el mensaje cuando una membresía venza o una cuenta se pause por falta de pago.</p></div>
      </header>
      <section className="panel-card notice-settings-card">
        <div className="panel-card-header"><h2>Diseño que verá el cliente</h2><PanelsTopLeft size={20} /></div>
        {loading ? <p className="panel-hint">Cargando preferencia guardada…</p> : (
          <div className="notice-design-options" role="radiogroup" aria-label="Diseño del aviso">
            <button type="button" role="radio" aria-checked={design === 'A'} className={`notice-design-choice ${design === 'A' ? 'selected' : ''}`} onClick={() => { setDesign('A'); setSaved(false) }}>
              <span className="notice-choice-title">A · Verde petróleo</span>
              <NoticePreview design="A" />
            </button>
            <button type="button" role="radio" aria-checked={design === 'B'} className={`notice-design-choice ${design === 'B' ? 'selected' : ''}`} onClick={() => { setDesign('B'); setSaved(false) }}>
              <span className="notice-choice-title">B · Claro y coral</span>
              <NoticePreview design="B" />
            </button>
          </div>
        )}
        {error ? <p className="form-error" role="alert">{error}</p> : null}
        {saved ? <p className="notice-saved" role="status"><Check size={16} /> Diseño guardado. Se aplicará al próximo inicio de sesión del cliente.</p> : null}
        <div className="notice-settings-footer">
          <p className="panel-hint">El mensaje cambia automáticamente: falta de pago si la cuenta está suspendida; prueba finalizada o suscripción vencida cuando termina el periodo.</p>
          <button className="primary-button" type="button" onClick={() => void save()} disabled={loading || saving}>
            {saving ? <LoaderCircle size={16} className="spin" /> : <Save size={16} />}
            <span>{saving ? 'Guardando…' : 'Guardar diseño'}</span>
          </button>
        </div>
      </section>
    </div>
  )
}

function NoticePreview({ design }: { design: SubscriptionNoticeDesign }) {
  return (
    <span className={`notice-preview notice-preview-${design.toLowerCase()}`} aria-hidden="true">
      <span className="notice-preview-mark">$</span>
      <span className="notice-preview-copy"><strong>Tu cuenta está deshabilitada por falta de pago</strong><small>Contacta al administrador para reactivarla al realizar tu pago.</small></span>
      <span className="notice-preview-button">Cerrar sesión</span>
    </span>
  )
}
