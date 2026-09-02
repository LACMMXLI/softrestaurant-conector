import { useEffect, useState } from 'react'
import { Download, PlusSquare, Share, Smartphone, X } from 'lucide-react'
import { usePwaInstall } from '../hooks/usePwaInstall'

const dismissedKey = 'sr-dashboard:v1:install-dismissed'

export function InstallAppButton() {
  const { installed, ios, canPromptNatively, promptInstall } = usePwaInstall()
  const [open, setOpen] = useState(false)
  const [dismissed, setDismissed] = useState(() => localStorage.getItem(dismissedKey) === '1')
  const [requesting, setRequesting] = useState(false)

  useEffect(() => {
    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') setOpen(false)
    }
    if (open) document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [open])

  if (installed) return null

  function dismiss() {
    localStorage.setItem(dismissedKey, '1')
    setDismissed(true)
    setOpen(false)
  }

  async function handleNativeInstall() {
    setRequesting(true)
    try {
      const outcome = await promptInstall()
      if (outcome === 'accepted') setOpen(false)
    } finally {
      setRequesting(false)
    }
  }

  return (
    <>
      <button
        className="icon-button install-app-button"
        type="button"
        onClick={() => setOpen(true)}
        aria-label="Instalar RestaurantAgent en este dispositivo"
      >
        <Download size={18} />
        {!dismissed ? <span className="install-app-dot" aria-hidden="true" /> : null}
      </button>

      {open ? (
        <div className="sheet-backdrop" role="presentation" onMouseDown={(event) => {
          if (event.target === event.currentTarget) setOpen(false)
        }}>
          <dialog className="ticket-sheet install-app-sheet" open aria-labelledby="install-app-title">
            <div className="sheet-handle" aria-hidden="true" />
            <button className="icon-button sheet-close" type="button" onClick={() => setOpen(false)} aria-label="Cerrar">
              <X size={20} />
            </button>

            <header className="sheet-header">
              <p className="utility-label">App instalable</p>
              <h2 id="install-app-title"><Smartphone size={20} aria-hidden="true" /> Instala RestaurantAgent</h2>
              <p>Agrégalo a tu pantalla de inicio para abrirlo como una app, con acceso directo y notificaciones sin depender del navegador.</p>
            </header>

            {canPromptNatively ? (
              <button className="primary-button install-app-cta" type="button" onClick={() => void handleNativeInstall()} disabled={requesting}>
                <span>{requesting ? 'Abriendo instalador…' : 'Instalar app'}</span>
                <Download size={18} aria-hidden="true" />
              </button>
            ) : ios ? (
              <ol className="install-app-steps">
                <li><Share size={16} aria-hidden="true" /> Toca el botón <strong>Compartir</strong> en Safari.</li>
                <li><PlusSquare size={16} aria-hidden="true" /> Elige <strong>Agregar a inicio</strong>.</li>
                <li>Confirma tocando <strong>Agregar</strong>.</li>
              </ol>
            ) : (
              <ol className="install-app-steps">
                <li><Smartphone size={16} aria-hidden="true" /> Abre el menú de tu navegador (⋮ o …).</li>
                <li><PlusSquare size={16} aria-hidden="true" /> Busca <strong>Instalar app</strong> o <strong>Agregar a pantalla de inicio</strong>.</li>
                <li>Confirma la instalación.</li>
              </ol>
            )}

            <button className="secondary-button install-app-dismiss" type="button" onClick={dismiss}>
              No volver a mostrar
            </button>
          </dialog>
        </div>
      ) : null}
    </>
  )
}
