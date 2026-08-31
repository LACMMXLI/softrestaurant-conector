import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Building2, Download, Plus, Server } from 'lucide-react'
import { api, ApiError } from '../api'
import type { BranchWithConnector, BusinessMembership } from '../types'

type BusinessesScreenProps = {
  onUnauthorized: () => void
}

export function BusinessesScreen({ onUnauthorized }: BusinessesScreenProps) {
  const [businesses, setBusinesses] = useState<BusinessMembership[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [newBusinessName, setNewBusinessName] = useState('')
  const [creatingBusiness, setCreatingBusiness] = useState(false)

  useEffect(() => {
    let active = true
    setLoading(true)
    api.businesses()
      .then((list) => {
        if (!active) return
        setBusinesses(list)
        setSelectedId((current) => current ?? list[0]?.id ?? null)
      })
      .catch((reason: unknown) => {
        if (!active) return
        if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
        setError(reason instanceof Error ? reason.message : 'No fue posible cargar tus negocios.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [onUnauthorized])

  async function handleCreateBusiness(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!newBusinessName.trim()) return
    setCreatingBusiness(true)
    setError(null)
    try {
      const created = await api.createBusiness(newBusinessName.trim())
      setBusinesses((current) => [...current, created])
      setSelectedId(created.id)
      setNewBusinessName('')
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible crear el negocio.')
    } finally {
      setCreatingBusiness(false)
    }
  }

  const selected = businesses.find((business) => business.id === selectedId) ?? null

  return (
    <div className="screen-stack">
      <section className="screen-title">
        <p className="utility-label">Cuentas y sucursales</p>
        <h1>Mis negocios</h1>
        <p>Crea negocios, da de alta sus sucursales e instala el conector en cada equipo.</p>
      </section>

      <section className="content-card">
        <div className="section-heading horizontal">
          <div>
            <p className="utility-label">Negocios</p>
            <h2>Selecciona uno para administrarlo</h2>
          </div>
        </div>

        {loading ? <p className="panel-hint">Cargando…</p> : null}
        {!loading && businesses.length === 0 ? (
          <p className="panel-hint">Todavía no tienes ningún negocio. Crea el primero abajo.</p>
        ) : null}

        <div className="detail-list">
          {businesses.map((business) => (
            <button
              key={business.id}
              type="button"
              className={business.id === selectedId ? 'secondary-button' : 'secondary-button'}
              style={{ justifyContent: 'flex-start', marginBottom: 8 }}
              onClick={() => setSelectedId(business.id)}
            >
              <Building2 size={16} aria-hidden="true" />
              <span>{business.name} — {roleLabel(business.role)}</span>
            </button>
          ))}
        </div>

        <form onSubmit={(event) => void handleCreateBusiness(event)}>
          <label className="field-label" htmlFor="business-name">Crear nuevo negocio</label>
          <input
            id="business-name"
            value={newBusinessName}
            onChange={(event) => setNewBusinessName(event.target.value)}
            placeholder="Nombre del negocio"
          />
          <button className="primary-button" type="submit" disabled={creatingBusiness || !newBusinessName.trim()}>
            <Plus size={16} aria-hidden="true" />
            <span>{creatingBusiness ? 'Creando…' : 'Crear negocio'}</span>
          </button>
        </form>
        {error ? <p className="form-error" role="alert">{error}</p> : null}
      </section>

      {selected ? (
        <BusinessBranches key={selected.id} business={selected} onUnauthorized={onUnauthorized} />
      ) : null}
    </div>
  )
}

function BusinessBranches({ business, onUnauthorized }: { business: BusinessMembership; onUnauthorized: () => void }) {
  const [branches, setBranches] = useState<BranchWithConnector[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [newCode, setNewCode] = useState('')
  const [newName, setNewName] = useState('')
  const [creatingBranch, setCreatingBranch] = useState(false)
  const [installer, setInstaller] = useState<{ version: string | null; downloadUrl: string | null } | null>(null)
  const canManage = business.role === 'OWNER' || business.role === 'MANAGER'

  useEffect(() => {
    let active = true
    setLoading(true)
    Promise.all([api.businessBranches(business.id), api.agentLatest()])
      .then(([branchList, latest]) => {
        if (!active) return
        setBranches(branchList)
        setInstaller(latest)
      })
      .catch((reason: unknown) => {
        if (!active) return
        if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
        setError(reason instanceof Error ? reason.message : 'No fue posible cargar las sucursales.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [business.id, onUnauthorized])

  async function handleCreateBranch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!newCode.trim() || !newName.trim()) return
    setCreatingBranch(true)
    setError(null)
    try {
      await api.createBranch(business.id, newCode.trim(), newName.trim())
      const refreshed = await api.businessBranches(business.id)
      setBranches(refreshed)
      setNewCode('')
      setNewName('')
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible crear la sucursal.')
    } finally {
      setCreatingBranch(false)
    }
  }

  return (
    <section className="content-card">
      <div className="section-heading horizontal">
        <div>
          <p className="utility-label">{business.name}</p>
          <h2>Sucursales</h2>
        </div>
      </div>

      {loading ? <p className="panel-hint">Cargando…</p> : null}
      {!loading && branches.length === 0 ? <p className="panel-hint">Este negocio todavía no tiene sucursales.</p> : null}

      {branches.map(({ branch, connector }) => (
        <div className="detail-list" key={branch.id} style={{ marginBottom: 16 }}>
          <div><dt><Server size={16} /> {branch.name} ({branch.code})</dt></div>
          <div><dt>Conector</dt><dd>{connector ? `Vinculado a ${connector.machineName}` : 'Sin instalar'}</dd></div>
          {connector ? (
            <>
              <div><dt>Último latido</dt><dd>{connector.lastHeartbeatAt ? new Date(connector.lastHeartbeatAt).toLocaleString('es-MX') : 'nunca'}</dd></div>
              <div><dt>Último error</dt><dd>{connector.lastError ?? 'Ninguno'}</dd></div>
            </>
          ) : null}
        </div>
      ))}

      {installer?.downloadUrl ? (
        <a className="secondary-button" href={installer.downloadUrl}>
          <Download size={16} aria-hidden="true" />
          <span>Instalar conector{installer.version ? ` (v${installer.version})` : ''}</span>
        </a>
      ) : (
        <p className="panel-hint">El instalador todavía no está disponible para descarga.</p>
      )}
      <p className="panel-hint">
        Descarga e instala el conector en la computadora de la sucursal, abre el panel del agente,
        inicia sesión con esta misma cuenta y selecciona la sucursal para vincular el equipo.
      </p>

      {canManage ? (
        <form onSubmit={(event) => void handleCreateBranch(event)}>
          <label className="field-label" htmlFor="branch-code">Crear nueva sucursal</label>
          <input id="branch-code" value={newCode} onChange={(event) => setNewCode(event.target.value)} placeholder="código (ej. sucursal-centro)" />
          <input value={newName} onChange={(event) => setNewName(event.target.value)} placeholder="Nombre visible" />
          <button className="primary-button" type="submit" disabled={creatingBranch || !newCode.trim() || !newName.trim()}>
            <Plus size={16} aria-hidden="true" />
            <span>{creatingBranch ? 'Creando…' : 'Crear sucursal'}</span>
          </button>
        </form>
      ) : null}
      {error ? <p className="form-error" role="alert">{error}</p> : null}
    </section>
  )
}

function roleLabel(role: BusinessMembership['role']) {
  if (role === 'OWNER') return 'Propietario'
  if (role === 'MANAGER') return 'Gerente'
  return 'Consulta'
}
