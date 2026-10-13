import { formatAmount, formatQuantity } from '../format'
import type { TopProductItem } from '../types'

export function ProductTotals({ items, scope, loading = false, error = null }: { items: TopProductItem[]; scope: string; loading?: boolean; error?: string | null }) {
  const units = items.reduce((total, item) => total + item.quantity, 0)
  const sales = items.reduce((total, item) => total + item.sales, 0)
  return (
    <section className="content-card product-totals-card" aria-label={`Todos los productos vendidos ${scope}`}>
      <div className="section-heading horizontal">
        <div><p className="utility-label">Detalle de ventas · {scope}</p><h2>Productos vendidos</h2></div>
      </div>
      {error && !loading ? <p className="quiet-empty" role="alert">{error}</p> : loading ? <p className="quiet-empty" role="status">Cargando productos vendidos…</p> : items.length ? <>
        <div className="product-total-list" role="list">
          {items.map((item) => (
            <article className="product-total-row" role="listitem" key={`${item.productId}:${item.groupName ?? ''}`}>
              <strong className="product-total-name">{item.productName}</strong>
              <span className="product-total-quantity"><strong>{formatQuantity(item.quantity)}</strong><small>vendidos</small></span>
              <span className="product-total-sales"><strong>{formatAmount(item.sales)}</strong><small>venta</small></span>
            </article>
          ))}
        </div>
        <footer className="product-totals-summary">
          <span>Total de productos vendidos</span>
          <strong>{formatQuantity(units)} unidades</strong>
          <b>{formatAmount(sales)}</b>
        </footer>
      </> : <p className="quiet-empty">No hay productos vendidos en los tickets cobrados y no cancelados de este periodo.</p>}
    </section>
  )
}
