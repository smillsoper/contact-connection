// Shared shape for the credential audit trail (CredentialAuditEntry / ICredentialAuditService on
// the backend — see API_HARDENING_CHECKLIST.md Tier 1). Records THAT a credential Set/Delete
// happened — actor, timestamp, key name, action — never the secret value itself. Admin and
// Portal both return this same shape from their `/credentials/{keyName}/audit` endpoint.
export interface CredentialAuditEntrySummary {
  keyName: string
  action: 'set' | 'delete'
  actorId: string
  actorName: string
  createdAt: string
}
