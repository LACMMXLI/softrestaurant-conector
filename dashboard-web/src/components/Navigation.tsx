import { BarChart3, Ban, Building2, LayoutDashboard, Menu, ReceiptText, ShoppingBasket } from 'lucide-react'
import { isTabAvailable } from '../lib/navigation'
import type { Tab } from '../lib/navigation'

type NavigationProps = {
  tab: Tab
  consolidated: boolean
  onChange: (tab: Tab) => void
}

const items: Array<{ key: Tab; label: string; icon: React.ReactNode }> = [
  { key: 'home', label: 'Inicio', icon: <LayoutDashboard size={20} /> },
  { key: 'sales', label: 'Ventas', icon: <BarChart3 size={20} /> },
  { key: 'operations', label: 'Operación', icon: <ReceiptText size={20} /> },
  { key: 'cancellations', label: 'Cancelados', icon: <Ban size={20} /> },
  { key: 'products', label: 'Productos', icon: <ShoppingBasket size={20} /> },
  { key: 'businesses', label: 'Negocios', icon: <Building2 size={20} /> },
  { key: 'more', label: 'Más', icon: <Menu size={20} /> },
]

export function Navigation({ tab, consolidated, onChange }: NavigationProps) {
  // "Categorías de gastos" se abre desde Más, así que Más se mantiene activa en esa pantalla.
  const active: Tab = tab === 'expense-categories' ? 'more' : tab
  return (
    <div className="nav-items">
      {items.filter((item) => isTabAvailable(item.key, consolidated)).map((item) => (
        <button
          type="button"
          key={item.key}
          className={active === item.key ? 'nav-item active' : 'nav-item'}
          onClick={() => onChange(item.key)}
          aria-current={active === item.key ? 'page' : undefined}
        >
          {item.icon}<span>{item.label}</span>
        </button>
      ))}
    </div>
  )
}
