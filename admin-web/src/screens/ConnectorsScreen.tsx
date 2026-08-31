import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, Copy, Laptop, RefreshCw, Trash2 } from 'lucide-react'
import { api, ApiError } from '../api'
import type { Branch, ConnectorInstallation } from '../types'

type ConnectorsScreenProps = {
  branch: Branch
  onUnauthorized: () => void
}

/// Reemplaza al antiguo generador de claves de activación: ahora el vínculo lo hace el usuario
/// desde la GUI del agente con su propia sesión (ver extractor-ui/BusinessBranchPickerForm.cs).
/// Esta pantalla es historial + herramientas de soporte para un operador (SUPERADMIN): ver quién
/// está vinculado, revocar, o reemplazar el equipo en nombre de un tenant que no puede hacerlo
/// por sí mismo.
export function ConnectorsScreen({ branch, onUnauthorized }: ConnectorsScreenProps) {
  const [installations, setInstallations] = useState<ConnectorInstallation[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyAction, setBusyAction] = useState<string | null>(null)
  const [replaceMachineName, setReplaceMachineName] = useState('')
  const [replacing, setReplacing] = useState(false)
  const [newCredential, setNewCredential] = useState<{ token: string; installationId: string } | null>(null)
  const [copied, setCopied] = useState(false)

  async function loadInstallations(branchCode: string) {
    setLoading(true)
    setError(null)
    try {
      setInstallations(await api.connectorInstallations(branchCode))
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible cargar el historial de conectores.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    setNewCredential(null)
    void loadInstallations(branch.code)
  }, [branch.code])

  async function handleRevoke(installationId: string) {
    setBusyAction(installationId)
    try {
      await api.revokeConnectorInstallation(installationId)
      await loadInstallations(branch.code)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible revocar el conector.')
    } finally {
      setBusyAction(null)
    }
  }

  async function handleReplace(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!replaceMachineName.trim()) return
    setReplacing(true)
    setError(null)
    try {
      const credential = await api.replaceDevice(branch.code, replaceMachineName.trim())
      setNewCredential({ token: credential.token, installationId: credential.installationId })
      setReplaceMachineName('')
      await loadInstallations(branch.code)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible reemplazar el equipo.')
    } finally {
      setReplacing(false)
    }
  }

  async function copyToken() {
    if (!newCredential) return
    try {
      await navigator.clipboard.writeText(newCredential.token)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Portapapeles no disponible; el valor sigue visible en pantalla.
    }
  }

  const active = installations.find((i) => i.active) ?? null

  return (
    <div className="panel-stack">
      <section className="panel-card" aria-labelledby="replace-title">
        <h2 id="replace-title">Reemplazar equipo (soporte)</h2>
        <p className="panel-hint">
          El vínculo normal lo hace el propietario/gerente del negocio desde el panel del agente
          (inicia sesión ahí y elige "Vincular este equipo"). Usa esto solo cuando el tenant no
          puede hacerlo por sí mismo: emite una credencial nueva y revoca la anterior de inmediato.
        </p>
        <form className="inline-form" onSubmit={handleReplace}>
          <label>
            Nombre del nuevo equipo
            <input
              type="text"
              maxLength={200}
              value={replaceMachineName}
              onChange={(event) => setReplaceMachineName(event.target.value)}
              placeholder="Ej. CAJA-2"
              required
            />
          </label>
          <button className="primary-button" type="submit" disabled={replacing}>
            <Laptop size={17} aria-hidden="true" />
            <span>{replacing ? 'Reemplazando…' : 'Reemplazar equipo'}</span>
          </button>
        </form>

        {newCredential ? (
          <div className="secret-box" role="status">
            <p>Token del nuevo dispositivo (solo se muestra una vez — pégalo con <code>--import-connector-credential</code> si necesitas configurarlo manualmente):</p>
            <div className="secret-value">
              <code>{newCredential.token}</code>
              <button type="button" className="icon-button" onClick={() => void copyToken()} aria-label="Copiar token">
                {copied ? <Check size={17} /> : <Copy size={17} />}
              </button>
            </div>
          </div>
        ) : null}
      </section>

      <section className="panel-card" aria-labelledby="installations-title">
        <div className="panel-card-header">
          <h2 id="installations-title">Historial de instalaciones</h2>
          <button className="icon-button" type="button" onClick={() => void loadInstallations(branch.code)} aria-label="Actualizar">
            <RefreshCw size={17} className={loading ? 'spinning' : ''} />
          </button>
        </div>

        {error ? <p className="form-error" role="alert">{error}</p> : null}

        {!active && !loading ? (
          <p className="panel-hint">Esta sucursal no tiene ningún conector activo en este momento.</p>
        ) : null}

        {loading ? (
          <p className="panel-hint">Cargando…</p>
        ) : installations.length === 0 ? (
          <p className="panel-hint">Todavía no hay ningún equipo vinculado a esta sucursal.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Equipo</th>
                  <th>Estado</th>
                  <th>En línea</th>
                  <th>Última sincronización correcta</th>
                  <th>Último error</th>
                  <th>Versión</th>
                  <th>Vinculado</th>
                  <th>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {installations.map((installation) => (
                  <tr key={installation.id}>
                    <td>{installation.machineName}</td>
                    <td>
                      <span className={installation.active ? 'status-pill status-ok' : 'status-pill status-off'}>
                        {installation.active ? 'Activo' : 'Revocado'}
                      </span>
                    </td>
                    <td>
                      <span className={isOnline(installation.lastHeartbeatAt) ? 'status-pill status-ok' : 'status-pill status-off'}>
                        {isOnline(installation.lastHeartbeatAt) ? 'En línea' : 'Sin latido'}
                      </span>
                    </td>
                    <td>{installation.lastSuccessAt ? new Date(installation.lastSuccessAt).toLocaleString('es-MX') : 'Nunca'}</td>
                    <td className={installation.lastError ? 'form-error' : undefined}>{installation.lastError ?? '—'}</td>
                    <td>{installation.agentVersion ?? '—'}</td>
                    <td>{installation.linkedAt ? new Date(installation.linkedAt).toLocaleString('es-MX') : '—'}</td>
                    <td className="table-actions">
                      <button
                        type="button"
                        className="icon-button icon-button-danger"
                        title="Revocar"
                        aria-label="Revocar conector"
                        disabled={!installation.active || busyAction === installation.id}
                        onClick={() => void handleRevoke(installation.id)}
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
