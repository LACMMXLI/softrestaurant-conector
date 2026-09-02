import { ArrowDownToLine, Ban, Banknote, CreditCard, Receipt, Store, Utensils, WalletCards } from 'lucide-react'
import { formatAmount, formatInteger } from '../format'
import type { BusinessDashboard } from '../types'
import { EmptyState } from '../components/EmptyState'

export function BusinessDashboardScreen({ data, loading, error, onRetry, onOpenBranch }: {
  data: BusinessDashboard | null; loading: boolean; error: string | null; onRetry: () => void; onOpenBranch: (code: string) => void
}) {
  if (loading && !data) return <div className="skeleton business-skeleton" aria-busy="true" />
  if (error && !data) return <EmptyState title="No se pudo consultar el resumen" message={error} />
  if (!data) return null
  const { summary } = data
  return <div className="screen-stack business-summary">
    {error ? <button className="inline-alert" onClick={onRetry}>{error} · Toca para reintentar</button> : null}
    <section className="business-hero">
      <div><p>Resumen general</p><h1>{data.businessName}</h1><span>{data.includedBranches} de {data.totalBranches} sucursales con periodo conciliado</span></div>
      <strong>{formatAmount(summary.sales)}</strong><small>Ventas consolidadas</small>
    </section>
    {data.coverage !== 'complete' ? <div className="coverage-note">El resumen incluye solo sucursales con sincronización conciliada para esta fecha. No se interpretan sucursales sin cobertura como venta cero.</div> : null}
    <section className="metric-strip"><Metric icon={<Receipt />} label="Tickets" value={formatInteger(summary.tickets)} /><Metric icon={<WalletCards />} label="Ticket promedio" value={formatAmount(summary.averageTicket)} /><Metric icon={<Banknote />} label="Propinas" value={formatAmount(summary.tips)} /></section>
    <section className="content-card"><div className="section-heading"><div><p className="utility-label">Participación</p><h2>Venta por sucursal</h2></div></div><div className="branch-comparison">{data.branches.map(branch => <button key={branch.code} className="branch-share" onClick={() => onOpenBranch(branch.code)}><span><Store size={17} /><b>{branch.name}</b><small>{formatInteger(branch.tickets)} tickets · {formatAmount(branch.averageTicket)} promedio</small></span><strong>{formatAmount(branch.sales)}<em>{branch.participationPercent}% del total</em></strong><i><i style={{ width: `${branch.participationPercent}%` }} /></i></button>)}</div></section>
    <section className="content-card"><div className="section-heading"><div><p className="utility-label">Formas de pago y caja</p><h2>Ingresos y salidas</h2></div></div><div className="payment-breakdown"><div><Banknote size={17}/><span>Efectivo</span><strong>{formatAmount(summary.cashSales)}</strong></div><div><CreditCard size={17}/><span>Tarjeta</span><strong>{formatAmount(summary.cardSales)}</strong></div><div><WalletCards size={17}/><span>Otros</span><strong>{formatAmount(summary.otherSales)}</strong></div></div><div className="business-operations"><span><ArrowDownToLine size={18}/> Entradas <b>{formatAmount(summary.cashIn)}</b></span><span><Banknote size={18}/> Salidas <b>{formatAmount(summary.cashOut)}</b></span><span><Ban size={18}/> Cancelaciones <b>{formatInteger(summary.cancelledTickets)}</b><small>{formatInteger(summary.cancelledLines)} líneas</small></span></div></section>
    <section className="content-card top-products-card"><div className="section-heading"><div><p className="utility-label">Productos vendidos</p><h2>Los más vendidos del negocio</h2></div></div><div className="top-products-grid"><ProductList title="Alimentos" items={data.topProducts.foods} /><ProductList title="Bebidas" items={data.topProducts.beverages} /></div></section>
  </div>
}
function Metric({ icon, label, value }: {icon: React.ReactNode; label:string; value:string}) { return <article className="metric-item"><span className="metric-icon">{icon}</span><span>{label}</span><strong>{value}</strong></article> }
function ProductList({title,items}:{title:string;items:BusinessDashboard['topProducts']['foods']}) { return <div className="top-product-column"><div className="top-product-heading"><Utensils size={18}/><h3>{title}</h3></div>{items.length ? <ol className="top-product-list">{items.map(x=><li key={x.productId}><span className="top-product-rank">{x.rank}</span><span className="top-product-name"><strong>{x.productName}</strong><small>{formatInteger(x.quantity)} unidades</small></span><span className="top-product-values"><strong>{formatAmount(x.sales)}</strong></span></li>)}</ol> : <p className="quiet-empty">Sin productos clasificados.</p>}</div> }
