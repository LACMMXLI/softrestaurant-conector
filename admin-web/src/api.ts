import type {
  ActivationKeyResult,
  AdminUser,
  Branch,
  Connector,
  Role,
  RotatedCredential,
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

  branches: () => request<Branch[]>('/api/admin/branches'),
  branch: (code: string) => request<Branch>(`/api/admin/branches/${encodeURIComponent(code)}`),
  createBranch: (code: string, name: string, timezone: string) =>
    request<Branch>('/api/admin/branches', {
      method: 'POST',
      body: JSON.stringify({ code, name, timezone }),
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

  connectors: (branchCode: string) =>
    request<Connector[]>(`/api/admin/branches/${encodeURIComponent(branchCode)}/connectors`),
  createActivationKey: (branchCode: string, expiresInMinutes: number, note: string) =>
    request<ActivationKeyResult>(`/api/admin/branches/${encodeURIComponent(branchCode)}/activation-keys`, {
      method: 'POST',
      body: JSON.stringify({ expiresInMinutes, note: note.trim() || null }),
    }),
  revokeConnector: (connectorId: string) =>
    request<{ connectorId: string; active: boolean }>(`/api/admin/connectors/${connectorId}/revoke`, {
      method: 'POST',
    }),
  rotateToken: (connectorId: string) =>
    request<RotatedCredential>(`/api/admin/connectors/${connectorId}/rotate-token`, {
      method: 'POST',
    }),
  disableLegacyAuth: (branchCode: string) =>
    request<{ branchCode: string; legacyAuthEnabled: boolean }>(
      `/api/admin/branches/${encodeURIComponent(branchCode)}/legacy-auth/disable`,
      { method: 'POST' },
    ),

  users: () => request<UserSummary[]>('/api/admin/users'),
  user: (id: string) => request<UserDetail>(`/api/admin/users/${id}`),
  createUser: (email: string, displayName: string, password: string, role: Role, branchCodes: string[]) =>
    request<UserDetail>('/api/admin/users', {
      method: 'POST',
      body: JSON.stringify({ email, displayName, password, role, branchCodes }),
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
  resetUserPassword: (id: string, password: string) =>
    request<UserMutationResponse>(`/api/admin/users/${id}/password`, {
      method: 'POST',
      body: JSON.stringify({ password }),
    }),
  assignUserBranches: (id: string, branchCodes: string[]) =>
    request<UserDetail>(`/api/admin/users/${id}/branches`, {
      method: 'POST',
      body: JSON.stringify({ branchCodes }),
    }),
  removeUserBranch: (id: string, branchCode: string) =>
    request<{ removed: boolean }>(`/api/admin/users/${id}/branches/${encodeURIComponent(branchCode)}`, {
      method: 'DELETE',
    }),
}
