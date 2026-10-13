import { formatAmount, formatInteger, formatQuantity } from '../format'
import type { TopProductItem } from '../types'

export function ProductTotals({ items, scope }: { items: TopProductItem[]; scope: string }) {
  const units = items.reduce((total, item) => total + item.quantity, 0)
  return (
    <section className="content-card product-totals-card" aria-label={`Todos los productos vendidos ${scope}`}>
      <div className="section-heading horizontal">
        <div><p className="utility-label">Detalle completo de tickets</p><h2>Todos los productos vendidos</h2></div>
        <span className="product-total-count">{formatQuantity(units)} unidades · {formatInteger(items.length)} productos</span>
      </div>
      {items.length ? (
        <div className="product-totals-scroll">
          <table className="product-totals-table">
            <thead><tr><th>Producto</th><th>Grupo</th><th>Unidades</th><th>Venta de tickets</th></tr></thead>
            <tbody>{items.map((item) => (
              <tr key={`${item.productId}:${item.groupName ?? ''}`}>
                <td>{item.productName}</td><td>{item.groupName || 'Sin grupo'}</td>
                <td>{formatQuantity(item.quantity)}</td><td>{formatAmount(item.sales)}</td>
              </tr>
            ))}</tbody>
            <tfoot><tr><th colSpan={2}>Total</th><th>{formatQuantity(units)}</th><th>{formatAmount(items.reduce((total, item) => total + item.sales, 0))}</th></tr></tfoot>
          </table>
        </div>
      ) : <p className="quiet-empty">No hay productos vendidos en los tickets cobrados y no cancelados de este periodo.</p>}
    </section>
  )
}
