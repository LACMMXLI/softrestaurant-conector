import { useState } from 'react'
import { Download, PlusSquare, Share, Smartphone, X } from 'lucide-react'
import { usePwaInstall } from '../hooks/usePwaInstall'

const dismissedKey = 'sr-dashboard:v1:install-dismissed'

/**
 * Notificación de instalación de la PWA: se muestra sola (sin que el usuario tenga que
 * encontrar un ícono escondido), igual que el banner de "Instalar app" del sitio de menú.
 * En Android/Chrome dispara el instalador nativo de un toque; en iOS, que no soporta ese
 * prompt, expande los 3 pasos manuales (Compartir → Agregar a inicio).
 */
export function InstallAppBanner() {
  const { installed, ios, canPromptNatively, promptInstall } = usePwaInstall()
  const [dismissed, setDismissed] = useState(() => localStorage.getItem(dismissedKey) === '1')
  const [showSteps, setShowSteps] = useState(false)
  const [requesting, setRequesting] = useState(false)

  if (installed) return null

  function dismiss() {
    localStorage.setItem(dismissedKey, '1')
    setDismissed(true)
    setShowSteps(false)
  }

  async function handleCtaClick() {
    if (!canPromptNatively) {
      setShowSteps((value) => !value)
      return
    }
    setRequesting(true)
    try {
      const outcome = await promptInstall()
      if (outcome === 'accepted') dismiss()
    } finally {
      setRequesting(false)
    }
  }

  if (dismissed) {
    return (
      <button className="install-fab" type="button" onClick={() => setDismissed(false)} aria-label="Instalar RestaurantAgent en este dispositivo">
        <Download size={20} />
      </button>
    )
  }

  return (
    <aside className="install-banner" aria-label="Instalar RestaurantAgent en este dispositivo">
      <button className="icon-button install-banner-close" type="button" onClick={dismiss} aria-label="Cerrar">
        <X size={16} />
      </button>
      <span className="install-banner-icon"><Smartphone size={20} aria-hidden="true" /></span>
      <div className="install-banner-body">
        <p className="utility-label">Instalar app</p>
        <h3>RestaurantAgent en tu pantalla</h3>
        <p>Accede más rápido, sin buscar la página en el navegador.</p>

        <button className="primary-button install-banner-cta" type="button" onClick={() => void handleCtaClick()} disabled={requesting}>
          <span>{requesting ? 'Abriendo instalador…' : 'Descargar aplicación'}</span>
          <Download size={18} aria-hidden="true" />
        </button>

        {showSteps ? (
          <ol className="install-banner-steps">
            {ios ? (
              <>
                <li><Share size={14} aria-hidden="true" /> Toca <strong>Compartir</strong> en Safari.</li>
                <li><PlusSquare size={14} aria-hidden="true" /> Elige <strong>Agregar a inicio</strong>.</li>
                <li>Confirma tocando <strong>Agregar</strong>.</li>
              </>
            ) : (
              <>
                <li><Smartphone size={14} aria-hidden="true" /> Abre el menú (⋮ o …) del navegador.</li>
                <li><PlusSquare size={14} aria-hidden="true" /> Busca <strong>Instalar app</strong> o <strong>Agregar a pantalla de inicio</strong>.</li>
                <li>Confirma la instalación.</li>
              </>
            )}
          </ol>
        ) : null}
      </div>
    </aside>
  )
}
