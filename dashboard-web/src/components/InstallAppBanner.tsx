import { useState } from 'react'
import { Download, Smartphone, X } from 'lucide-react'
import { usePwaInstall } from '../hooks/usePwaInstall'

const dismissedKey = 'sr-dashboard:v2:install-dismissed'

/**
 * Notificación de instalación de la PWA: se muestra sola (sin que el usuario tenga que
 * encontrar un ícono escondido), igual que el banner de "Instalar app" del sitio de menú.
 * Solo aparece cuando el navegador entrega `beforeinstallprompt`; el botón abre
 * directamente su diálogo nativo y no intenta sustituirlo con instrucciones manuales.
 */
export function InstallAppBanner() {
  const { installed, canPromptNatively, promptInstall } = usePwaInstall()
  const [dismissed, setDismissed] = useState(() => localStorage.getItem(dismissedKey) === '1')
  const [requesting, setRequesting] = useState(false)

  if (installed || !canPromptNatively) return null

  function dismiss() {
    localStorage.setItem(dismissedKey, '1')
    setDismissed(true)
  }

  async function handleCtaClick() {
    setRequesting(true)
    try {
      await promptInstall()
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
        <p className="utility-label">Instalar aplicación</p>
        <h3>RestaurantAgent en tu pantalla</h3>
        <p>Accede más rápido, sin buscar la página en el navegador.</p>

        <button className="primary-button install-banner-cta" type="button" onClick={() => void handleCtaClick()} disabled={requesting}>
          <span>{requesting ? 'Abriendo instalador…' : 'Instalar'}</span>
          <Download size={18} aria-hidden="true" />
        </button>
      </div>
    </aside>
  )
}
