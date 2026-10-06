import { apiRequest } from './client';

export interface AuditLogEntry {
  id: string;
  // Who acted. "Unknown" for a sign-in attempt that matched no account.
  username: string;
  action: string;
  // The account an admin action was performed on; absent for sign-in and sign-out.
  targetUsername?: string | null;
  occurredAt: string;
  ipAddress: string;
}

// Newest first. The server caps how many entries one request returns.
export function listAuditLog(): Promise<AuditLogEntry[]> {
  return apiRequest<AuditLogEntry[]>('/api/audit-logs');
}
