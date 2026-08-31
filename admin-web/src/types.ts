export type Role = 'SUPERADMIN' | 'OWNER' | 'MANAGER' | 'VIEWER'

export const ROLES: Role[] = ['SUPERADMIN', 'OWNER', 'MANAGER', 'VIEWER']

export const ROLE_LABELS: Record<Role, string> = {
  SUPERADMIN: 'Super administrador',
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
  branchCount: number
}

export type UserBranch = {
  code: string
  name: string
  active: boolean
}

export type UserDetail = {
  id: string
  email: string
  displayName: string
  role: Role
  active: boolean
  lastLoginAt: string | null
  createdAt: string
  branches: UserBranch[]
}

export type UserMutationResponse = {
  user: UserDetail
  selfAffected: boolean
}

export type Branch = {
  id: string
  code: string
  name: string
  timezone: string
  active: boolean
  legacyAuthEnabled: boolean
  lastSyncAt: string | null
  createdAt: string
  syncRequestedAt: string | null
}

export type ActivationKeyResult = {
  id: string
  activationKey: string
  expiresAt: string
}

export type Connector = {
  id: string
  branchCode: string
  machineName: string
  active: boolean
  agentVersion: string | null
  createdAt: string
  lastSeenAt: string | null
  lastIp: string | null
  lastUserAgent: string | null
  revokedAt: string | null
  tokenRotatedAt: string | null
  lastStatus: string | null
  lastError: string | null
  pendingBatches: number | null
  lastHeartbeatAt: string | null
  lastSyncRequestHandledAt: string | null
}

export type RotatedCredential = {
  connectorId: string
  branchCode: string
  token: string
}
