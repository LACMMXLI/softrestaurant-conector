import { useEffect, useState } from 'react'
import { ApiError } from '../api'

type Resource<T> = { data: T | null; loading: boolean; error: string | null }

/**
 * Carga un recurso de la API cancelando la petición anterior cuando cambian las
 * dependencias o el componente se desmonta. Un 401 se delega a `onUnauthorized`; cualquier
 * otro error queda en `error` con `fallbackError` como texto por defecto.
 *
 * `load` se lee solo cuando cambia `deps`, por eso puede ser una función anónima.
 */
export function useApiResource<T>(
  load: (signal: AbortSignal) => Promise<T>,
  deps: readonly unknown[],
  onUnauthorized: () => void,
  fallbackError: string,
): Resource<T> {
  const [state, setState] = useState<Resource<T>>({ data: null, loading: true, error: null })

  useEffect(() => {
    const controller = new AbortController()
    setState({ data: null, loading: true, error: null })
    load(controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) setState({ data, loading: false, error: null })
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted || (reason instanceof DOMException && reason.name === 'AbortError')) return
        if (reason instanceof ApiError && reason.status === 401) onUnauthorized()
        else setState({ data: null, loading: false, error: reason instanceof Error ? reason.message : fallbackError })
      })
    return () => controller.abort()
  }, [...deps, onUnauthorized])

  return state
}
