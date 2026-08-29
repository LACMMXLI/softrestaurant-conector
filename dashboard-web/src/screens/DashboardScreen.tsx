import { ArrowDownRight, ArrowUpRight, Ban, Coins, Receipt, WalletCards } from 'lucide-react'
import { EmptyState } from '../components/EmptyState'
import { SalesChart } from '../components/SalesChart'
import { StatusPill } from '../components/StatusPill'
import { TicketRow } from '../components/TicketRow'
import { formatAmount, formatDateLabel, formatInteger, timeAgo } from '../format'
import type { DashboardHome } from '../types'

type DashboardScreenProps = {
  data: DashboardHome | null
  loading: boolean
  error: string | null
  onRetry: () => void
  onOpenTicket: (folio: number) => void
  onOpenSales: () => void
}

export function DashboardScreen({
  data,
  loading,
  error,
  onRetry,
  onOpenTicket,
  onOpenSales,
}: DashboardScreenProps) {
  if (loading && !data) return <DashboardSkeleton />
  if (error && !data) {
    return (
      <EmptyState
        title="No se pudo consultar el dashboard"
        message={`${error} Revisa la conexión e inténtalo nuevamente.`}
      />
    )
  }
  if (!data) return null

  const { meta, summary } = data
  const comparison = summary.salesChangePercent

  return (
    <div className="screen-stack">
      {error ? (
        <button className="inline-alert" type="button" onClick={onRetry}>
          {error} · Toca para volver a intentar
        </button>
      ) : null}

      <section className="pulse-ticket" aria-labelledby="pulse-title">
        <div className="pulse-topline">
          <div>
            <p className="utility-label">Corte en movimiento</p>
            <h2 id="pulse-title">{formatDateLabel(meta.date)}</h2>
          </div>
          <StatusPill freshness={meta.freshness} coverage={meta.coverage} />
        </div>

        <div className="pulse-amount">
          <span>Venta registrada</span>
          <strong>{formatAmount(summary.sales)}</strong>
          <small>Importe de cabeceras válidas · moneda de la sucursal</small>
        </div>

        <div className="pulse-divider" aria-hidden="true" />

        <div className="pulse-foot">
          <span>{timeAgo(meta.lastSyncAt)}</span>
          {comparison !== null ? (
            <span className={comparison >= 0 ? 'comparison up' : 'comparison down'}>
              {comparison >= 0 ? <ArrowUpRight size={16} /> : <ArrowDownRight size={16} />}
              {Math.abs(comparison).toFixed(1)}% vs. día anterior
            </span>
          ) : (
            <span>Comparación no disponible</span>
          )}
        </div>
      </section>

      {meta.canShowData && meta.coverage === 'partial' ? (
        <div className="coverage-note" role="status">
          Este periodo solo está cubierto parcialmente. Los importes corresponden únicamente al rango conciliado recibido.
        </div>
      ) : null}

      {!meta.canShowData ? (
        <EmptyState
          title={meta.coverage === 'invalid' ? 'Lote sin conciliación válida' : 'Periodo sin cobertura'}
          message="No se convierten datos ausentes en ceros. Espera una sincronización conciliada para este periodo."
        />
      ) : (
        <>
          <section className="metric-strip" aria-label="Indicadores principales">
            <Metric icon={<Receipt size={19} />} label="Tickets" value={formatInteger(summary.tickets)} />
            <Metric icon={<WalletCards size={19} />} label="Promedio" value={formatAmount(summary.averageTicket)} />
            <Metric icon={<Coins size={19} />} label="Propinas" value={formatAmount(summary.tips)} />
          </section>

          <section className="content-card chart-card">
            <SalesChart points={data.hourlySales} />
          </section>

          <section className="content-card operational-card">
            <div className="section-heading">
              <div>
                <p className="utility-label">Atención operativa</p>
                <h2>Lo que salió del ritmo normal</h2>
              </div>
            </div>
            <div className="operation-grid">
              <div className="operation-stat danger-soft">
                <Ban size={19} />
                <span>Tickets cancelados</span>
                <strong>{formatInteger(summary.cancelledTickets)}</strong>
                <small>{formatInteger(summary.cancelledLines)} líneas canceladas</small>
              </div>
              <div className="operation-stat gold-soft">
                <ArrowUpRight size={19} />
                <span>Salidas de caja</span>
                <strong>{formatAmount(summary.cashOut)}</strong>
                <small>Entradas: {formatAmount(summary.cashIn)}</small>
              </div>
            </div>
          </section>

          <section className="content-card tickets-card">
            <div className="section-heading horizontal">
              <div>
                <p className="utility-label">Rastro verificable</p>
                <h2>Tickets recientes</h2>
              </div>
              <button className="text-button" type="button" onClick={onOpenSales}>Ver todos</button>
            </div>
            {data.recentTickets.length > 0 ? (
              <div className="ticket-list">
                {data.recentTickets.map((ticket) => (
                  <TicketRow key={ticket.folio} ticket={ticket} onOpen={onOpenTicket} />
                ))}
              </div>
            ) : (
              <p className="quiet-empty">No hay tickets registrados para esta fecha.</p>
            )}
          </section>
        </>
      )}
    </div>
  )
}

function Metric({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <article className="metric-item">
      <span className="metric-icon" aria-hidden="true">{icon}</span>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  )
}

function DashboardSkeleton() {
  return (
    <div className="screen-stack" aria-label="Cargando dashboard" aria-busy="true">
      <div className="skeleton pulse-skeleton" />
      <div className="metric-strip">
        <div className="skeleton metric-skeleton" />
        <div className="skeleton metric-skeleton" />
        <div className="skeleton metric-skeleton" />
      </div>
      <div className="skeleton chart-skeleton" />
    </div>
  )
}
