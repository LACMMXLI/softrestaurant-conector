import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, Copy, KeyRound, RefreshCw, ShieldOff, Trash2 } from 'lucide-react'
import { api, ApiError } from '../api'
import type { Branch, Connector } from '../types'

type ConnectorsScreenProps = {
  branch: Branch
  onUnauthorized: () => void
  onBranchUpdated: (branch: Branch) => void
}

export function ConnectorsScreen({ branch, onUnauthorized, onBranchUpdated }: ConnectorsScreenProps) {
  const [connectors, setConnectors] = useState<Connector[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [minutes, setMinutes] = useState(30)
  const [note, setNote] = useState('')
  const [creating, setCreating] = useState(false)
  const [generatedKey, setGeneratedKey] = useState<{ key: string; expiresAt: string } | null>(null)
  const [rotatedToken, setRotatedToken] = useState<{ connectorId: string; token: string } | null>(null)
  const [busyAction, setBusyAction] = useState<string | null>(null)
  const [copied, setCopied] = useState<string | null>(null)

  async function loadConnectors(branchCode: string) {
    setLoading(true)
    setError(null)
    try {
      setConnectors(await api.connectors(branchCode))
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible cargar los conectores.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    setGeneratedKey(null)
    setRotatedToken(null)
    void loadConnectors(branch.code)
  }, [branch.code])

  async function handleCreateKey(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setCreating(true)
    setError(null)
    try {
      const result = await api.createActivationKey(branch.code, minutes, note)
      setGeneratedKey({ key: result.activationKey, expiresAt: result.expiresAt })
      setNote('')
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible generar la llave.')
    } finally {
      setCreating(false)
    }
  }

  async function handleRevoke(connectorId: string) {
    setBusyAction(connectorId)
    try {
      await api.revokeConnector(connectorId)
      await loadConnectors(branch.code)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible revocar el conector.')
    } finally {
      setBusyAction(null)
    }
  }

  async function handleRotate(connectorId: string) {
    setBusyAction(connectorId)
    try {
      const credential = await api.rotateToken(connectorId)
      setRotatedToken({ connectorId, token: credential.token })
      await loadConnectors(branch.code)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible rotar el token.')
    } finally {
      setBusyAction(null)
    }
  }

  async function handleDisableLegacy() {
    setBusyAction('legacy')
    try {
      await api.disableLegacyAuth(branch.code)
      onBranchUpdated({ ...branch, legacyAuthEnabled: false })
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible desactivar la autenticación legacy.')
    } finally {
      setBusyAction(null)
    }
  }

  async function copyToClipboard(value: string, key: string) {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(key)
      setTimeout(() => setCopied((current) => (current === key ? null : current)), 2000)
    } catch {
      // Portapapeles no disponible; el valor sigue visible en pantalla.
    }
  }

  return (
    <div className="panel-stack">
      <section className="panel-card" aria-labelledby="activation-title">
        <h2 id="activation-title">Nueva llave de activación</h2>
        <p className="panel-hint">
          Se usa una sola vez desde el instalador del conector para la sucursal <strong>{branch.name}</strong>.
        </p>
        <form className="inline-form" onSubmit={handleCreateKey}>
          <label>
            Expira en (minutos)
            <input
              type="number"
              min={1}
              max={10080}
              value={minutes}
              onChange={(event) => setMinutes(Number(event.target.value))}
              required
            />
          </label>
          <label>
            Nota (opcional)
            <input
              type="text"
              maxLength={500}
              value={note}
              onChange={(event) => setNote(event.target.value)}
              placeholder="Ej. caja 2, instalación 2026-08-29"
            />
          </label>
          <button className="primary-button" type="submit" disabled={creating}>
            <KeyRound size={17} aria-hidden="true" />
            <span>{creating ? 'Generando…' : 'Generar llave'}</span>
          </button>
        </form>

        {generatedKey ? (
          <div className="secret-box" role="status">
            <p>Llave de activación (solo se muestra una vez):</p>
            <div className="secret-value">
              <code>{generatedKey.key}</code>
              <button
                type="button"
                className="icon-button"
                onClick={() => void copyToClipboard(generatedKey.key, 'activation-key')}
                aria-label="Copiar llave"
              >
                {copied === 'activation-key' ? <Check size={17} /> : <Copy size={17} />}
              </button>
            </div>
            <p className="panel-hint">Expira: {new Date(generatedKey.expiresAt).toLocaleString('es-MX')}</p>
          </div>
        ) : null}
      </section>

      {branch.legacyAuthEnabled ? (
        <section className="panel-card panel-card-warning" aria-labelledby="legacy-title">
          <h2 id="legacy-title">Autenticación legacy activa</h2>
          <p className="panel-hint">
            Esta sucursal todavía acepta el token compartido antiguo. Desactívalo una vez que todos sus
            conectores usen credenciales individuales.
          </p>
          <button
            className="secondary-button"
            type="button"
            onClick={() => void handleDisableLegacy()}
            disabled={busyAction === 'legacy'}
          >
            <ShieldOff size={17} aria-hidden="true" />
            <span>Desactivar auth legacy</span>
          </button>
        </section>
      ) : null}

      <section className="panel-card" aria-labelledby="connectors-title">
        <div className="panel-card-header">
          <h2 id="connectors-title">Conectores</h2>
          <button className="icon-button" type="button" onClick={() => void loadConnectors(branch.code)} aria-label="Actualizar">
            <RefreshCw size={17} className={loading ? 'spinning' : ''} />
          </button>
        </div>

        {error ? <p className="form-error" role="alert">{error}</p> : null}

        {rotatedToken ? (
          <div className="secret-box" role="status">
            <p>Nuevo token del conector (solo se muestra una vez):</p>
            <div className="secret-value">
              <code>{rotatedToken.token}</code>
              <button
                type="button"
                className="icon-button"
                onClick={() => void copyToClipboard(rotatedToken.token, 'rotated-token')}
                aria-label="Copiar token"
              >
                {copied === 'rotated-token' ? <Check size={17} /> : <Copy size={17} />}
              </button>
            </div>
          </div>
        ) : null}

        {loading ? (
          <p className="panel-hint">Cargando conectores…</p>
        ) : connectors.length === 0 ? (
          <p className="panel-hint">Todavía no hay conectores activados para esta sucursal.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Equipo</th>
                  <th>Estado</th>
                  <th>En línea</th>
                  <th>Última sincronización</th>
                  <th>Pendientes</th>
                  <th>Último error</th>
                  <th>Versión</th>
                  <th>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {connectors.map((connector) => (
                  <tr key={connector.id}>
                    <td>{connector.machineName}</td>
                    <td>
                      <span className={connector.active ? 'status-pill status-ok' : 'status-pill status-off'}>
                        {connector.active ? 'Activo' : 'Revocado'}
                      </span>
                    </td>
                    <td>
                      <span className={isOnline(connector.lastHeartbeatAt) ? 'status-pill status-ok' : 'status-pill status-off'}>
                        {isOnline(connector.lastHeartbeatAt) ? 'En línea' : 'Sin latido'}
                      </span>
                    </td>
                    <td>{connector.lastSeenAt ? new Date(connector.lastSeenAt).toLocaleString('es-MX') : 'Nunca'}</td>
                    <td>{connector.pendingBatches ?? '—'}</td>
                    <td className={connector.lastError ? 'form-error' : undefined}>{connector.lastError ?? '—'}</td>
                    <td>{connector.agentVersion ?? '—'}</td>
                    <td className="table-actions">
                      <button
                        type="button"
                        className="icon-button"
                        title="Rotar token"
                        aria-label="Rotar token"
                        disabled={!connector.active || busyAction === connector.id}
                        onClick={() => void handleRotate(connector.id)}
                      >
                        <RefreshCw size={16} />
                      </button>
                      <button
                        type="button"
                        className="icon-button icon-button-danger"
                        title="Revocar conector"
                        aria-label="Revocar conector"
                        disabled={!connector.active || busyAction === connector.id}
                        onClick={() => void handleRevoke(connector.id)}
                      >
                        <Trash2 size={16} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}

// Heartbeat esperado cada 30-60s (ver extractor/HeartbeatWorker.cs); 3 minutos sin latido ya
// es señal razonable de que el agente está apagado, sin SQL/red, o el servicio está detenido.
const ONLINE_THRESHOLD_MS = 3 * 60 * 1000

function isOnline(lastHeartbeatAt: string | null): boolean {
  if (!lastHeartbeatAt) return false
  return Date.now() - new Date(lastHeartbeatAt).getTime() < ONLINE_THRESHOLD_MS
}
