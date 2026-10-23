import type {
  ActivityFilters,
  ActivityPage,
  ActivitySummary,
  AdminUser,
  Branch,
  Business,
  BusinessRole,
  ConnectorInstallation,
  DeviceCredential,
  Role,
  Subscription,
  UserDetail,
  UserMutationResponse,
  UserSummary,
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
    let message = response.status === 401 ? 'La sesión no es válida o no tiene permisos de administrador.' : 'No fue posible completar la solicitud.'
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
    request<{ user: AdminUser; expiresAt: string }>('/api/web/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),
  logout: () => request<void>('/api/web/auth/logout', { method: 'POST' }),
  me: () => request<{ user: AdminUser }>('/api/web/auth/me'),

  businesses: () => request<Business[]>('/api/admin/businesses'),

  branches: () => request<Branch[]>('/api/admin/branches'),
  branch: (code: string) => request<Branch>(`/api/admin/branches/${encodeURIComponent(code)}`),
  createBranch: (businessId: string, code: string, name: string, timezone: string) =>
    request<Branch>('/api/admin/branches', {
      method: 'POST',
      body: JSON.stringify({ businessId, code, name, timezone }),
    }),
  updateBranch: (code: string, name: string, timezone: string) =>
    request<Branch>(`/api/admin/branches/${encodeURIComponent(code)}`, {
      method: 'PUT',
      body: JSON.stringify({ name, timezone }),
    }),
  setBranchActive: (code: string, active: boolean) =>
    request<Branch>(`/api/admin/branches/${encodeURIComponent(code)}/status`, {
      method: 'POST',
      body: JSON.stringify({ active }),
    }),
  requestSync: (code: string) =>
    request<Branch>(`/api/admin/branches/${encodeURIComponent(code)}/request-sync`, {
      method: 'POST',
    }),

  connectorInstallations: (branchCode: string) =>
    request<ConnectorInstallation[]>(`/api/admin/branches/${encodeURIComponent(branchCode)}/connector-installations`),
  revokeConnectorInstallation: (installationId: string) =>
    request<{ installationId: string; active: boolean }>(`/api/admin/connector-installations/${installationId}/revoke`, {
      method: 'POST',
    }),
  replaceDevice: (branchCode: string, machineName: string) =>
    request<DeviceCredential>(`/api/admin/branches/${encodeURIComponent(branchCode)}/replace-device`, {
      method: 'POST',
      body: JSON.stringify({ machineName }),
    }),

  users: () => request<UserSummary[]>('/api/admin/users'),
  user: (id: string) => request<UserDetail>(`/api/admin/users/${id}`),
  createUser: (email: string, displayName: string, password: string, role: Role) =>
    request<UserDetail>('/api/admin/users', {
      method: 'POST',
      body: JSON.stringify({ email, displayName, password, role }),
    }),
  updateUser: (id: string, displayName: string, role: Role) =>
    request<UserMutationResponse>(`/api/admin/users/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ displayName, role }),
    }),
  setUserActive: (id: string, active: boolean) =>
    request<UserMutationResponse>(`/api/admin/users/${id}/status`, {
      method: 'POST',
      body: JSON.stringify({ active }),
    }),
  activateSubscription: (id: string, plan: 'BASIC' | 'PLUS' | 'UNLIMITED', months: 1 | 2 | 3 | 6) =>
    request<Subscription>(`/api/admin/users/${id}/subscription/activate`, {
      method: 'POST',
      body: JSON.stringify({ plan, months }),
    }),
  setSubscriptionSuspended: (id: string, suspended: boolean) =>
    request<Subscription>(`/api/admin/users/${id}/subscription/status`, {
      method: 'POST',
      body: JSON.stringify({ suspended }),
    }),
  resetSubscriptionTime: (id: string) =>
    request<Subscription>(`/api/admin/users/${id}/subscription/reset-time`, { method: 'POST' }),
  subscriptionNoticeDesign: () => request<{ design: 'A' | 'B' }>('/api/admin/settings/subscription-notice'),
  setSubscriptionNoticeDesign: (design: 'A' | 'B') => request<{ design: 'A' | 'B' }>(
    '/api/admin/settings/subscription-notice',
    { method: 'POST', body: JSON.stringify({ design }) },
  ),
  resetUserPassword: (id: string, password: string) =>
    request<UserMutationResponse>(`/api/admin/users/${id}/password`, {
      method: 'POST',
      body: JSON.stringify({ password }),
    }),
  assignUserBusinesses: (id: string, businessIds: string[], role: BusinessRole) =>
    request<UserDetail>(`/api/admin/users/${id}/businesses`, {
      method: 'POST',
      body: JSON.stringify({ businessIds, role }),
    }),
  removeUserBusiness: (id: string, businessId: string) =>
    request<{ removed: boolean }>(`/api/admin/users/${id}/businesses/${encodeURIComponent(businessId)}`, {
      method: 'DELETE',
    }),

  activity: (filters: ActivityFilters, page: number, signal?: AbortSignal) =>
    request<ActivityPage>(`/api/admin/activity?${activityParams(filters, { page: String(page), pageSize: '40' })}`, { signal }),
  activitySummary: (filters: ActivityFilters, signal?: AbortSignal) =>
    request<ActivitySummary>(
      `/api/admin/activity/summary?${activityParams(filters, { tz: Intl.DateTimeFormat().resolvedOptions().timeZone })}`,
      { signal },
    ),
}

/** Las fechas se interpretan en la zona horaria del navegador: "hasta" incluye todo ese día. */
function activityParams(filters: ActivityFilters, extra: Record<string, string>): string {
  const params = new URLSearchParams(extra)
  if (filters.userId) params.set('userId', filters.userId)
  if (filters.businessId) params.set('businessId', filters.businessId)
  if (filters.branchCode) params.set('branchCode', filters.branchCode)
  if (filters.eventType) params.set('eventType', filters.eventType)
  if (filters.fromDate) params.set('from', new Date(`${filters.fromDate}T00:00:00`).toISOString())
  if (filters.toDate) {
    const end = new Date(`${filters.toDate}T00:00:00`)
    end.setDate(end.getDate() + 1)
    params.set('to', end.toISOString())
  }
  if (filters.includeAdmins) params.set('includeAdmins', 'true')
  return params.toString()
}
