type BrandProps = {
  compact?: boolean
  className?: string
  showName?: boolean
}

export function Brand({ compact = false, className = '', showName = true }: BrandProps) {
  return (
    <span className={`product-brand${compact ? ' product-brand--compact' : ''}${className ? ` ${className}` : ''}`}>
      <img className="product-brand__mark" src="/brand-mark.png" alt="" aria-hidden="true" />
      {showName ? (
        <span className="product-brand__name">
          <strong>RestaurantAgent</strong>
          <small>by Fatboy</small>
        </span>
      ) : null}
    </span>
  )
}
