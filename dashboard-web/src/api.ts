import type {
  AgentLatest,
  BranchWithConnector,
  BusinessMembership,
  DashboardBranch,
  DashboardHome,
  DashboardUser,
  CashMovementsPage,
  ConnectorInstallation,
  SalesPage,
  TicketDetail,
} from './types'

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  })

  if (!response.ok) {
    let message = response.status === 401 ? 'La sesión no es válida.' : 'No fue posible completar la consulta.'
    try {
      const body = (await response.json()) as { error?: string }
      if (body.error) message = body.error
    } catch {
      // La respuesta puede no contener JSON.
    }
    throw new ApiError(response.status, message)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  login: (email: string, password: string) =>
    request<{ user: DashboardUser; expiresAt: string }>('/api/web/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),
  logout: () => request<void>('/api/web/auth/logout', { method: 'POST' }),
  me: () => request<{ user: DashboardUser }>('/api/web/auth/me'),
  register: (email: string, password: string, displayName: string) =>
    request<{ user: DashboardUser; expiresAt: string }>('/api/web/auth/register', {
      method: 'POST',
      body: JSON.stringify({ email, password, displayName }),
    }),
  branches: () => request<DashboardBranch[]>('/api/web/branches'),
  businesses: () => request<BusinessMembership[]>('/api/web/businesses'),
  createBusiness: (name: string) =>
    request<BusinessMembership>('/api/web/businesses', { method: 'POST', body: JSON.stringify({ name }) }),
  businessBranches: (businessId: string) =>
    request<BranchWithConnector[]>(`/api/web/businesses/${encodeURIComponent(businessId)}/branches`),
  createBranch: (businessId: string, code: string, name: string) =>
    request<BranchWithConnector['branch']>(`/api/web/businesses/${encodeURIComponent(businessId)}/branches`, {
      method: 'POST',
      body: JSON.stringify({ code, name }),
    }),
  connectorInstallations: (branchCode: string) =>
    request<ConnectorInstallation[]>(`/api/web/branches/${encodeURIComponent(branchCode)}/connector-installations`),
  revokeConnector: (branchCode: string, installationId: string) =>
    request<{ installationId: string; active: boolean }>(
      `/api/web/connector-installations/${encodeURIComponent(installationId)}/revoke?branchCode=${encodeURIComponent(branchCode)}`,
      { method: 'POST' },
    ),
  agentLatest: () => request<AgentLatest>('/api/web/agent/latest'),
  requestSync: (branchCode: string) =>
    request<{ branchCode: string; syncRequestedAt: string }>(
      `/api/web/branches/${encodeURIComponent(branchCode)}/request-sync`,
      { method: 'POST' },
    ),
  dashboard: (branchCode: string, date: string, signal?: AbortSignal) =>
    request<DashboardHome>(
      `/api/web/dashboard/home?branchCode=${encodeURIComponent(branchCode)}&date=${date}`,
      { signal },
    ),
  sales: (branchCode: string, date: string, page: number, search: string, signal?: AbortSignal) => {
    const params = new URLSearchParams({ branchCode, date, page: String(page), pageSize: '20' })
    if (search.trim()) params.set('search', search.trim())
    return request<SalesPage>(`/api/web/sales?${params}`, { signal })
  },
  cashMovements: (
    branchCode: string,
    date: string,
    page: number,
    type: number | null,
    search: string,
    signal?: AbortSignal,
  ) => {
    const params = new URLSearchParams({ branchCode, date, page: String(page), pageSize: '20' })
    if (type !== null) params.set('type', String(type))
    if (search.trim()) params.set('search', search.trim())
    return request<CashMovementsPage>(`/api/web/cash-movements?${params}`, { signal })
  },
  ticket: (branchCode: string, folio: number, signal?: AbortSignal) =>
    request<TicketDetail>(`/api/web/sales/${encodeURIComponent(branchCode)}/${folio}`, { signal }),
}
