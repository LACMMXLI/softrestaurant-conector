import { useCallback, useEffect, useState } from 'react'

type BeforeInstallPromptEvent = Event & {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

type InstallOutcome = 'accepted' | 'dismissed' | 'unavailable'

function isStandalone(): boolean {
  if (typeof window === 'undefined') return false
  const navigatorWithIosFlag = window.navigator as Navigator & { standalone?: boolean }
  return window.matchMedia('(display-mode: standalone)').matches || navigatorWithIosFlag.standalone === true
}

function isIos(): boolean {
  if (typeof window === 'undefined') return false
  const ua = window.navigator.userAgent
  // iPadOS 13+ reporta como Mac pero con soporte táctil.
  const isIpadOs = /Macintosh/.test(ua) && navigator.maxTouchPoints > 1
  return /iPhone|iPad|iPod/.test(ua) || isIpadOs
}

function isAndroid(): boolean {
  if (typeof window === 'undefined') return false
  return /Android/.test(window.navigator.userAgent)
}

/**
 * Detecta la posibilidad de instalar el dashboard como PWA y expone el flujo nativo
 * (Android/desktop Chrome vía `beforeinstallprompt`) o la señal para mostrar instrucciones
 * manuales (iOS, que no soporta el prompt nativo).
 */
export function usePwaInstall() {
  const [deferredPrompt, setDeferredPrompt] = useState<BeforeInstallPromptEvent | null>(null)
  const [installed, setInstalled] = useState(isStandalone)

  useEffect(() => {
    function onBeforeInstallPrompt(event: Event) {
      event.preventDefault()
      setDeferredPrompt(event as BeforeInstallPromptEvent)
    }
    function onInstalled() {
      setInstalled(true)
      setDeferredPrompt(null)
    }
    window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt)
    window.addEventListener('appinstalled', onInstalled)
    return () => {
      window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt)
      window.removeEventListener('appinstalled', onInstalled)
    }
  }, [])

  const promptInstall = useCallback(async (): Promise<InstallOutcome> => {
    if (!deferredPrompt) return 'unavailable'
    await deferredPrompt.prompt()
    const choice = await deferredPrompt.userChoice
    setDeferredPrompt(null)
    if (choice.outcome === 'accepted') setInstalled(true)
    return choice.outcome
  }, [deferredPrompt])

  return {
    installed,
    ios: isIos(),
    android: isAndroid(),
    canPromptNatively: deferredPrompt !== null,
    promptInstall,
  }
}
