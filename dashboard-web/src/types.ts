export type DashboardUser = {
  id: string
  email: string
  displayName: string
  // Rol de PLATAFORMA (no de negocio): solo distingue operador (SUPERADMIN, panel admin) de
  // cuenta normal. El permiso real por negocio (OWNER/MANAGER/VIEWER) vive en BusinessMembership.
  role: 'SUPERADMIN' | 'USER'
}

export type Subscription = {
  plan: 'BASIC' | 'PLUS'
  status: 'TRIAL' | 'ACTIVE' | 'EXPIRED' | 'SUSPENDED'
  trialEndsAt: string
  paidUntil: string | null
  suspended: boolean
  canAccessContent: boolean
  trialDaysRemaining: number
}

export type DashboardSession = { user: DashboardUser; expiresAt?: string; subscription: Subscription }

export type BusinessMembership = {
  id: string
  name: string
  slug: string
  active: boolean
  createdAt: string
  role: 'OWNER' | 'MANAGER' | 'VIEWER'
}

export type BusinessBranch = {
  id: string
  businessId: string
  code: string
  name: string
  active: boolean
}

export type ConnectorInstallation = {
  id: string
  branchCode: string
  machineName: string
  active: boolean
  agentVersion: string | null
  linkedAt: string | null
  lastHeartbeatAt: string | null
  lastSuccessAt: string | null
  lastStatus: string | null
  lastError: string | null
  revokedAt: string | null
}

export type BranchWithConnector = {
  branch: BusinessBranch
  connector: ConnectorInstallation | null
}

export type AgentLatest = {
  version: string | null
  downloadUrl: string | null
}

export type DashboardBranch = {
  code: string
  name: string
  timezone: string
  lastSyncAt: string | null
  freshness: 'fresh' | 'stale' | 'missing'
  reconciliationOk: boolean | null
  rangeStart: string | null
  rangeEnd: string | null
  syncRequestedAt: string | null
}

export type DashboardMeta = {
  branchId: string
  branchCode: string
  branchName: string
  timezone: string
  date: string
  lastSyncAt: string | null
  lastBatchId: string | null
  rangeStart: string | null
  rangeEnd: string | null
  reconciliationOk: boolean | null
  freshness: 'fresh' | 'stale' | 'missing'
  coverage: 'complete' | 'partial' | 'missing' | 'invalid'
  canShowData: boolean
  shiftId: number | null
  shiftNumber: number | null
  shiftIsOpen: boolean
}

export type DashboardShift = {
  id: number
  number: number
  openedAt: string | null
  closedAt: string | null
  cashier: string | null
  isOpen: boolean
}

export type DashboardSummary = {
  tickets: number | null
  sales: number | null
  averageTicket: number | null
  tips: number | null
  cancelledTickets: number | null
  cancelledLines: number | null
  cashIn: number | null
  cashOut: number | null
  cashSales: number | null
  cardSales: number | null
  otherSales: number | null
  openingFund: number | null
  declaredCash: number | null
  expectedCash: number | null
  cashDifference: number | null
  paymentBreakdownComplete: boolean
  previousSales: number | null
  salesChangePercent: number | null
  openAccounts: number | null
  openAccountsTotal: number | null
  currentActivity: number | null
}

export type HourlySalesPoint = {
  hour: number
  sales: number
  tickets: number
}

export type SalesTicket = {
  folio: number
  checkNumber: string | null
  openedAt: string | null
  closedAt: string | null
  total: number | null
  tip: number | null
  paid: boolean
  cancelled: boolean
  table: string | null
  paymentUser: string | null
}

export type TransientAccount = {
  tempFolio: number
  checkNumber: string | null
  openedAt: string | null
  total: number | null
  tip: number | null
  paid: boolean
  table: string | null
  waiter: string | null
  paymentUser: string | null
}

export type CancellationItem = {
  date: string
  folio: number | null
  productId: string | null
  description: string | null
  quantity: number | null
  price: number | null
  occurrences: number
  user: string | null
  reason: string | null
}

export type CashMovementItem = {
  folio: number
  date: string | null
  type: number
  amount: number | null
  concept: string | null
  reference: string | null
}

export type DashboardHome = {
  meta: DashboardMeta
  summary: DashboardSummary
  hourlySales: HourlySalesPoint[]
  recentTickets: SalesTicket[]
  openAccounts: TransientAccount[]
  topProducts: {
    foods: TopProductItem[]
    beverages: TopProductItem[]
  }
  recentCancellations: CancellationItem[]
  recentCashMovements: CashMovementItem[]
}

export type TopProductItem = {
  productId: string
  productName: string
  groupName: string | null
  quantity: number
  sales: number
  rank: number
}

export type SalesPage = {
  meta: DashboardMeta
  items: SalesTicket[]
  page: number
  pageSize: number
  hasMore: boolean
}

export type CashMovementsPage = {
  meta: DashboardMeta
  items: CashMovementItem[]
  page: number
  pageSize: number
  hasMore: boolean
}

export type TicketDetail = {
  ticket: SalesTicket
  station: string | null
  restaurantArea: string | null
  waiterId: string | null
  cancellationReason: string | null
  cancelledBy: string | null
  lines: Array<{
    productId: string | null
    productName: string | null
    quantity: number | null
    price: number | null
    discount: number | null
    comment: string | null
  }>
  payments: Array<{
    paymentMethodId: string | null
    paymentMethodName: string | null
    paymentMethodType: number | null
    amount: number | null
    tip: number | null
    exchangeRate: number | null
    cardBrand: string | null
  }>
}
