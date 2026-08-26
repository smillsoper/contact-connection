// Shared shape for the generic version-history subsystem (EntityVersion / IVersionHistoryService
// on the backend — see API_HARDENING_CHECKLIST.md Tier 1). Covers Flows, TenantApiDefinition/
// TenantApiEndpoint (admin), and PortalApiDefinition/PortalApiEndpoint (portal) — all five
// entity types return this same summary shape from their `.../versions` endpoint.
export interface EntityVersionSummary {
  versionNumber: number
  isActive: boolean
  createdById: string
  createdByName: string
  createdAt: string
  changeSummary: string | null
}
