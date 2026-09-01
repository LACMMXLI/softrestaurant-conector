// Rol de cuenta (app_users.role): solo distingue operador de plataforma de cuenta normal. El
// permiso real por negocio (OWNER/MANAGER/VIEWER) vive en BusinessRole/business_members.
export type Role = 'SUPERADMIN' | 'USER'

export const ROLES: Role[] = ['SUPERADMIN', 'USER']

export const ROLE_LABELS: Record<Role, string> = {
  SUPERADMIN: 'Super administrador',
  USER: 'Cuenta normal',
}

export type BusinessRole = 'OWNER' | 'MANAGER' | 'VIEWER'

export const BUSINESS_ROLES: BusinessRole[] = ['OWNER', 'MANAGER', 'VIEWER']

export const BUSINESS_ROLE_LABELS: Record<BusinessRole, string> = {
  OWNER: 'Propietario',
  MANAGER: 'Gerente',
  VIEWER: 'Solo lectura',
}

export type AdminUser = {
  id: string
  email: string
  displayName: string
  role: string
}

export type UserSummary = {
  id: string
  email: string
  displayName: string
  role: Role
  active: boolean
  lastLoginAt: string | null
  createdAt: string
  businessCount: number
  subscription: Subscription
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

export type UserBusiness = {
  businessId: string
  name: string
  slug: string
  active: boolean
  role: BusinessRole
}

export type UserDetail = {
  id: string
  email: string
  displayName: string
  role: Role
  active: boolean
  lastLoginAt: string | null
  createdAt: string
  businesses: UserBusiness[]
  subscription: Subscription
}

export type UserMutationResponse = {
  user: UserDetail
  selfAffected: boolean
}

export type Business = {
  id: string
  name: string
  slug: string
  active: boolean
  createdAt: string
}

export type Branch = {
  id: string
  businessId: string
  code: string
  name: string
  timezone: string
  active: boolean
  lastSyncAt: string | null
  createdAt: string
  syncRequestedAt: string | null
}

export type ConnectorInstallation = {
  id: string
  branchCode: string
  machineName: string
  active: boolean
  agentVersion: string | null
  createdAt: string
  linkedAt: string | null
  linkedByUserId: string | null
  lastSeenAt: string | null
  lastIp: string | null
  lastUserAgent: string | null
  revokedAt: string | null
  lastStatus: string | null
  lastError: string | null
  pendingBatches: number | null
  lastHeartbeatAt: string | null
  lastSuccessAt: string | null
  lastSyncRequestHandledAt: string | null
}

export type DeviceCredential = {
  installationId: string
  branchCode: string
  businessId: string
  token: string
  apiUrl: string | null
}
