import { useCallback, useEffect, useRef, useState } from 'react'

type Options = {
  /** Mientras la página esté a esta distancia del inicio, el elemento siempre se ve. */
  revealWithin?: number
  /** Desplazamiento mínimo (px) para considerar que el usuario cambió de dirección. */
  tolerance?: number
}

/**
 * Oculta un elemento fijo al bajar por la página y lo vuelve a mostrar al subir o al volver
 * al inicio. Devuelve `[hidden, reveal]`; `reveal` sirve para mostrarlo por otra causa, como
 * cuando el foco del teclado entra a él.
 */
export function useHideOnScroll({ revealWithin = 64, tolerance = 8 }: Options = {}): [boolean, () => void] {
  const [hidden, setHidden] = useState(false)
  const lastY = useRef(0)

  useEffect(() => {
    lastY.current = window.scrollY
    let frame = 0

    function update() {
      frame = 0
      const y = Math.max(0, window.scrollY)
      const delta = y - lastY.current
      // El rebote al llegar al final de la página (iOS) se ve como un "subir" falso: se ignora.
      const bouncingAtBottom = y + window.innerHeight >= document.documentElement.scrollHeight - 2

      if (y <= revealWithin) setHidden(false)
      else if (!bouncingAtBottom && delta < -tolerance) setHidden(false)
      else if (delta > tolerance) setHidden(true)

      // Solo se avanza la referencia cuando hubo un movimiento claro, así los desplazamientos pequeños se acumulan.
      if (Math.abs(delta) > tolerance || y <= revealWithin) lastY.current = y
    }

    function onScroll() {
      if (frame === 0) frame = window.requestAnimationFrame(update)
    }

    window.addEventListener('scroll', onScroll, { passive: true })
    return () => {
      window.removeEventListener('scroll', onScroll)
      if (frame !== 0) window.cancelAnimationFrame(frame)
    }
  }, [revealWithin, tolerance])

  const reveal = useCallback(() => {
    lastY.current = window.scrollY
    setHidden(false)
  }, [])

  return [hidden, reveal]
}
