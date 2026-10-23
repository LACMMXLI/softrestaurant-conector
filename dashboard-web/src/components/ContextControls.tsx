import { CalendarDays, RefreshCw, Store } from 'lucide-react'
import type { BusinessMembership, DashboardBranch, DashboardShift, Subscription } from '../types'

type ContextControlsProps = {
  branchCode: string
  branches: DashboardBranch[]
  businesses: BusinessMembership[]
  subscription: Subscription | null
  shifts: DashboardShift[]
  shiftId: number | null
  date: string
  rangeEnd: string
  minimumDate: string | undefined
  consolidated: boolean
  refreshing: boolean
  onBranchChange: (code: string) => void
  onShiftChange: (shift: DashboardShift | null) => void
  onDateChange: (date: string) => void
  onRangeEndChange: (date: string) => void
  onRefresh: () => void
}

export function ContextControls({
  branchCode, branches, businesses, subscription, shifts, shiftId, date, rangeEnd, minimumDate,
  consolidated, refreshing, onBranchChange, onShiftChange, onDateChange, onRangeEndChange, onRefresh,
}: ContextControlsProps) {
  const canUseConsolidated = subscription?.plan === 'PLUS' || subscription?.plan === 'UNLIMITED'
  const consolidatedBusinesses = canUseConsolidated
    ? businesses.filter((business) => branches.some((branch) => branch.businessId === business.id))
    : []

  return (
    <div className="context-controls">
      <label className="context-select">
        <Store size={17} aria-hidden="true" />
        <span className="sr-only">Sucursal</span>
        <select value={branchCode} onChange={(event) => onBranchChange(event.target.value)}>
          {consolidatedBusinesses.map((business) => (
            <option value={`all:${business.id}`} key={`all:${business.id}`}>Resumen general · {business.name}</option>
          ))}
          {branches.map((branch) => <option value={branch.code} key={branch.code}>{branch.name}</option>)}
        </select>
      </label>

      {!consolidated ? (
        <label className="context-select">
          <span className="sr-only">Turno</span>
          <select value={shiftId ?? ''} onChange={(event) => onShiftChange(shifts.find((shift) => shift.id === Number(event.target.value)) ?? null)}>
            {shifts.length === 0 ? <option value="">Sin turnos sincronizados</option> : null}
            {shifts.map((shift) => (
              <option value={shift.id} key={shift.id}>{shift.isOpen ? 'Abierto' : 'Cerrado'} · Turno {shift.number} · {shift.cashier || 'Sin cajero'}</option>
            ))}
          </select>
        </label>
      ) : null}

      <label className="context-select context-date">
        <CalendarDays size={16} aria-hidden="true" />
        <span className="sr-only">{consolidated ? 'Desde' : 'Fecha'}</span>
        <input type="date" value={date} min={minimumDate} onChange={(event) => onDateChange(event.target.value)} />
      </label>

      {consolidated ? (
        <label className="context-select context-date">
          <CalendarDays size={16} aria-hidden="true" />
          <span className="sr-only">Hasta</span>
          <input type="date" value={rangeEnd || date} min={date || minimumDate} onChange={(event) => onRangeEndChange(event.target.value)} />
        </label>
      ) : null}

      <button className="icon-button refresh-button" type="button" onClick={onRefresh} aria-label="Actualizar datos">
        <RefreshCw size={18} className={refreshing ? 'spinning' : ''} />
      </button>
    </div>
  )
}
