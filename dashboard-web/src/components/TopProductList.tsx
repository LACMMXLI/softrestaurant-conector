import { formatAmount, formatInteger } from '../format'
import type { TopProductItem } from '../types'

type TopProductListProps = {
  title: string
  icon: React.ReactNode
  items: TopProductItem[]
  /** `group`: grupo y unidades abreviadas (sucursal). `units`: solo unidades e importe (consolidado). */
  variant?: 'group' | 'units'
  emptyMessage: string
}

export function TopProductList({ title, icon, items, variant = 'group', emptyMessage }: TopProductListProps) {
  return (
    <div className="top-product-column">
      <div className="top-product-heading">{icon}<h3>{title}</h3>{variant === 'group' ? <span>{items.length}</span> : null}</div>
      {items.length > 0 ? (
        <ol className="top-product-list">
          {items.map((item) => (
            <li key={item.productId}>
              <span className="top-product-rank">{item.rank}</span>
              <span className="top-product-name">
                <strong>{item.productName}</strong>
                <small>{variant === 'group' ? item.groupName || 'Sin grupo' : `${formatInteger(item.quantity)} unidades`}</small>
              </span>
              <span className="top-product-values">
                {variant === 'group' ? (
                  <>
                    <strong>{formatInteger(item.quantity)} u.</strong>
                    <small>{formatAmount(item.sales)}</small>
                  </>
                ) : (
                  <strong>{formatAmount(item.sales)}</strong>
                )}
              </span>
            </li>
          ))}
        </ol>
      ) : (
        <p className="quiet-empty">{emptyMessage}</p>
      )}
    </div>
  )
}
