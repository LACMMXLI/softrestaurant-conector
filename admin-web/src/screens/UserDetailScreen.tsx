import { useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowLeft, Ban, CheckCircle2, KeyRound, Plus, Save, Trash2 } from 'lucide-react'
import { api, ApiError } from '../api'
import { BUSINESS_ROLE_LABELS, BUSINESS_ROLES, ROLE_LABELS, ROLES } from '../types'
import type { Business, BusinessRole, Role, UserDetail } from '../types'

type UserDetailScreenProps = {
  user: UserDetail
  allBusinesses: Business[]
  onBack: () => void
  onUserUpdated: (user: UserDetail) => void
  onSelfSessionInvalidated: (message: string) => void
  onUnauthorized: () => void
}

export function UserDetailScreen({
  user,
  allBusinesses,
  onBack,
  onUserUpdated,
  onSelfSessionInvalidated,
  onUnauthorized,
}: UserDetailScreenProps) {
  const [displayName, setDisplayName] = useState(user.displayName)
  const [role, setRole] = useState<Role>(user.role)
  const [saving, setSaving] = useState(false)
  const [togglingStatus, setTogglingStatus] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [newPassword, setNewPassword] = useState('')
  const [resettingPassword, setResettingPassword] = useState(false)
  const [passwordNotice, setPasswordNotice] = useState<string | null>(null)

  const [businessToAdd, setBusinessToAdd] = useState('')
  const [businessRoleToAdd, setBusinessRoleToAdd] = useState<BusinessRole>('VIEWER')
  const [businessBusy, setBusinessBusy] = useState<string | null>(null)

  const unassignedBusinesses = allBusinesses.filter(
    (business) => !user.businesses.some((assigned) => assigned.businessId === business.id),
  )

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const result = await api.updateUser(user.id, displayName.trim(), role)
      onUserUpdated(result.user)
      if (result.selfAffected && result.user.role !== 'SUPERADMIN') {
        onSelfSessionInvalidated('Ya no tienes rol de administrador: se cerró tu sesión.')
      }
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      if (reason instanceof ApiError && reason.status === 409) {
        setError(reason.message)
        return
      }
      setError(reason instanceof Error ? reason.message : 'No fue posible guardar los cambios.')
    } finally {
      setSaving(false)
    }
  }

  async function handleToggleActive() {
    setTogglingStatus(true)
    setError(null)
    try {
      const result = await api.setUserActive(user.id, !user.active)
      onUserUpdated(result.user)
      if (result.selfAffected && !result.user.active) {
        onSelfSessionInvalidated('Desactivaste tu propia cuenta: se cerró tu sesión.')
      }
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      if (reason instanceof ApiError && reason.status === 409) {
        setError(reason.message)
        return
      }
      setError(reason instanceof Error ? reason.message : 'No fue posible cambiar el estado.')
    } finally {
      setTogglingStatus(false)
    }
  }

  async function handleResetPassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setResettingPassword(true)
    setError(null)
    setPasswordNotice(null)
    try {
      const result = await api.resetUserPassword(user.id, newPassword)
      onUserUpdated(result.user)
      setNewPassword('')
      if (result.selfAffected) {
        onSelfSessionInvalidated('Restableciste tu propia contraseña: se cerró tu sesión, inicia sesión de nuevo.')
        return
      }
      setPasswordNotice('Contraseña actualizada. Todas las sesiones anteriores de esta cuenta quedaron cerradas.')
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible restablecer la contraseña.')
    } finally {
      setResettingPassword(false)
    }
  }

  async function handleAddBusiness(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!businessToAdd) return
    setBusinessBusy(businessToAdd)
    setError(null)
    try {
      const updated = await api.assignUserBusinesses(user.id, [businessToAdd], businessRoleToAdd)
      onUserUpdated(updated)
      setBusinessToAdd('')
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible asignar el negocio.')
    } finally {
      setBusinessBusy(null)
    }
  }

  async function handleRemoveBusiness(businessId: string) {
    setBusinessBusy(businessId)
    setError(null)
    try {
      await api.removeUserBusiness(user.id, businessId)
      onUserUpdated({ ...user, businesses: user.businesses.filter((b) => b.businessId !== businessId) })
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible quitar el negocio.')
    } finally {
      setBusinessBusy(null)
    }
  }

  return (
    <div className="panel-stack">
      <button className="icon-button back-button" type="button" onClick={onBack}>
        <ArrowLeft size={17} aria-hidden="true" />
        <span>Volver a usuarios</span>
      </button>

      <section className="panel-card" aria-labelledby="user-detail-title">
        <div className="panel-card-header">
          <h2 id="user-detail-title">
            {user.displayName} <code className="branch-code-badge">{user.email}</code>
          </h2>
          <span className={user.active ? 'status-pill status-ok' : 'status-pill status-off'}>
            {user.active ? 'Activo' : 'Inactivo'}
          </span>
        </div>

        <form className="inline-form" onSubmit={handleSave}>
          <label>
            Nombre
            <input
              type="text"
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
              maxLength={200}
              required
            />
          </label>
          <label>
            Rol de plataforma
            <select value={role} onChange={(event) => setRole(event.target.value as Role)}>
              {ROLES.map((r) => (
                <option key={r} value={r}>{ROLE_LABELS[r]}</option>
              ))}
            </select>
          </label>
          <button className="primary-button" type="submit" disabled={saving}>
            <Save size={16} aria-hidden="true" />
            <span>{saving ? 'Guardando…' : 'Guardar cambios'}</span>
          </button>
        </form>

        {error ? <p className="form-error" role="alert">{error}</p> : null}

        <div className="panel-card-footer">
          <button
            className={user.active ? 'secondary-button danger-outline' : 'secondary-button'}
            type="button"
            onClick={() => void handleToggleActive()}
            disabled={togglingStatus}
          >
            {user.active ? <Ban size={16} aria-hidden="true" /> : <CheckCircle2 size={16} aria-hidden="true" />}
            <span>{togglingStatus ? 'Actualizando…' : user.active ? 'Desactivar usuario' : 'Activar usuario'}</span>
          </button>
          <p className="panel-hint">
            Desactivar no borra la cuenta ni su historial: solo le impide iniciar sesión y cierra
            cualquier sesión abierta. Puede reactivarse en cualquier momento.
          </p>
        </div>
      </section>

      <section className="panel-card" aria-labelledby="user-password-title">
        <h2 id="user-password-title">Restablecer contraseña</h2>
        <p className="panel-hint">Cierra de inmediato todas las sesiones activas de esta cuenta.</p>
        <form className="inline-form" onSubmit={handleResetPassword}>
          <label>
            Nueva contraseña
            <input
              type="password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              minLength={12}
              autoComplete="new-password"
              required
            />
          </label>
          <button className="primary-button" type="submit" disabled={resettingPassword}>
            <KeyRound size={16} aria-hidden="true" />
            <span>{resettingPassword ? 'Guardando…' : 'Restablecer'}</span>
          </button>
        </form>
        {passwordNotice ? <p className="panel-hint form-notice-ok">{passwordNotice}</p> : null}
      </section>

      <section className="panel-card" aria-labelledby="user-businesses-title">
        <h2 id="user-businesses-title">Negocios asignados</h2>
        <p className="panel-hint">
          {user.role === 'SUPERADMIN'
            ? 'Un SUPERADMIN también administra el panel de operador (X-Admin-Key/sesión), pero para ver el dashboard de un negocio necesita, igual que cualquier cuenta, ser miembro explícito aquí.'
            : 'El acceso de esta cuenta a cada negocio (y por tanto a sus sucursales) se controla aquí.'}
        </p>

        {user.businesses.length === 0 ? (
          <p className="panel-hint">Esta cuenta no tiene negocios asignados todavía: no verá ninguno en el dashboard.</p>
        ) : (
          <ul className="assigned-branch-list">
            {user.businesses.map((business) => (
              <li key={business.businessId}>
                <span>
                  {business.name} — {BUSINESS_ROLE_LABELS[business.role]}
                  {!business.active ? <span className="panel-hint"> (negocio inactivo)</span> : null}
                </span>
                <button
                  type="button"
                  className="icon-button icon-button-danger"
                  title="Quitar negocio"
                  aria-label={`Quitar ${business.name}`}
                  disabled={businessBusy === business.businessId}
                  onClick={() => void handleRemoveBusiness(business.businessId)}
                >
                  <Trash2 size={15} />
                </button>
              </li>
            ))}
          </ul>
        )}

        {unassignedBusinesses.length > 0 ? (
          <form className="inline-form" onSubmit={handleAddBusiness}>
            <label>
              Agregar negocio
              <select value={businessToAdd} onChange={(event) => setBusinessToAdd(event.target.value)}>
                <option value="">Selecciona…</option>
                {unassignedBusinesses.map((business) => (
                  <option key={business.id} value={business.id}>{business.name}</option>
                ))}
              </select>
            </label>
            <label>
              Rol
              <select value={businessRoleToAdd} onChange={(event) => setBusinessRoleToAdd(event.target.value as BusinessRole)}>
                {BUSINESS_ROLES.map((r) => (
                  <option key={r} value={r}>{BUSINESS_ROLE_LABELS[r]}</option>
                ))}
              </select>
            </label>
            <button className="secondary-button" type="submit" disabled={!businessToAdd || businessBusy !== null}>
              <Plus size={16} aria-hidden="true" />
              <span>Agregar</span>
            </button>
          </form>
        ) : null}
      </section>
    </div>
  )
}
