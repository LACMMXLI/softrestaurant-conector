import { ArrowDownRight, ArrowUpRight, Ban, Banknote, Coins, CreditCard, Equal, GlassWater, Hourglass, Landmark, Minus, Plus, Receipt, Utensils, WalletCards } from 'lucide-react'
import { EmptyState } from '../components/EmptyState'
import { SalesChart } from '../components/SalesChart'
import { StatusPill } from '../components/StatusPill'
import { TicketRow } from '../components/TicketRow'
import { formatAmount, formatInteger, formatTime, timeAgo } from '../format'
import type { DashboardHome } from '../types'

type DashboardScreenProps = {
  data: DashboardHome | null
  loading: boolean
  error: string | null
  onRetry: () => void
  onOpenTicket: (folio: number) => void
  onOpenAccount: (tempFolio: number) => void
  onOpenSales: () => void
}

export function DashboardScreen({
  data,
  loading,
  error,
  onRetry,
  onOpenTicket,
  onOpenAccount,
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
  const comparison = meta.shiftIsOpen ? null : summary.salesChangePercent

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
            <h2 id="pulse-title">Turno {meta.shiftNumber ?? 'seleccionado'}</h2>
          </div>
          <StatusPill freshness={meta.freshness} coverage={meta.coverage} />
        </div>

        <div className="pulse-amount">
          <span>{meta.shiftIsOpen ? 'Venta cobrada acumulada' : 'Venta cerrada'}</span>
          <strong>{formatAmount(summary.sales)}</strong>
          <small>
            {meta.shiftIsOpen
              ? `${formatInteger(summary.tickets)} tickets cobrados · ${formatAmount(summary.openAccountsTotal)} pendiente en cuentas abiertas`
              : 'Importe de tickets pagados, no cancelados y cerrados'}
          </small>
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
            <span>{meta.shiftIsOpen ? 'Comparación disponible al cerrar el turno' : 'Comparación no disponible'}</span>
          )}
        </div>
      </section>

      {meta.canShowData && meta.coverage === 'partial' ? (
        <div className="coverage-note" role="status">
          Este turno solo está cubierto parcialmente. Los importes corresponden únicamente a la parte conciliada recibida.
        </div>
      ) : null}

      {!meta.canShowData ? (
        <EmptyState
          title={meta.coverage === 'invalid' ? 'Lote sin conciliación válida' : 'Periodo sin cobertura'}
          message="No se convierten datos ausentes en ceros. Espera una sincronización conciliada para este turno."
        />
      ) : (
        <>
          <section className="metric-strip" aria-label="Indicadores principales">
            <Metric icon={<Receipt size={19} />} label={meta.shiftIsOpen ? 'Tickets cobrados' : 'Tickets cerrados'} value={formatInteger(summary.tickets)} />
            {meta.shiftIsOpen ? <Metric icon={<Hourglass size={19} />} label="Cuentas abiertas" value={formatInteger(summary.openAccounts)} /> : null}
            <Metric icon={<WalletCards size={19} />} label="Promedio" value={formatAmount(summary.averageTicket)} />
            <Metric icon={<Coins size={19} />} label="Propinas" value={formatAmount(summary.tips)} />
          </section>

          {meta.shiftIsOpen ? (
            <section className="content-card tickets-card" aria-labelledby="open-accounts-title">
              <div className="section-heading horizontal">
                <div>
                  <p className="utility-label">Pendiente por cobrar</p>
                  <h2 id="open-accounts-title">Cuentas abiertas</h2>
                </div>
                <strong>{formatAmount(summary.openAccountsTotal)}</strong>
              </div>
              {data.openAccounts.length > 0 ? (
                <div className="ticket-list">
                  {data.openAccounts.map((account) => (
                    <button className="ticket-row transient-row" type="button" key={`${account.tempFolio}:${account.checkNumber ?? ''}`} onClick={() => onOpenAccount(account.tempFolio)}>
                      <span className="ticket-status-rail" data-state="open" />
                      <span className="ticket-main">
                        <span className="ticket-title">Cuenta {account.checkNumber || `temporal ${account.tempFolio}`}</span>
                        <span className="ticket-meta">
                          {formatTime(account.openedAt)}
                          {account.table ? ` · Mesa ${account.table}` : ''}
                          {account.waiter ? ` · Mesero ${account.waiter}` : ''}
                        </span>
                      </span>
                      <strong>{formatAmount(account.total)}</strong>
                      <span className="transient-badge">Abierta</span>
                    </button>
                  ))}
                </div>
              ) : (
                <p className="quiet-empty">No hay cuentas abiertas para este turno.</p>
              )}
              <p className="data-note">Estas cuentas no se suman a la venta cobrada. Permanecen separadas hasta que SoftRestaurant registre su pago.</p>
            </section>
          ) : null}

          <section className="content-card cash-cut-card" aria-labelledby="cash-cut-title">
            <div className="section-heading horizontal">
              <div>
                <p className="utility-label">Corte estimado</p>
                <h2 id="cash-cut-title">¿Cuánto efectivo debe haber?</h2>
              </div>
              <Banknote size={22} aria-hidden="true" />
            </div>

            {summary.paymentBreakdownComplete && summary.expectedCash !== null ? (
              <>
                <div className="cash-equation" aria-label="Fórmula del efectivo esperado">
                  <EquationPart icon={<Landmark size={17} />} label="Fondo" value={summary.openingFund} />
                  <Plus size={17} aria-label="más" />
                  <EquationPart icon={<Banknote size={17} />} label="Efectivo vendido" value={summary.cashSales} />
                  <Plus size={17} aria-label="más" />
                  <EquationPart label="Entradas" value={summary.cashIn} />
                  <Minus size={17} aria-label="menos" />
                  <EquationPart label="Salidas" value={summary.cashOut} />
                  <Equal size={17} aria-label="igual" />
                  <div className="equation-result"><span>Efectivo esperado</span><strong>{formatAmount(summary.expectedCash)}</strong></div>
                </div>

                <div className="payment-breakdown" aria-label="Venta por tipo de pago">
                  <div><Banknote size={17} /><span>Efectivo</span><strong>{formatAmount(summary.cashSales)}</strong></div>
                  <div><CreditCard size={17} /><span>Tarjeta</span><strong>{formatAmount(summary.cardSales)}</strong></div>
                  <div><WalletCards size={17} /><span>Otros</span><strong>{formatAmount(summary.otherSales)}</strong></div>
                </div>

                {meta.shiftIsOpen ? (
                  <p className="data-note">Cálculo operativo: fondo + cobros en efectivo + entradas − salidas. La declaración y diferencia de caja estarán disponibles al realizar el corte.</p>
                ) : (
                  <>
                    <div className="cash-reconciliation">
                      <span>Declarado por cajeros: <strong>{formatAmount(summary.declaredCash)}</strong></span>
                      <span className={summary.cashDifference === 0 ? 'balanced' : 'attention'}>
                        Diferencia candidata: <strong>{formatAmount(summary.cashDifference)}</strong>
                      </span>
                    </div>
                    <p className="data-note">Cálculo: fondo + cobros en efectivo + entradas − salidas. La diferencia es candidata hasta contrastarla con el corte impreso de RestaurantAgent.</p>
                  </>
                )}
              </>
            ) : (
              <div className="coverage-note" role="status">
                El total de venta es válido, pero el catálogo de formas de pago aún no llegó para este turno. No se estima la caja con información incompleta.
              </div>
            )}
          </section>

          <section className="content-card chart-card">
            <SalesChart points={data.hourlySales} />
          </section>

          <section className="content-card top-products-card" aria-labelledby="top-products-title">
            <div className="section-heading horizontal">
              <div><p className="utility-label">Productos vendidos</p><h2 id="top-products-title">Top 20 del corte</h2></div>
              <span className="top-products-total">Ordenado por unidades reales</span>
            </div>
            <div className="top-products-grid">
              <TopProductList title="Alimentos" icon={<Utensils size={19} />} items={data.topProducts.foods} />
              <TopProductList title="Bebidas" icon={<GlassWater size={19} />} items={data.topProducts.beverages} />
            </div>
            <p className="data-note">Incluye productos con importe de tickets cobrados y no cancelados del turno, aunque el corte siga abierto. La categoría proviene del grupo configurado en SoftRestaurant.</p>
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
                  <TicketRow key={`${ticket.transient ? 'temp' : 'final'}:${ticket.folio}`} ticket={ticket} onOpen={onOpenTicket} />
                ))}
              </div>
            ) : (
              <p className="quiet-empty">No hay tickets registrados para este turno.</p>
            )}
          </section>
        </>
      )}
    </div>
  )
}

function TopProductList({ title, icon, items }: { title: string; icon: React.ReactNode; items: DashboardHome['topProducts']['foods'] }) {
  return <div className="top-product-column">
    <div className="top-product-heading">{icon}<h3>{title}</h3><span>{items.length}</span></div>
    {items.length > 0 ? <ol className="top-product-list">{items.map((item) => <li key={item.productId}>
      <span className="top-product-rank">{item.rank}</span>
      <span className="top-product-name"><strong>{item.productName}</strong><small>{item.groupName || 'Sin grupo'}</small></span>
      <span className="top-product-values"><strong>{formatInteger(item.quantity)} u.</strong><small>{formatAmount(item.sales)}</small></span>
    </li>)}</ol> : <p className="quiet-empty">No hay productos clasificados en este corte.</p>}
  </div>
}

function EquationPart({ icon, label, value }: { icon?: React.ReactNode; label: string; value: number | null }) {
  return (
    <div className="equation-part">
      <span>{icon}{label}</span>
      <strong>{formatAmount(value)}</strong>
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
