# ContactConnection Development Log

**Project:** ContactConnection Platform
**LLC:** Call Center Solutions, LLC
**Repository:** https://github.com/smillsoper/hubion

---

## Summary

| Session | Date | Start | End | Duration | Total Cumulative |
|---------|------|-------|-----|----------|-----------------|
| 1 | 2026-04-12 | 6:32 AM PDT | 7:04 AM PDT | 32 min | 32 min |
| 2 | 2026-04-12 | 7:04 AM PDT | 7:40 AM PDT | 36 min | 68 min |
| 3 | 2026-04-12 | 7:40 AM PDT | 11:12 AM PDT | ~212 min (incl. token limit reset 8:00–11:00 AM) | ~280 min |
| 4 | 2026-04-19 | 8:44 AM PDT | 8:58 AM PDT | 14 min | ~294 min |
| 5 | 2026-04-20 | 9:50 AM PDT | 11:42 AM PDT | 112 min | ~406 min |
| 6 | 2026-04-20 | 3:29 PM PDT | 3:56 PM PDT | 27 min | ~433 min |
| 7 | 2026-04-20 | 3:56 PM PDT | 4:49 PM PDT | 53 min | ~486 min |
| 8 | 2026-04-20 | 4:53 PM PDT | 5:00 PM PDT | 7 min | ~493 min |
| 9 | 2026-04-21 | 6:15 AM PDT | 6:31 AM PDT | 16 min | ~509 min |
| 10 | 2026-04-21 | 6:31 AM PDT | 6:45 AM PDT | 14 min | ~523 min |
| 11 | 2026-04-22 | 6:32 AM PDT | 6:47 AM PDT | 15 min | ~538 min |
| 12 | 2026-04-22 | 6:48 AM PDT | 7:14 AM PDT | 26 min | ~564 min |
| 13 | 2026-04-23 | 7:26 AM PDT | 8:22 AM PDT | 56 min | ~620 min |
| 14 | 2026-04-23 | 10:19 AM PDT | 10:36 AM PDT | 17 min | ~637 min |
| 15 | 2026-04-24 | 6:51 AM PDT | 7:12 AM PDT | 21 min | ~658 min |
| 16 | 2026-04-24 | 7:12 AM PDT | 7:58 AM PDT | 46 min | ~704 min |
| 17 | 2026-04-26 | 6:45 AM PDT | 6:58 AM PDT | 13 min | ~717 min |
| 18 | 2026-04-26 | 7:01 AM PDT | 7:59 AM PDT | 58 min | ~775 min |
| 19 | 2026-05-03 | 7:49 AM PDT | 9:11 AM PDT | 82 min | ~857 min |
| 20 | 2026-05-06 | 4:45 AM CDT | 5:36 AM CDT | 51 min | ~908 min |
| 21 | 2026-05-07 | 5:58 AM CDT | 6:09 AM CDT | 11 min | ~919 min |
| 22 | 2026-05-08 | 4:54 AM CDT | 5:25 AM CDT | 31 min | ~950 min |
| 23 | 2026-05-10 | 1:11 PM CDT | 1:55 PM CDT | 44 min | ~994 min |
| 24 | 2026-05-11 | 4:55 AM PDT | 5:06 AM PDT | 11 min | ~1005 min |
| 25 | 2026-05-11 | 5:06 AM PDT | 5:24 AM PDT | 18 min | ~1023 min |
| 26 | 2026-05-14 | 4:29 PM CDT | 6:03 PM CDT | 94 min | ~1117 min |
| 27 | 2026-05-14 | 6:07 PM CDT | 7:03 PM CDT | 56 min | ~1173 min |
| 28 | 2026-05-16 | 5:19 AM CDT | 7:06 AM CDT | 107 min | ~1280 min |
| 29 | 2026-05-19 | 4:52 PM CDT | 6:55 PM CDT | 123 min | ~1403 min |
| 30 | 2026-05-21 | 4:00 PM CDT | 5:10 PM CDT | 70 min | ~1473 min |
| 31 | 2026-05-21 | 5:15 PM CDT | 5:48 PM CDT | 33 min | ~1506 min |
| 32 | 2026-05-23 | 6:30 AM CDT | 7:17 AM CDT | 47 min | ~1553 min |
| 33 | 2026-05-25 | 7:11 AM CDT | 11:29 AM CDT | 258 min | ~1811 min |
| 34 | 2026-05-27 | 4:43 PM CDT | 5:12 PM CDT | 29 min | ~1840 min |
| 35 | 2026-05-27 | 5:26 PM CDT | 5:47 PM CDT | 21 min | ~1861 min |
| 36 | 2026-05-29 | 4:11 PM CDT | 5:56 PM CDT | 105 min | ~1966 min |
| 37 | 2026-05-30 | 9:41 AM CDT | 10:43 AM CDT | 62 min | ~2028 min |

---

## Session 37

**Date:** 2026-05-30
**Start:** 9:41 AM CDT
**End:** 10:43 AM CDT
**Duration:** 62 minutes

### Accomplished

**API Endpoint Test Runner — source context, test data tab, OAuth2, variable resolution fixes**

- Added `API_TYPE_SOURCE_CONTEXT` data map in `ApiDefinitionDetailContent.tsx`: maps API types (`address_validation`, `zip_code_lookup`) to a `SourceContext` object with namespace (`address`), label, description, and grouped fields (Name: firstName/lastName/middleInitial/company; Address: address1/address2/city/state/zip/zip4)
- Added `VarChip` component — click to copy `{{address.field}}` to clipboard; amber on hover; ✓ flash for 1.5s
- When an API type has a source context, the endpoint edit modal widens to `max-w-4xl` and renders a right-side **Variables** reference panel (w-56) showing all available `{{namespace.field}}` tags grouped by category
- Added **Test** tab to endpoint modal (alongside Params/Headers/Body); only visible when definition has a source context
- Test tab renders an address entry form (grid layout: first/last name, middle initial, company, address1, address2, city/state/zip/zip4) with labeled fields matching the Address flow node's entry fields
- `runEndpointTest()` — collects test data from form fields, calls `api.testEndpoint(definitionId, payload)`, displays resolved URL, status code (color-coded), success/fail badge, error box, and pretty-printed response body
- Created `ApiEndpointTestHelper.cs` — static helper for `RunTest` and `ApplyAuth`:
  - `SubstituteVars` — regex replaces `{{namespace.field}}` tags; unmatched variables in the namespace resolve to `""` (not literal tag) to prevent literal template text being sent to external APIs
  - `RunTest` — builds HTTP request (method, URL, substituted path, query params, headers, body), applies auth, sends via `IHttpClientFactory`, pretty-prints JSON response, returns `RunEndpointTestResponse`
  - Query params: supports `_skipIfEmpty` metadata array — keys listed there are omitted from the request when their resolved value is empty; all other params always sent regardless of value
  - `ApplyAuth` — handles api_key (header/query), bearer, basic, and **oauth2** (client credentials flow: header Basic or body placement, exchanges for access token, applies as `Authorization: Bearer {token}`)
- Added `POST .../endpoints/test` route in `PortalApiEndpointsEndpoints.cs` and `AdminApiEndpointsEndpoints.cs`
- Added `testAdminEndpoint` / `testPortalEndpoint` API functions in `adminApiDefinitions.ts` and `portal.ts`
- Wired `testEndpoint` into `DetailApi` interface and both portal/admin page objects
- `KVRow` extended with `skipIfEmpty?: boolean`; `kvToJson` outputs `_skipIfEmpty` metadata array; `jsonToKv` reads it back
- `KVEditor` extended with `showSkipToggle?: boolean` — renders amber `∅` toggle button per row in the Params editor; legend text added below editor explaining the toggle behavior
- Build: **0 errors** ✓ (backend + frontend)

---

## Session 36

**Date:** 2026-05-29
**Start:** 4:11 PM CDT
**End:** 5:56 PM CDT
**Duration:** 105 minutes

### Accomplished

**API Endpoint Detail Page — full backend + frontend**

- Created `PortalApiEndpoint` and `TenantApiEndpoint` domain entities: Id, DefinitionId, Name, Description, Path, HttpMethod (nullable = inherit from definition), RequestBodyTemplate, QueryParams/Headers/ResponseMapping (jsonb), SortOrder, IsActive
- Created `IPortalApiEndpointRepository` and `ITenantApiEndpointRepository` interfaces in Application
- Created `PortalApiEndpointConfiguration` and `TenantApiEndpointConfiguration` EF configs; registered in `ContactConnectionDbContext` and `TenantDbContext`; added `PortalApiEndpoints` and `TenantApiEndpoints` DbSets
- Created `PortalApiEndpointRepository` (uses `ContactConnectionDbContext` directly) and `TenantApiEndpointRepository` (lazy `ScopedTenantDbContextFactory` pattern); registered both in `ServiceCollectionExtensions`
- Created `PortalApiEndpointsEndpoints.cs` — CRUD routes nested under `/api/v1/portal/api-definitions/{definitionId}/endpoints`; validates parent definition exists before create; scope-checks `endpoint.DefinitionId` on all get/update/delete
- Created `AdminApiEndpointsEndpoints.cs` — identical structure under `/api/v1/admin/api-definitions/{definitionId}/endpoints` with `TenantContext.HasTenant` guards
- Registered both route groups in `Program.cs`
- Migrations: `AddPortalApiEndpoints` → `public.portal_api_endpoints`; `AddTenantApiEndpoints` → `tenant_api_endpoints` — both applied ✓
- Frontend: added `ApiEndpointRecord`, `CreateApiEndpointData`, `UpdateApiEndpointData` types + list/create/update/delete functions to `portal.ts` and `adminApiDefinitions.ts`
- Created shared `ApiDefinitionDetailContent.tsx` component: definition header card (name, provider, API type badge, active/inactive badge, method, base URL, auth badge, timeout) with Activate/Deactivate + Edit Definition buttons; Endpoints table (name, method badge, path, description) with Add/Edit/Delete; Edit Definition modal (full AuthConfigForm); Add/Edit Endpoint modal
- Created `PortalApiDefinitionDetailPage.tsx` — thin wrapper in `PortalShell` wiring portal API functions into the shared component
- Created `AdminApiDefinitionDetailPage.tsx` — same for `AdminShell` with admin API functions
- Updated `PortalApiDefinitionsPage.tsx` and `AdminApiDefinitionsPage.tsx`: "Edit" button now navigates to `/portal/api-definitions/:id` or `/admin/api-definitions/:id` instead of opening the inline modal
- Added `/portal/api-definitions/:id` and `/admin/api-definitions/:id` routes in `App.tsx`

**Bug fix — Key Vault credential name mismatch showing false ⚠**

- Root cause: `KeyVaultCredentialStoreBase.Sanitize()` converts underscores → hyphens (`usps_client_id` → `portal--usps-client-id`); `ListAsync` returns the vault-sanitized name (e.g. `usps-client-id`); `CredentialKeyField` checked exact match against `knownCredentials`, so `usps_client_id` ≠ `usps-client-id` → always showed ⚠ even when credential was in Key Vault
- Fixed in `AuthConfigForm.tsx`: `CredentialKeyField` now also compares against the vault-sanitized form of the input key (lowercase, non-alphanumeric → hyphens, trim hyphens) so stored credentials resolve to ✓ correctly

**Endpoint modal — Query Params, Headers, Body tabs**

- Replaced the simple Request Body Template textarea with a tabbed UI: Query Params | Headers | Body
- `KVEditor` sub-component: auto-appends a blank row when the last row gets input (Postman-style flow); `×` delete on each row; renders `key` and `value` inputs side-by-side
- Query Params tab: key → param name, value → static or `{{namespace.field}}` variable; serializes to/from JSON object stored in `queryParams` column
- Headers tab: key input backed by `<datalist id="header-key-suggestions">` with 23 common HTTP headers (Accept, Content-Type, Authorization, Cache-Control, X-API-Key, etc.) for browser-native autocomplete; serializes to/from `headers` column
- Body tab: request body template textarea with variable syntax hint
- Tab badges: blue count bubble on tab shows number of configured rows, visible at a glance
- Build: **0 errors** ✓ (backend + frontend)

---

## Session 35

**Date:** 2026-05-27
**Start:** 5:26 PM CDT
**End:** 5:47 PM CDT
**Duration:** 21 minutes

### Accomplished

**API Builder — Base URL rename + full Auth Config sub-form**

- Renamed `EndpointUrl` → `BaseUrl` on both `PortalApiDefinition` and `TenantApiDefinition` domain entities; updated EF configs (`endpoint_url` → `base_url` column name); updated all API endpoint request records (`CreateApiDefinitionRequest`, `UpdateApiDefinitionRequest`) and `ToResponse()` shapes; updated all TypeScript API types in `portal.ts` and `adminApiDefinitions.ts`
- Migration `RenameEndpointUrlToBaseUrl` applied to both contexts via PostgreSQL `RENAME COLUMN` — no data loss
- Added `Auth` column to both list table views showing the active auth method as a colored badge
- Built `src/components/apiDefinitions/AuthConfigForm.tsx` — shared reusable auth configuration component:
  - **None** — no auth
  - **API Key** — header or query param, param name, credential key reference
  - **Basic Auth** — username + password credential key references
  - **Bearer Token** — static token credential key reference
  - **OAuth2** — token URL, method, grant type, scopes, client ID/secret credential key refs; credential placement toggle (Header Basic Auth / Body JSON or Form); token request body template with `{{credential_key}}` variable substitution (consistent with flow engine convention); content-type selector (JSON / form-encoded); auto-generates template from credential keys; re-generates on key changes until user manually edits; "Reset to default" button; token response field mapping (token field + expires-in field)
  - **HMAC Signature** — algorithm, secret key ref, signature header name, timestamp toggle
  - All credentials stored as key references (not plain values) — points to future Credentials store
- Updated `PortalApiDefinitionsPage.tsx` and `AdminApiDefinitionsPage.tsx` to use `AuthConfigForm`; modal enlarged to `max-w-2xl` with scrollable body to accommodate OAuth2 sub-form
- Build: **0 errors** ✓

---

## Session 34

**Date:** 2026-05-27
**Start:** 4:43 PM CDT
**End:** 5:12 PM CDT
**Duration:** 29 minutes

### Accomplished

**API Builder — scaffolding + `Provider` / `TimeoutSeconds` enhancement**

- Reviewed architectural screenshots from prior claude.ai conversation — confirmed two-tier API hierarchy (portal fallback → tenant override) and identified two missing fields: `Provider` (string, nullable) and `TimeoutSeconds` (int, default 30)
- Added `Provider` and `TimeoutSeconds` to both `PortalApiDefinition` and `TenantApiDefinition` domain entities; updated `Create()` and `Update()` signatures on both
- Updated `PortalApiDefinitionConfiguration` and `TenantApiDefinitionConfiguration` EF configs — `provider varchar(100)` nullable, `timeout_seconds integer NOT NULL DEFAULT 30`
- Updated `CreateApiDefinitionRequest` and `UpdateApiDefinitionRequest` shared records to include `Provider?` and `TimeoutSeconds?`
- Updated `Create`/`Update` calls and `ToResponse()` in both `AdminApiDefinitionsEndpoints` and `PortalApiDefinitionsEndpoints`
- Generated and applied migrations:
  - `AddApiDefinitionProviderTimeout` → `ContactConnectionDbContext` (adds `provider`, `timeout_seconds` to `public.portal_api_definitions`) ✓
  - `AddApiDefinitionProviderTimeout` → `TenantDbContext` (adds same columns to `tenant_api_definitions`) ✓
- Built portal API definitions TypeScript API (`portal.ts` extended with `ApiDefinitionRecord`, `CreateApiDefinitionData`, `UpdateApiDefinitionData`, all 6 CRUD + activate/deactivate/delete functions)
- Created `src/api/adminApiDefinitions.ts` — identical structure using `api` client with tenant auth
- Built `PortalApiDefinitionsPage.tsx` — full CRUD UI with create/edit modal, activate/deactivate toggle, delete; type badge colors per API type
- Built `AdminApiDefinitionsPage.tsx` — same UX but under tenant admin shell; empty state explains platform defaults are used when no override exists
- Added "API Definitions" nav item to `PortalShell.tsx` and `AdminShell.tsx`
- Wired `/portal/api-definitions` and `/admin/api-definitions` routes in `App.tsx`
- Build: **0 errors** ✓ (both backend + frontend)

---

## Session 1

**Date:** 2026-04-12
**Start:** 6:32 AM PDT
**End:** 7:04 AM PDT
**Duration:** 32 minutes

### Accomplished

- Reviewed ARCHITECTURE.md in full; established authoritative reference for all future sessions
- Assessed initial project scaffold state against Session 1 goals from ARCHITECTURE.md
- Created `ContactConnection.Domain` project (Clean Architecture domain layer — replaces incorrectly named `ContactConnection.Core`)
- Created `ContactConnection.Application` project (Clean Architecture business logic / use cases layer)
- Established correct dependency chain: `Domain ← Application ← Infrastructure ← Api`
- Wired all project references in `.csproj` files and `ContactConnection.slnx`
- Removed `Class1.cs` placeholders from all projects
- Initialized .NET User Secrets for `ContactConnection.Api` — connection string removed from `appsettings.Development.json`
- Cleaned `Program.cs` of WeatherForecast boilerplate
- Expanded `docker-compose.yml` to full local dev stack — Redis, Nginx, pgAdmin, MailHog (FreeSWITCH commented out until telephony session)
- Created `nginx/local.conf` — wildcard subdomain routing with SignalR WebSocket support
- Added `freeswitch/conf` and `freeswitch/sounds` directory placeholders
- Created `.gitignore` (standard .NET, secrets, OS, IDE entries)
- Initialized git repository and pushed to GitHub (`smillsoper/hubion`)
- Created `CLAUDE.md` session memory file and `DevLog.md` (this file)
- Built `Tenant` domain entity with factory method (`Tenant.Create`)
- Built `TenantFeatureFlags` value object (all 8 flags, default all off)
- Built `ITenantRepository` and `ITenantProvisioningService` Application interfaces
- Built `TenantConfiguration` (EF fluent — snake_case, JSONB feature_flags, all unique indexes)
- Built `TenantRepository` and `TenantProvisioningService` (creates tenant record + PostgreSQL schema atomically)
- Built `ServiceCollectionExtensions.AddInfrastructure()` — clean DI registration pattern
- Built `TenantResolutionMiddleware` — resolves tenant from `X-Tenant-Subdomain` header per request
- Built tenant endpoints: `POST /api/v1/tenants` (provision), `GET /api/v1/tenants/{id}`
- Created and applied EF migration `CreateTenantsTable`
- Verified `public.tenants` table in PostgreSQL — correct columns, types, indexes

### Session 1 Goals Status (from ARCHITECTURE.md §29)

| Deliverable | Status |
|---|---|
| Solution structure created | ✓ Complete |
| Secrets management wired (.NET User Secrets for local dev) | ✓ Complete |
| PostgreSQL connection with EF Core + Npgsql | ✓ Complete |
| Docker compose stack running locally | ✓ Complete |
| Tenant schema and provisioning | ✓ Complete |
| First API endpoint returning call record data | Deferred → Session 2 |

---

## Session 2

**Date:** 2026-04-12
**Start:** 7:04 AM PDT
**End:** 7:40 AM PDT
**Duration:** 36 minutes

### Accomplished

- Oriented with `Original Application/CRMPro/crmPro_SharedClasses` — confirmed caller identity model (single record, no multi-contact join), confirmed address flags from production (`IsOutlyingUS`, `IsAKHI`)
- Added `Original Application/` to `.gitignore` — reference code is local only, never committed
- Built `AddressData` value object — full production field set including all address classification flags
- Built `CallAddresses` value object — billing + shipping JSONB envelope
- Built `CommitmentEvent` value object — lock registry entry, supervisor override fields
- Built `CallRecord` entity — full schema per ARCHITECTURE.md §19
  - All relational columns with correct types and lengths
  - `handle_time_seconds` as PostgreSQL generated stored column
  - Typed JSONB: `addresses`, `commitment_events`
  - Opaque JSONB strings for engine-owned fields: `flow_execution_state`, `api_response_cache`, `telephony_events`
  - PCI sensitive data lifecycle fields (`sensitive_data`, `stored_at`, `wiped_at`, `wipe_reason`)
  - `AddInteraction()`, `SetCallerIdentity()`, `Complete()`, `DeriveOverallStatus()`, etc.
- Built `CallInteraction` entity — full schema per ARCHITECTURE.md §22 with `InteractionType` and `InteractionStatus` constants
- Built `TenantContext` scoped service — holds resolved `Tenant` for current request, feeds `TenantDbContextFactory`
- Built `ICallRecordRepository` Application interface
- Built `TenantDbContext` — no default schema, relies on `search_path` per connection
- Built `TenantDbContextFactory` — builds context with `Search Path=tenant_{schema},public` per request
- Built `ScopedTenantDbContextFactory` — resolves current tenant from `TenantContext` and creates context
- Built `TenantDbContextDesignTimeFactory` — EF tooling support targeting `tenant_tms`
- Built `CallRecordConfiguration` — all columns, 7 indexes per ARCHITECTURE.md §19, JSONB converters
- Built `CallInteractionConfiguration` — full EF config with FK cascade
- Built `CallRecordRepository`
- Updated `TenantProvisioningService` — now runs `MigrateAsync` on new tenant schema at provision time
- Updated `ServiceCollectionExtensions` — all new services registered
- Updated `TenantResolutionMiddleware` — now also populates `TenantContext` service
- Built `GET /api/v1/call-records/{id}` — tenant-scoped, returns full record with interactions
- Created and applied EF migration `CreateCallRecordsTables` to `tenant_tms` schema
- Verified `tenant_tms.call_records` and `tenant_tms.call_interactions` in PostgreSQL

---

## Session 3

**Date:** 2026-04-12
**Start:** 7:40 AM PDT
**End:** 11:12 AM PDT
**Duration:** ~212 min active (token limit hit at ~8:00 AM PDT; context reset and resumed at ~11:00 AM PDT)

### Accomplished

- Added `ContactConnection.Infrastructure` NuGet packages: `BCrypt.Net-Next`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `System.IdentityModel.Tokens.Jwt`
- Built `Agent` domain entity — tenant-scoped, stored in tenant schema
  - Factory method `Agent.Create(tenantId, firstName, lastName, email, passwordHash, role)`
  - `RecordLogin()` updates `LastLoginAt`
  - `AgentRole` static class with constants: `agent`, `supervisor`, `admin`
- Built Application interfaces: `IPasswordHasher`, `ITokenService`, `IAgentRepository`
- Built `PasswordHasher` — BCrypt enhanced hashing, work factor 12
- Built `JwtTokenService` — HS256, 480-min expiry, claims: sub/email/given_name/family_name/jti/tenant_id/tenant_schema/tenant_subdomain/role
- Built `AgentConfiguration` — `agents` table, snake_case columns, unique email index, password_hash max 100 chars
- Built `AgentRepository` — lazy `ScopedTenantDbContextFactory` init pattern (same as `CallRecordRepository`)
- Updated `TenantDbContext` — added `Agents` DbSet
- Created and applied EF migration `AddAgentsTable` to `tenant_tms` schema
- Updated `ServiceCollectionExtensions` — wired `IPasswordHasher` (singleton), `ITokenService` (scoped), `IAgentRepository` (scoped)
- Built JWT Bearer pipeline in `Program.cs` — ValidateIssuer/Audience/Lifetime/SigningKey, 1-min clock skew
- Updated `appsettings.json` with Jwt section (Issuer/Audience/ExpiryMinutes); SigningKey in User Secrets only
- Built `POST /api/v1/auth/login` — BCrypt verify, RecordLogin(), GenerateToken(); identical 401 for unknown email and wrong password (no enumeration)
- Built `GET /api/v1/agents/{id}` — RequireAuthorization(), PasswordHash excluded from response
- Built `POST /api/v1/agents` — AllowAnonymous() bootstrap endpoint for first agent creation
- Fixed TenantDbContext DI eager resolution bug — removed TenantDbContext from DI entirely; repositories hold `ScopedTenantDbContextFactory` and lazy-init `Db` via `_db ??= _factory.Create()`
- Seeded TMS tenant directly via SQL INSERT (bootstrap catch-22: protected endpoint, no agents yet)
- **Verified end-to-end auth flow:**
  - `POST /api/v1/agents` (AllowAnonymous) → 200 with agent ID
  - `POST /api/v1/auth/login` correct credentials → 200 with signed JWT
  - `GET /api/v1/agents/{id}` with Bearer token → 200
  - `GET /api/v1/agents/{id}` without token → 401
  - `POST /api/v1/auth/login` wrong password → 401

### Session 3 Goals Status (from ARCHITECTURE.md §29)

| Deliverable | Status |
|---|---|
| JWT Bearer authentication | ✓ Complete |
| BCrypt password hashing | ✓ Complete |
| Agent entity + repository | ✓ Complete |
| Login endpoint | ✓ Complete |
| Protected endpoint enforcement | ✓ Complete |

---

## Session 4

**Date:** 2026-04-19
**Start:** 8:44 AM PDT
**End:** 8:58 AM PDT
**Duration:** 14 minutes (analysis/planning only — session cut short)

### Accomplished

- Read ARCHITECTURE.md in full at session start
- Deep analysis of CRMPro source files to ground flow engine design in real production patterns:
  - `ControlClasses.vb` (10,117 lines) — all control/node types, base class, runtime data model
  - `clsCodeGenerator.vb` (7,177 lines) — script compilation, stored vs. displayed script, code generation
  - `AccordionControl.vb` / `AccordionItem.vb` — script structure (accordion panels = flow steps)
  - `AddressControl.vb` — composite address capture control

### Key Findings from CRMPro Analysis

**Tag syntax:** `<*...*>` delimiters enclosing **executable VB.NET code** (not field references). ContactConnection will use `{{namespace.field}}` declarative syntax — a deliberate upgrade for safety and no-code ownership.

**Script structure:** `AccordionControl` is the top-level container; each `AccordionItem` panel = one flow step (node). Navigation is linear by default; branching is procedural VB.NET in button click handlers setting `SelectedIndex`.

**Runtime data available to scripts:**
- `CallDetail` (clsCall) → maps to `{{call_record.*}}` + `{{caller.*}}`
- `User` (clsUser) → maps to `{{agent.*}}`
- `ScriptForm.[ControlName]` → maps to `{{input.[node_id]}}`
- `WebServiceControl.Response` → maps to `{{api.[node_id].*}}`

**Node types confirmed by real usage:**
- `script` ← RichTextBoxControl/LabelControl with ScriptBox template
- `input` ← TextBox, ComboBox, CheckBox, DateTimePicker, AddressControl
- `branch` ← button click If/Then/Else navigating to accordion index
- `api_call` ← WebServiceControl (all fields — URL, headers, body — are `<*...*>` template-based)
- `set_variable` ← PostCallCode response mapping after API call
- `end` ← final accordion panel with wrap-up button

**API call control (WebServiceControl):** Every field (URL, method, headers, body, auth) is template-based. Post-call VB.NET code block processes the response. ContactConnection maps this to `response_map` + `set_variable` — no code, fully declarative.

**Branching:** No declarative condition syntax in CRMPro — all embedded procedural code. ContactConnection introduces a proper `branch` node with explicit condition expressions.

### CRMPro → ContactConnection Design Translation

| CRMPro | ContactConnection |
|---|---|
| `<*VBCode*>` (executable) | `{{namespace.field}}` (declarative) |
| Button click If/Then → `SelectedIndex = N` | `branch` node with declared condition + transitions |
| Accordion panel index navigation | Named node transitions (explicit graph) |
| `ScriptForm.ControlName.Text` | `{{input.node_id}}` |
| `PostCallCode` VB snippet | `response_map` + `set_variable` nodes |
| Client-side WinForms execution | Server-side engine + SignalR push |

### Pending Decision for Next Session Start

**Branch condition complexity:** Simple expressions only (`==`, `>`, `<`, `contains`) evaluated by our own parser — covers 95% of real cases and is faster to build. Or richer boolean expressions (`&&`, `||`) from the start. Recommendation: **simple first, extend later.** Awaiting confirmation before building.

### Planned Build for Session 5

1. `Flow` domain entity + `flows` table
2. `FlowSession` domain entity + `flow_sessions` table
3. `IVariableResolver` + `VariableResolver` (all namespaces)
4. `FlowExecutionContext` (VariableStore, ExecutionHistory, LockedFields, CommitmentEvents)
5. `INodeHandler` + core handlers: `script`, `input`, `branch`, `set_variable`, `end`
6. `FlowEngine` service — StartFlow, AdvanceNode, ProcessInput; Redis active state + PostgreSQL on completion
7. `FlowHub` (SignalR in ContactConnection.Api) — pushes node state to agent UI
8. Endpoints: `POST /flows`, `GET /flows/{id}`, `POST /flow-sessions`, `POST /flow-sessions/{id}/advance`

→ All of the above were completed in Session 5.

---

## Session 5

**Date:** 2026-04-20
**Start:** 9:50 AM PDT
**End:** 11:42 AM PDT
**Duration:** 112 minutes (token limit hit near end while preparing to test)

### Accomplished

- Analyzed 4 production CRMPro scripts (Dexcom G7, Scrubzz, Shopify CS, ASPCA Donation) + GlobalClasses.vb
- Confirmed branch condition design decision: simple expressions first (`==`, `!=`, `>`, `<`, `>=`, `<=`, `contains`)
- Built full JSON flow engine — all layers:

**Domain**
- `Flow` entity — id, tenant_id, client_id, campaign_id, name, flow_type, version, is_active, definition (JSONB), Publish/Deactivate/UpdateDefinition methods
- `FlowSession` entity — id, flow_id, call_record_id, interaction_id, agent_id, current_node_id, status, variable_store (JSONB), execution_history (JSONB), AdvanceTo/Complete/Abandon methods
- `FlowType` static class: `crm` | `telephony`
- `FlowSessionStatus` static class: `active` | `complete` | `abandoned`

**Application**
- `IFlowRepository` — GetById, GetActiveByTenant, Add, SaveChanges
- `IFlowSessionRepository` — GetById, GetActiveByCallRecord, Add, SaveChanges
- `IVariableResolver` — Resolve, ExtractReferences, EvaluateCondition + `VariableContext` class
- `IFlowEngine` — StartAsync, AdvanceAsync, GetCurrentStateAsync + request/response types
- `IFlowNotifier` — PushNodeStateAsync, PushErrorAsync (keeps Infrastructure free of API dependencies)

**Infrastructure**
- `VariableResolver` — `{{namespace.field}}` tag regex, all namespaces (call_record, caller, agent, tenant, input, api, flow), condition evaluator (==, !=, >, <, >=, <=, contains)
- `FlowExecutionContext` — runtime state with serialization to/from Redis cache entry
- `NodeExecutionRecord` — immutable audit log entry
- `INodeHandler` + `NodeHandlerBase` — dispatch interface and shared JSON/state helpers
- Node handlers:
  - `ScriptNodeHandler` — resolves content, auto-advances on default transition
  - `InputNodeHandler` — waits for agent input, stores in Inputs[node_id], locked field guard
  - `BranchNodeHandler` — evaluates condition, follows true/false/default, transparent to agent
  - `SetVariableNodeHandler` — applies assignments array to FlowVars, transparent to agent
  - `ApiCallNodeHandler` — HTTP call with resolved URL/headers/body, response_map to ApiResults, commitment events on success
  - `EndNodeHandler` — terminal node, sets IsTerminal
- `FlowEngine` — session lifecycle (Start/Advance/GetCurrentState), Redis active state (12hr TTL), PostgreSQL on completion, auto-advance loop for transparent nodes (branch, set_variable), SignalR push via IFlowNotifier
- `FlowRepository`, `FlowSessionRepository` — lazy ScopedTenantDbContextFactory pattern
- `FlowConfiguration`, `FlowSessionConfiguration` — EF snake_case, JSONB columns, indexes
- Added `StackExchange.Redis` and `Microsoft.Extensions.Http` packages
- EF migration `AddFlowsAndFlowSessions` generated ✓

**API**
- `FlowHub : Hub<IFlowHubClient>` — JoinSession, LeaveSession, JoinSupervisorView; JWT WebSocket support (access_token query param)
- `FlowNotifier : IFlowNotifier` — wraps `IHubContext<FlowHub, IFlowHubClient>`
- `POST /api/v1/flows` — create draft flow
- `GET /api/v1/flows` — list active flows for tenant
- `GET /api/v1/flows/{id}` — get flow by id
- `POST /api/v1/flows/{id}/publish` — publish draft
- `POST /api/v1/flow-sessions` — start session (returns first node state)
- `GET /api/v1/flow-sessions/{id}` — get current node state (reconnect)
- `POST /api/v1/flow-sessions/{id}/advance` — submit agent input, get next node
- SignalR hub mapped at `/hubs/flow`
- `AddSignalR()` and `AddScoped<IFlowNotifier, FlowNotifier>()` in Program.cs
- All services registered in `ServiceCollectionExtensions`

**Build:** 0 warnings, 0 errors ✓
**Migration:** Generated ✓ — pending Docker startup to apply to database

### Pending for Next Session Start
- End-to-end test of flow engine (token limit hit before testing could begin):
  1. Start API: `dotnet watch run --project ContactConnection.Api`
  2. Login to get JWT: `POST /api/v1/auth/login` with `admin@tms.hubion.local`
  3. Create a simple test flow: `POST /api/v1/flows` with a 3-node definition (script → input → end)
  4. Publish it: `POST /api/v1/flows/{id}/publish`
  5. Start a session: `POST /api/v1/flow-sessions`
  6. Advance through nodes: `POST /api/v1/flow-sessions/{id}/advance`
  7. Verify Redis state during, PostgreSQL persistence after completion
- Session 6 (after test passes): Offer/Cart engine or Worker project

---

## Session 6

**Date:** 2026-04-20
**Start:** 3:29 PM PDT
**End:** 3:56 PM PDT
**Duration:** 27 minutes

### Accomplished

End-to-end flow engine test — verified full execution path from flow creation through session completion.

**Bugs discovered and fixed:**

1. **`MapInboundClaims = false` (Program.cs)** — ASP.NET Core's JWT bearer handler remaps the `sub` claim to `ClaimTypes.NameIdentifier` by default. All authorized endpoints called `http.User.FindFirst("sub")` expecting the raw claim name, which always returned null → 401. Fixed by setting `options.MapInboundClaims = false` in JWT bearer options so all JWT claim names are preserved as-is.

2. **`AdvanceInternalAsync` revert bug (FlowEngine.cs)** — After processing a non-auto-advance node (e.g. script) that returned a NextNodeId, the engine set `ctx.CurrentNodeId = result.NextNodeId` then immediately reverted it with `ctx.CurrentNodeId = nodeId`. This caused script nodes to loop forever — each Advance call re-processed the same node. Fixed by introducing an `isStart` parameter: `StartAsync` passes `isStart: true` (stays on entry node after processing), `AdvanceAsync` passes `isStart: false` (advances past the current node and processes the next one until reaching a node that waits for input or is terminal).

3. **Agent password mismatch** — The previously seeded admin agent's BCrypt hash didn't match `Admin123!`. The hash was from a prior session with an unknown password. Deleted and recreated the agent via the bootstrap endpoint.

**End-to-end test verified:**

| Step | Result |
|---|---|
| `POST /api/v1/auth/login` | ✓ 200 with JWT |
| `POST /api/v1/flows` (create draft) | ✓ 201 with flow id |
| `POST /api/v1/flows/{id}/publish` | ✓ 200, is_active=true |
| `POST /api/v1/flow-sessions` | ✓ 200, node_001 (script), `{{agent.id}}` resolved |
| `POST /api/v1/flow-sessions/{id}/advance` (acknowledge script) | ✓ 200, advances to node_002 (input) |
| `POST /api/v1/flow-sessions/{id}/advance` (submit "order") | ✓ 200, auto-advances branch→set_variable→end, `{{flow.call_type}}` = "order", isTerminal=true |
| Redis after completion | ✓ Key deleted |
| PostgreSQL flow_sessions | ✓ status=complete, variable_store has FlowVars + Inputs |
| "billing" path (false branch) | ✓ call_type="billing", same completion flow |

**Test flow definition:** script → input (select) → branch (condition: `{{input.node_002}} == order`) → set_variable (node_004 or node_005) → end

---

## Session 7

**Date:** 2026-04-20
**Start:** 3:56 PM PDT
**End:** 4:49 PM PDT
**Duration:** 53 minutes

### Accomplished

Completed CRMPro `DatabaseClasses.vb` commerce class analysis, designed and built the initial Offer/Inventory/Cart engine, identified enterprise-grade gaps, and established the roadmap to fill them.

**CRMPro analysis completed:**

Finished reading all commerce subclasses from `DatabaseClasses.vb`:
- `QPBTier` — `MinQty` + full `Payments` schedule (each QPB tier carries its own independent payment plan, not just a price)
- `clsOfferPayment` — `PaymentNumber`, `PaymentDescription`, `PaymentAmount`, `PaymentIntervalDays`
- `clsOfferShipMethod` — `MethodCode`, delivery window, `BusinessDaysOnly`, `Surcharge`
- `clsOfferPersonalization` — typed prompt (Boolean/Date/Decimal/Long/String), dependency chain, selection list, `ChargeAmount`
- `clsOfferFlag` — opaque key-value metadata
- `clsOfferKit` / `clsOfferVariableKit` / `clsOfferVariableKitItem` — fixed and agent-selectable kit components
- `enumBackorderStatus` — `Available` / `CanBackorder` / `NoBackorder` / `Discontinued`
- `AutoshipInterval` — `IntervalDays`, optional `AutoShipId`

**Key pricing insight confirmed:** QPB (Quantity Price Break) is a payment schedule override keyed by minimum quantity — not a flat price reduction. MixMatchTiers use identical structure but MinQty is checked against the summed quantity of all cart items sharing the same `MixMatchCode`. MixMatch takes priority over QPB.

**Built — Domain:**
- `PaymentInstallment`, `QuantityPriceBreak` value objects (`Commerce/ProductPricing.cs`)
- `ProductShipMethod` value object
- `PersonalizationPromptType` enum + `PersonalizationPrompt`, `PersonalizationDependency`, `PersonalizationSelectionItem` value objects
- `ProductFlag`, `AutoShipInterval` value objects
- `TierRange`, `CartPaymentBreakdown`, `CartPersonalizationAnswer`, `CartKitSelection`, `CartItem`, `CartDocument` value objects — full cart document structure
- `Product` entity — all scalar fields relational, complex structures (pricing tiers, personalization, ship methods, flags) as JSONB; `ProductInventoryStatus` enum; `CanAddToCart()` guard
- `ProductKit` entity — fixed and variable kit components with `CreateFixed()` / `CreateVariable()` factory methods
- `CallRecord.Cart` property + `SetCart()` method

**Built — Application:**
- `IProductRepository` — GetById, GetBySku, Search, GetByMixMatchCode, Add, SaveChanges
- `IPricingService` — `ResolvePayments()` (MixMatch → QPB → base fallback) + `CalculateTotals()`

**Built — Infrastructure:**
- `ProductConfiguration` — all scalar columns, 9 JSONB columns with `HasConversion` + `'[]'::jsonb` defaults, 4 indexes, self-referential FK for variants, cascade to kits
- `ProductKitConfiguration`
- Updated `CallRecordConfiguration` — added `cart` JSONB column
- `ProductRepository` — lazy `ScopedTenantDbContextFactory` pattern, `ILike` search on description
- `PricingService` — MixMatch → QPB → base fallback, weight/subtotal tier resolution, installment splitting with rounding correction in payment 1
- Updated `TenantDbContext` — added `Products`, `ProductKits` DbSets and configurations
- Updated `ServiceCollectionExtensions` — registered `IProductRepository` and `IPricingService`

**Built — API:**
- `POST /api/v1/products` — create product with optional pricing, inventory, payment plan
- `GET /api/v1/products` — search (ILike on description, paginated)
- `GET /api/v1/products/{id}`
- `GET /api/v1/products/sku/{sku}`
- `GET /api/v1/call-records/{id}/cart`
- `PUT /api/v1/call-records/{id}/cart` — accepts CartDocument, calls `CalculateTotals`, saves
- Registered `MapProductsEndpoints()` in `Program.cs`

**Build:** 0 warnings, 0 errors ✓
**Migration:** `AddProductsAndCart` — applied to `tenant_tms` ✓
**Tables verified:** `products`, `product_kits` created; `cart` JSONB column added to `call_records`

### Enterprise Gap Analysis

After building, identified 6 structural gaps between the current implementation and true enterprise-grade:

| Gap | Impact | Planned Session |
|---|---|---|
| Product/Offer separation (same product, multiple campaigns/price points) | High | Session 8 |
| Tax service integration point (`ITaxProvider` abstraction) | Medium | Session 9 |
| Inventory reservation (`qty_reserved` + session lifecycle) | Medium | Session 10 |
| Order entity (post-call fulfillment artifact — Order + OrderLine) | Medium | Session 11 |
| Subscription lifecycle for AutoShip | Medium | Session 12 |
| Category/attribute system (for large catalog clients) | Low (DR), High (catalog) | Session 13 |

Root cause: the original CRMPro system was a broker between the call center and the client's external OMS/fulfillment platform. These gaps are what elevate ContactConnection beyond that role into a full-stack contact center commerce platform.

---

## Session 8

**Date:** 2026-04-20
**Start:** 4:53 PM PDT
**End:** ~5:20 PM PDT
**Duration:** ~27 minutes

### Accomplished

Product/Offer separation — the most structurally significant commerce engine gap from Session 7's analysis. The same physical product can now be sold under multiple independent sales configurations (offers) without duplicating inventory tracking.

**Domain**
- Refactored `Product` entity — removed all pricing/sales fields; now holds only physical item identity, inventory, geographic surcharges, and catalog metadata. Added `Offers` navigation collection.
- Created `Offer` entity — all pricing/sales configuration fields moved from `Product` plus three new fields:
  - `Name` — display label distinguishing offers (e.g. "TV Special", "Web Offer", "Upsell")
  - `IsActive` — explicit activation gate; new offers start inactive (draft)
  - `ValidFrom` / `ValidTo` — optional campaign validity window (nullable `DateTimeOffset`)
  - `IsAvailable()` — checks `IsActive` + validity window in one call
- Updated `CartItem` value object — added `OfferId` field; `ProductId` + `Sku` are now explicitly documented as order-time snapshots

**Application**
- Created `IOfferRepository` — GetById, GetByProductId, GetActive, Add, SaveChanges
- Updated `IPricingService.ResolvePayments` — signature changed from `(Product, ...)` to `(Offer, ...)` to reflect that pricing configuration lives on the Offer

**Infrastructure**
- Updated `ProductConfiguration` — stripped all 24 pricing columns; `products` table now contains only physical/inventory/catalog fields
- Created `OfferConfiguration` — all pricing scalar columns, 7 JSONB columns (`payments`, `quantity_price_breaks`, `mix_match_price_breaks`, `auto_ship_intervals`, `ship_methods`, `personalization`, `flags`), 3 indexes (`ix_offers_product_id`, `ix_offers_mix_match_code`, `ix_offers_tenant_active`), cascade FK from products
- Created `OfferRepository` — lazy factory pattern; includes `Product` navigation on GetById/GetActive
- Updated `ProductRepository` — `GetByMixMatchCodeAsync` now queries via offers (`p.Offers.Any(o => o.MixMatchCode == code && o.IsActive)`)
- Updated `PricingService` — `ResolvePayments` takes `Offer` parameter; all logic unchanged (MixMatch → QPB → base fallback)
- Added `Offers` DbSet to `TenantDbContext` and `OfferConfiguration` to model builder
- Registered `IOfferRepository` → `OfferRepository` in `ServiceCollectionExtensions`

**API**
- Updated `ProductsEndpoints` — `CreateProductRequest` now contains only physical fields (Sku, Description, Weight, Inventory, GeographicSurcharges); product response includes `Offers` collection via `OffersEndpoints.ToResponse`
- Created `OffersEndpoints` — `POST /api/v1/offers`, `GET /api/v1/offers`, `GET /api/v1/offers/{id}`, `POST /api/v1/offers/{id}/activate`, `POST /api/v1/offers/{id}/deactivate`, `GET /api/v1/offers/product/{productId}`
- Registered `MapOffersEndpoints()` in `Program.cs`

**Build:** 0 warnings, 0 errors ✓
**Migration:** `SeparateProductFromOffer` — applied to `tenant_tms` ✓
- Dropped 24 pricing columns from `products`
- Created `offers` table with all moved fields + `name`, `is_active`, `valid_from`, `valid_to`

---

## Session 9

**Date:** 2026-04-21
**Start:** 6:15 AM PDT
**End:** 6:31 AM PDT
**Duration:** 16 minutes

### Accomplished

**Part 1 — Product/Offer separation verification**

All 7 API checks passed. Also discovered and fixed a missing global enum serialization config.

| Step | Expected | Result |
|---|---|---|
| `POST /products` with `"inventoryStatus": "Available"` (string) | 201 | ✓ — but failed initially |
| Fix: added `JsonStringEnumConverter` globally via `ConfigureHttpJsonOptions` | Enums as strings everywhere | ✓ |
| `POST /offers` (TV Special, 3-pay $29.95) | 201, `isActive: false` | ✓ |
| `POST /offers` (Web Offer, 1-pay $24.95) | 201, `isActive: false` | ✓ |
| `POST /offers/{id}/activate` (TV Special) | 200, `isActive: true` | ✓ |
| `GET /products/{id}` | 2 offers embedded, different prices/plans | ✓ |
| `GET /offers` | 1 result — TV Special only (active filter works) | ✓ |
| `GET /offers/product/{productId}` | 2 results — TV Special + Web Offer | ✓ |

**Bonus fix:** `JsonStringEnumConverter` registered globally in `Program.cs` via `ConfigureHttpJsonOptions` — all enums across all endpoints now serialize/deserialize as strings (e.g. `"Available"` instead of `0`).

**Part 2 — Tax service integration**

Built the `ITaxProvider` abstraction with a factory pattern, integrated it into `PricingService`, and verified correct calculations.

**Application**
- `ITaxProvider` — `ProviderKey` + `CalculateTaxAsync(CartDocument, ct)` → `TaxResult(Rate, TaxAmount, Jurisdictions?)`
- `ITaxProviderFactory` — `Resolve(providerKey?)` → correct provider; falls back to default
- `TaxResult` + `JurisdictionTax` records — support future multi-jurisdiction breakdown display
- `IPricingService.CalculateTotals` → renamed `CalculateTotalsAsync` (async — external providers need HTTP)
- `CartDocument.TaxProvider` field added — `null`/`""` = flat rate; future: `"avalara"`, `"taxjar"`

**Infrastructure**
- `FlatRateTaxProvider` — `ProviderKey = ""` (default); applies `CartDocument.TaxRate` directly to taxable subtotal; synchronous, no external calls
- `TaxProviderFactory` — builds dispatch dictionary from all registered `ITaxProvider` instances; throws on startup if no default provider registered
- `PricingService` — now takes `ITaxProviderFactory` in constructor; `CalculateTotalsAsync` delegates tax to resolved provider
- DI: `FlatRateTaxProvider` registered as singleton `ITaxProvider`; `TaxProviderFactory` registered as singleton `ITaxProviderFactory`

**API**
- `CallRecordsEndpoints.SetCart` — updated to `await pricing.CalculateTotalsAsync(...)`

**Bug found and fixed during verification:**
Payment installment breakdown logic was always calling `RoundSplit` for payment 1 even when `splitShippingInPayments`/`splitSalesTaxInPayments` were false. This caused payment 1 to receive 1/N of shipping and tax instead of the full amount. Fixed: when split = false, payment 1 gets the full value; when split = true, `RoundSplit` distributes evenly with remainder in payment 1.

**Verified cart calculation (1 item, 3-payment plan, 8.5% tax, split=false):**

| Field | Expected | Actual |
|---|---|---|
| cartSubtotal | 29.95 | 29.95 ✓ |
| shipping | 7.95 | 7.95 ✓ |
| salesTax | 2.55 | 2.55 ✓ |
| cartTotal | 40.45 | 40.45 ✓ |
| pmt 1 | sub=10.00, ship=7.95, tax=2.55, total=20.50 | ✓ |
| pmt 2 | sub=10.00, ship=0, tax=0, total=10.00 | ✓ |
| pmt 3 | sub=9.95, ship=0, tax=0, total=9.95 | ✓ |

**Build:** 0 warnings, 0 errors ✓

### Pending for Session 10
- **Gap 3: Inventory reservation** — `qty_reserved` column on `products`; reserve on add-to-cart, release on session abandon/expire, confirm (convert to decrement) on order commit

---

## Session 10

**Date:** 2026-04-21
**Start:** 6:31 AM PDT
**End:** 6:45 AM PDT
**Duration:** 14 minutes

### Accomplished

Gap 3: Inventory reservation — soft reservation system tracking units held in active carts, all-or-nothing reserve on cart set, automatic release on cart replace, confirmed decrement on order commit (scaffolded).

**Domain**
- `Product.QtyReserved` property — units currently held in active carts, not yet committed to orders
- `Product.Reserve(qty)` — increments `QtyReserved`; returns false (no change) if `CanAddToCart` fails; only tracks if `DecrementOnOrder = true`
- `Product.Release(qty)` — decrements `QtyReserved` (floor 0); called on cart replace/clear
- `Product.Confirm(qty)` — decrements both `QtyAvailable` and `QtyReserved` (floor 0); called on order commit
- `Product.CanAddToCart(qty)` — updated `NoBackorder` case to use `QtyAvailable - QtyReserved - qty >= MinimumQty` (net of reservations)

**Application**
- `IInventoryService` — `ReserveCartAsync`, `ReleaseCartAsync`, `ConfirmCartAsync`
  - `ReserveCartAsync` — all-or-nothing; validates all items before applying any change; returns list of failing SKUs on partial failure (empty = success)
  - `ReleaseCartAsync` — safe to call on null/empty cart
  - `ConfirmCartAsync` — converts soft reservations to real inventory decrements

**Infrastructure**
- `InventoryService` — lazy `ScopedTenantDbContextFactory` pattern; loads all referenced products in one query per operation; all-or-nothing reservation via validate-then-apply
- `ProductConfiguration` — `qty_reserved` mapped as `integer` column
- `ServiceCollectionExtensions` — registered `IInventoryService → InventoryService` (scoped)

**API**
- `PUT /api/v1/call-records/{id}/cart` — now injects `IInventoryService`:
  - Releases old cart reservations before applying new cart
  - Reserves new cart (all-or-nothing); if any SKU unavailable, restores old cart reservations and returns `409 Conflict` with `{ message, unavailableSkus: [...] }`
  - Only calls `CalculateTotalsAsync` and saves after reservation confirmed

**Database**
- Migration `AddInventoryReservation` — `ALTER TABLE products ADD COLUMN qty_reserved integer NOT NULL DEFAULT 0`
- Applied to `tenant_tms` ✓

**Verified end-to-end:**

| # | Test | Expected | Result |
|---|------|----------|--------|
| 1 | `PUT /cart` with qty=2, product has 3 available | 200, `qty_reserved=2` in DB | ✓ |
| 2 | Second cart requests 2 more (1 free) | 409, `unavailableSkus: ["INVTEST-001"]` | ✓ |
| 3 | Replace cart: qty 2→1 (release 2, reserve 1) | 200, `qty_reserved=1` in DB | ✓ |

**Build:** 0 warnings, 0 errors ✓

### Pending for Session 11
- **Gap 4: Order entity** — `Order` + `OrderLine` post-call fulfillment artifact; `ConfirmCartAsync` wired to order commit; `OrderStatus` lifecycle

---

## Session 11

**Date:** 2026-04-22
**Start:** 6:32 AM PDT
**End:** 6:47 AM PDT
**Duration:** 15 minutes

### Accomplished

Gap 4: Order entity — post-call fulfillment artifact with full lifecycle, inventory confirmation, and line-level fulfillment tracking.

**Domain**
- `Order` entity — `Id`, `TenantId`, `CallRecordId`; `Status` lifecycle (`confirmed` → `partially_shipped` / `shipped` / `delivered` / `cancelled`); financial snapshot (`Subtotal`, `Shipping`, `SalesTax`, `Discount`, `Total`); `PaymentBreakdowns` JSONB (mirrors cart); order-level fulfillment timestamps; `Cancel()` guard; `RefreshStatus()` — derives order status from line statuses and stamps `ShippedAt`/`DeliveredAt` on first transition
- `OrderLine` entity — full point-in-time snapshot of a `CartItem` at commit; all pricing, shipping, tax, personalization, kit, and payment fields frozen; `FulfillmentStatus` lifecycle (`pending` → `processing` / `shipped` / `delivered` / `cancelled` / `returned`); `Ship(trackingNumber)`, `MarkDelivered()`, `Cancel()` methods; `FromCartItem()` static factory
- `OrderStatus` + `OrderLineStatus` static constant classes

**Application**
- `IOrderRepository` — `GetByIdAsync`, `GetByCallRecordIdAsync`, `AddAsync`, `SaveChangesAsync`
- `IOrderService` — `CreateFromCartAsync(callRecordId)` → `(Order, created: bool)`; idempotent; throws on no/empty cart

**Infrastructure**
- `OrderRepository` — lazy `ScopedTenantDbContextFactory` pattern; `Include(o => o.Lines)` on both fetches
- `OrderService` — loads call record; returns existing order if already committed (idempotent); validates non-empty cart; calls `IInventoryService.ConfirmCartAsync` to convert soft reservations to real decrements; pre-generates `orderId` so `OrderLine.FromCartItem` and `Order.CreateFromCart` use the same ID
- `OrderConfiguration` — `orders` table, `payment_breakdowns` JSONB, 3 indexes (`ix_orders_call_record_id`, `ix_orders_tenant_id`, `ix_orders_tenant_status`)
- `OrderLineConfiguration` — `order_lines` table, 3 JSONB columns (`payments`, `personalization_answers`, `kit_selections`), 2 indexes, cascade delete from orders
- `TenantDbContext` — `Orders` + `OrderLines` DbSets; both configurations applied
- `ServiceCollectionExtensions` — `IOrderRepository → OrderRepository` + `IOrderService → OrderService` (scoped)

**API**
- `POST /api/v1/call-records/{callRecordId}/order` — creates order from cart; 201 on first commit, 200 on repeat (idempotent); 400 if no cart
- `GET /api/v1/call-records/{callRecordId}/order` — get order for a call
- `GET /api/v1/orders/{id}` — get order by id
- `POST /api/v1/orders/{id}/cancel` — cancel order (guards: already shipped)
- `POST /api/v1/orders/{id}/lines/{lineId}/ship` — mark line shipped, apply tracking; refreshes order status
- `POST /api/v1/orders/{id}/lines/{lineId}/deliver` — mark line delivered; refreshes order status
- `POST /api/v1/orders/{id}/lines/{lineId}/cancel` — cancel line (guard: already shipped); refreshes order status

**Database**
- Migration `AddOrders` — creates `orders` and `order_lines` tables with all columns, JSONB defaults, indexes
- Applied to `tenant_tms` ✓

**Verified end-to-end:**

| # | Test | Expected | Result |
|---|------|----------|--------|
| 1 | `POST /call-records/{id}/order` (first commit) | 201, status=confirmed, total=72.94, 1 line | ✓ |
| 2 | `POST /call-records/{id}/order` (repeat) | 200, same order ID | ✓ |
| 3 | `GET /call-records/{id}/order` | 200, order with lines | ✓ |
| 4 | `GET /orders/{id}` | 200, full order + lines | ✓ |
| 5 | `POST /orders/{id}/lines/{lineId}/ship` | 200, order=shipped, line=shipped, tracking applied | ✓ |
| 6 | `POST /orders/{id}/lines/{lineId}/deliver` | 200, order=delivered, line=delivered | ✓ |
| DB: inventory confirmed | `qty_available` 10→8, `qty_reserved` 0 | ✓ |
| DB: orders table | id, status=delivered, total=72.94 | ✓ |
| DB: order_lines | sku=ORDER-001, qty=2, tracking=1Z999AA10123456784 | ✓ |

**Build:** 0 warnings, 0 errors ✓

### Pending for Session 12
- **Gap 5: Subscription lifecycle** — `Subscription` entity; recurring schedule; `ContactConnection.Worker` background service project scaffolded

---

## Session 12

**Date:** 2026-04-22
**Start:** 6:48 AM PDT
**End:** 7:14 AM PDT
**Duration:** 26 minutes

### Accomplished

Gap 5: Subscription lifecycle + ContactConnection.Worker scaffolding — AutoShip enrollment on order commit, full subscription lifecycle management, and a Worker service that processes due subscriptions on a recurring schedule.

**Domain**
- `Order.CallRecordId` made nullable — autoship renewal orders have no originating call record; `CreateFromSubscription` factory added alongside `CreateFromCart`
- `OrderLine.CreateFromSubscription` factory — minimal snapshot for renewal lines
- `Subscription` entity — `Id`, `TenantId`, `CallRecordId?`, `OriginalOrderId`, `OriginalOrderLineId`; product/offer snapshot fields (frozen at enrollment); `IntervalDays`, `NextShipDate`, `LastShipDate`, `ShipmentCount`; `Status` lifecycle; `IsDue()` guard; `RecordShipment()` advances schedule; `Pause()`, `Resume()`, `Cancel()` methods; `CreateFromOrderLine()` factory
- `SubscriptionStatus` static class: `active` | `paused` | `cancelled`

**Application**
- `ISubscriptionRepository` — `GetByIdAsync`, `GetByCallRecordIdAsync`, `GetDueAsync(cutoff)`, `AddAsync`, `AddRangeAsync`, `SaveChangesAsync`
- `ISubscriptionOrderCreator` — `CreateRenewalOrderAsync(subscription)` — called by Worker for due subscriptions

**Infrastructure**
- `SubscriptionConfiguration` — `subscriptions` table; 3 indexes including `ix_subscriptions_due` on `(status, next_ship_date)` for efficient Worker queries
- `SubscriptionRepository` — lazy factory pattern
- `SubscriptionOrderCreator` — loads product, validates stock (`CanAddToCart`), calls `Product.Confirm`, creates `Order.CreateFromSubscription` + `OrderLine.CreateFromSubscription`, saves via `IOrderRepository`
- `OrderService` updated — after order creation, auto-enrolls all `AutoShip = true && IntervalDays > 0` lines into `Subscription` records via `ISubscriptionRepository.AddRangeAsync`
- `TenantDbContext` — `Subscriptions` DbSet + `SubscriptionConfiguration` applied
- `ServiceCollectionExtensions` — `ISubscriptionRepository`, `ISubscriptionOrderCreator` registered

**API**
- `GET /api/v1/call-records/{id}/subscriptions` — list subscriptions for a call
- `GET /api/v1/subscriptions/{id}` — get subscription
- `POST /api/v1/subscriptions/{id}/pause` — pause (guards: cancelled)
- `POST /api/v1/subscriptions/{id}/resume` — resume (guards: cancelled)
- `POST /api/v1/subscriptions/{id}/cancel` — cancel (idempotent)

**ContactConnection.Worker project (scaffolded)**
- `ContactConnection.Worker.csproj` — `Microsoft.NET.Sdk.Worker`; references `ContactConnection.Application` + `ContactConnection.Infrastructure`; added to `ContactConnection.slnx`
- `Program.cs` — `AddInfrastructure` + `AddHostedService<SubscriptionProcessingService>`
- `SubscriptionProcessingService : BackgroundService` — runs every hour; loads all active tenants from `ContactConnectionDbContext`; for each tenant creates a scoped DI scope and pre-populates `TenantContext.Current` so all scoped services (repositories, inventory) route to the correct schema without HTTP middleware; calls `ISubscriptionOrderCreator.CreateRenewalOrderAsync` + `Subscription.RecordShipment()` for each due subscription; per-subscription error isolation (one failure doesn't stop others)

**Database**
- Migration `AddSubscriptions` — creates `subscriptions` table + alters `orders.call_record_id` to nullable
- Applied to `tenant_tms` ✓

**Verified end-to-end:**

| # | Test | Expected | Result |
|---|------|----------|--------|
| 1 | `POST /call-records/{id}/order` with AutoShip cart item | 201 order created | ✓ |
| 2 | `GET /call-records/{id}/subscriptions` | 200, 1 subscription auto-enrolled, status=active, interval=30d | ✓ |
| 3 | `GET /subscriptions/{id}` | 200 | ✓ |
| 4 | `POST /subscriptions/{id}/pause` | 200, status=paused | ✓ |
| 5 | `POST /subscriptions/{id}/resume` | 200, status=active | ✓ |
| 6 | `POST /subscriptions/{id}/cancel` | 200, status=cancelled | ✓ |
| DB: subscriptions | sku=AUTOSHIP-001, interval_days=30, next_ship_date=2026-05-22 | ✓ |
| 7 | `POST /subscriptions/{id}/process-now` | 200, renewal order created (callRecordId=null, status=confirmed), shipmentCount=1, nextShipDate+30d, qty_available decremented | ✓ |
| DB: renewal order | id=7ef1e343, call_record_id=null, status=confirmed | ✓ |
| DB: inventory | qty_available 100→99 after renewal confirm | ✓ |

**Bugs found and fixed during Session 12:**

1. **`Order` ID mismatch** — `Order.CreateFromCart` was calling `Guid.NewGuid()` internally, so the ID passed to `OrderLine.FromCartItem` and the order's actual ID were different. Fixed by adding `Guid id` as the first parameter to `CreateFromCart` and `CreateFromSubscription` so the caller pre-generates and controls the ID.

2. **`RefreshStatus()` not stamping timestamps** — `ShippedAt`/`DeliveredAt` on the `Order` row were null after status transitioned to `shipped`/`delivered`. Fixed by adding `ShippedAt ??= DateTimeOffset.UtcNow` and `DeliveredAt ??= DateTimeOffset.UtcNow` inside the relevant `RefreshStatus()` transitions.

3. **Worker EF Core version conflict** — `Microsoft.Extensions.Hosting 10.0.6` pulled EF Core abstractions `10.0.4`, conflicting with Infrastructure's `10.0.5`. Fixed by pinning `<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.5" />` explicitly in `ContactConnection.Worker.csproj`.

**Build:** 0 errors ✓

### Pending for Session 13
- **Gap 6: Category/attribute system** — `ProductCategory` (hierarchical); `ProductAttribute` + `ProductAttributeValue`; faceted search on products endpoint

---

## Session 13

**Date:** 2026-04-23
**Start:** 7:26 AM PDT
**End:** 8:22 AM PDT
**Duration:** 56 minutes

### Accomplished

Gap 6: Category/attribute system — hierarchical product categories, typed attribute definitions with discrete values, and faceted search on the products endpoint. This completes the 6-session Commerce Engine gap roadmap.

**Domain**
- `ProductCategory` entity — hierarchical tree via self-referential `ParentId` (null = root); `Name`, `Slug`, `DisplayOrder`, `IsActive`; `Children` navigation (immediate children only); `Create()`, `Rename()`, `Activate()`, `Deactivate()` methods
- `ProductAttribute` entity — attribute definitions per tenant (e.g. "Color", "Scent"); `Name`, `Slug`, `DisplayOrder`, `IsActive`; `Values` navigation; `AddValue()` factory method
- `ProductAttributeValue` entity — discrete allowed values for an attribute (e.g. "Lavender" for "Scent"); `AttributeId` FK; public `Create()` factory
- `Product` updated — two new many-to-many navigation collections (`Categories`, `AttributeValues`); domain methods: `AssignToCategory(category)`, `RemoveFromCategory(categoryId)`, `SetAttributeValue(value)` (one-value-per-attribute enforcement), `RemoveAttributeValue(valueId)`

**Application**
- `IProductCategoryRepository` — `GetRootsAsync`, `GetChildrenAsync`, `GetByIdAsync`, `AddAsync`, `SaveChangesAsync`
- `IProductAttributeRepository` — `GetByIdAsync`, `GetValueByIdAsync`, `GetAllAsync`, `AddAsync`, `AddValueAsync`, `SaveChangesAsync`
- `IProductRepository.SearchAsync` — signature extended with `categoryId?` and `attributeValueIds?` (AND-faceted) parameters

**Infrastructure**
- `ProductCategoryConfiguration` — `product_categories` table; self-referential FK (Restrict); tenant+slug unique index
- `ProductAttributeConfiguration` — `product_attributes` table; `HasMany(Values).WithOne()` cascade delete
- `ProductAttributeValueConfiguration` — `product_attribute_values` table; FK to `product_attributes`
- `ProductConfiguration` — two `UsingEntity` many-to-many join tables: `product_category_map` (product_id, category_id) + `product_attribute_assignments` (product_id, attribute_value_id)
- `ProductCategoryRepository`, `ProductAttributeRepository` — lazy `ScopedTenantDbContextFactory` pattern
- `ProductRepository.SearchAsync` — updated to `Include(Categories)` + `Include(AttributeValues)`; `Where(p.Categories.Any(c => c.Id == categoryId))` for category facet; per-attribute-value AND filter loop

**API**
- `POST /api/v1/categories` — create root or child category
- `GET /api/v1/categories?parentId=x` — list roots (no parentId) or children (with parentId); each node includes its immediate children
- `GET /api/v1/categories/{id}` — get category with children
- `POST /api/v1/attributes` — create attribute definition
- `GET /api/v1/attributes` — list all active attributes with their values
- `GET /api/v1/attributes/{id}` — get attribute with values
- `POST /api/v1/attributes/{id}/values` — add a value to an attribute
- `POST /api/v1/products/{id}/categories/{categoryId}` — assign product to category
- `DELETE /api/v1/products/{id}/categories/{categoryId}` — remove product from category
- `POST /api/v1/products/{id}/attribute-values/{valueId}` — assign attribute value to product
- `DELETE /api/v1/products/{id}/attribute-values/{valueId}` — remove attribute value from product
- `GET /api/v1/products` — updated: accepts `categoryId` + `attributeValueIds` (repeated param); product response now embeds `categories[]` + `attributeValues[]`

**Database**
- Migration `AddCategoriesAndAttributes` — creates `product_categories`, `product_attributes`, `product_attribute_values`, `product_category_map`, `product_attribute_assignments` tables with all columns, FKs, and indexes
- Applied to `tenant_tms` ✓

**Bug found and fixed:**
EF Core 10 + Npgsql 10 throws `DbUpdateConcurrencyException` ("expected 1 row affected, got 0") when adding new `ProductAttributeValue` entities to a tracked parent collection (`_values`) via `List.Add()`. Root cause: EF Core 10 change detection doesn't properly snapshot collection additions through field-backed `IReadOnlyList<T>` navigations in this context. Fix: bypass collection tracking entirely — create the value via `ProductAttributeValue.Create()` and add it directly to the DbSet via `IProductAttributeRepository.AddValueAsync()`.

**Verified end-to-end:**

| # | Test | Expected | Result |
|---|------|----------|--------|
| 1 | `POST /categories` (root: Health & Beauty) | 201, parentId=null | ✓ |
| 2 | `POST /categories` (child: Skincare, parentId=root) | 201 | ✓ |
| 3 | `GET /categories` | 200, 1 root with Skincare as child | ✓ |
| 4 | `POST /attributes` (Scent) | 201 | ✓ |
| 5 | `POST /attributes/{id}/values` (Lavender, Citrus) | 201 each | ✓ |
| 6 | `GET /attributes/{id}` | 200, both values embedded | ✓ |
| 7 | `POST /products/{id}/categories/{categoryId}` | 200, product has category | ✓ |
| 8 | `POST /products/{id}/attribute-values/{valueId}` | 200, product has attribute value | ✓ |
| 9 | `GET /products?categoryId=x` | 200, only product in Skincare | ✓ |
| 10 | `GET /products?attributeValueIds=x` | 200, only product with Lavender | ✓ |
| 11 | `GET /products?categoryId=x&attributeValueIds=x` | 200, AND filter — same product | ✓ |
| DB: 5 new tables | categories(2), attributes(1), values(2), cat_map(1), attr_assignments(1) | ✓ |

**Build:** 0 warnings, 0 errors ✓

### Commerce Engine Gap Roadmap — COMPLETE

All 6 gaps from Session 7's analysis are now closed:

| Session | Gap | Status |
|---|---|---|
| 8 | Product/Offer separation | ✓ |
| 9 | Tax service integration | ✓ |
| 10 | Inventory reservation | ✓ |
| 11 | Order entity | ✓ |
| 12 | Subscription lifecycle + ContactConnection.Worker | ✓ |
| 13 | Category/attribute system | ✓ |

---

## Session 14 — Test Projects

**Date:** 2026-04-23
**Start:** 10:19 AM PDT
**End:** 10:36 AM PDT
**Duration:** 17 min
**Cumulative Total:** ~637 min

### Goal

Create `ContactConnection.Domain.Tests` and `ContactConnection.Application.Tests` — the first two test projects per ARCHITECTURE.md spec. Cover domain entity lifecycle logic and PricingService business logic with xUnit tests.

### What Was Built

**`tests/ContactConnection.Domain.Tests/`**
- `ContactConnection.Domain.Tests.csproj` — net10.0, xUnit 2.9.3, references ContactConnection.Domain
- `Domain/ProductInventoryTests.cs` — 13 tests: `CanAddToCart` for all 4 InventoryStatus values; `Reserve`, `Release`, `Confirm` behavior with/without `DecrementOnOrder`
- `Domain/OrderLineLifecycleTests.cs` — 6 tests: `FromCartItem` snapshot, `Ship`, `MarkDelivered`, `Cancel` state transitions; `CreateFromSubscription` snapshot
- `Domain/OrderLifecycleTests.cs` — 9 tests: `Cancel`; `RefreshStatus` for all status transitions (all-pending, partial-ship, all-shipped, all-delivered, delivered+cancelled, shipped+cancelled, all-cancelled, guard when order already cancelled); `CreateFromSubscription` null CallRecordId
- `Domain/SubscriptionLifecycleTests.cs` — 12 tests: `IsDue` (active+future, active+past via reflection on backing field, paused, cancelled); `RecordShipment` count/date/advance/re-not-due; `Pause`, `Resume`, `Cancel`, initial state

**`tests/ContactConnection.Application.Tests/`**
- `ContactConnection.Application.Tests.csproj` — net10.0, xUnit + Moq, references Domain + Application + Infrastructure
- `Commerce/PricingServiceResolvePaymentsTests.cs` — 8 tests: base schedule fallback; QPB threshold met/missed; QPB best-break-wins; MixMatch group threshold met/missed; MixMatch over QPB priority; MixMatch different-code exclusion
- `Commerce/PricingServiceCalculateTotalsTests.cs` — 12 tests: empty cart; subtotal/shipping; tax on taxable items only; shipping-exempt exclusion; weight-tier override; subtotal-tier override; weight-over-subtotal precedence; multi-payment breakdown; shipping not-split; shipping split; single-payment breakdown; RoundSplit remainder in payment 1

**Solution:** `ContactConnection.slnx` updated with both test project paths

**Test results:** 65/65 passed, 0 failures

### Technical Notes

- `xUnit` 2.x does not add global usings — `using Xunit;` required explicitly in each file despite `<ImplicitUsings>enable</ImplicitUsings>`
- EF Core version mismatch warning (10.0.4 vs 10.0.5) in Application.Tests — harmless; tests don't call EF Core directly; the 10.0.5 assembly from Infrastructure is used at runtime
- Subscription `IsDue()` with a past date requires reflection to set the backing field `<NextShipDate>k__BackingField` since `NextShipDate` has a `private set` — pattern documented inline in `SubscriptionLifecycleTests`
- `PricingService` is in Infrastructure but tested here because it's pure computation (no I/O) and is the core business logic of the commerce engine; Application.Tests therefore references Infrastructure
- `Order`/`OrderLine` tests work because `CreateFromCart` stores the passed-in `List<OrderLine>` by reference — calling `line.Ship()` on the original reference mutates the object in the order's `_lines` list, making `RefreshStatus` see the correct statuses

---

## Session 15 — Custom Field System

**Date:** 2026-04-24
**Start:** 6:51 AM PDT
**End:** 7:12 AM PDT
**Duration:** 21 minutes
**Cumulative Total:** ~658 min

### Goal

Build the custom field type system per ARCHITECTURE.md §20 — typed campaign/client/tenant-scoped fields stored on call records with a denormalized JSONB snapshot for fast display.

### What Was Built

**Domain**
- `CustomFieldDataType` static class — 8 well-known type name constants (`string`, `integer`, `decimal`, `currency`, `boolean`, `date`, `datetime`, `json`) + `All` HashSet for validation
- `DataType` entity — platform-level reference (public schema); maps type names to CLR/PostgreSQL types + aggregation metadata (`IsAggregatable`, `AggregationFunctions`); seeded with all 8 types
- `CustomFieldDefinition` entity — scoped to tenant/client/campaign; `FieldName` (stable machine key), `DisplayLabel` (editable UI label), `DataTypeName` FK to data_types; `ScopeRank` computed property drives resolution (0=campaign, 1=client, 2=tenant); `Create()`, `UpdateLabel()`, `SetDisplayOrder()`, `SetRequired()`, `Activate()`, `Deactivate()`
- `CustomFieldValue` entity — one row per (call_record, definition); 7 typed columns (one populated per row); `GetTypedValue()` returns the active column as `object?` for snapshot serialization; `ClearTypedColumns()` ensures only one column is ever set
- `CallRecord.UpdateCustomFieldsSnapshot(string? json)` — new method; updates `custom_fields` JSONB column + stamps `UpdatedAt`

**Application**
- `IDataTypeRepository` — `GetAllAsync`, `GetByNameAsync`
- `ICustomFieldDefinitionRepository` — `GetByIdAsync`, `GetForContextAsync` (scope-filtered), `GetAllForTenantAsync`, `AddAsync`, `SaveChangesAsync`
- `ICustomFieldValueRepository` — `GetByCallRecordAsync` (with Definition nav), `GetByCallRecordAndDefinitionAsync`, `AddAsync`, `DeleteAsync`, `SaveChangesAsync`
- `ICustomFieldService` + `ResolvedCustomField` record — `GetFieldsForCallAsync` (scope resolution + value pairing), `SetValueAsync` (upsert + snapshot refresh), `DeleteValueAsync` (delete + snapshot refresh)

**Infrastructure**
- `DataTypeConfiguration` — `public.data_types` table; stable well-known seed IDs (10000000-…-0001 through -0008); `AggregationFunctions` JSONB with value converter
- `CustomFieldDefinitionConfiguration` — `custom_field_definitions` table; 3 indexes including unique `(tenant_id, field_name, client_id, campaign_id)`
- `CustomFieldValueConfiguration` — `custom_field_values` table; FK cascade from definitions; unique `(call_record_id, definition_id)` index
- `DataTypeRepository` — reads from `ContactConnectionDbContext` (public schema)
- `CustomFieldDefinitionRepository`, `CustomFieldValueRepository` — lazy `ScopedTenantDbContextFactory` pattern
- `CustomFieldService` — scope resolution (groups by `FieldName`, picks lowest `ScopeRank`); typed value parsing via `ApplyTypedValue`; snapshot rebuild via `RefreshSnapshotAsync` (loads all current values → composes JSON dict → updates call_record)
- `ContactConnectionDbContext` — added `DataTypes` DbSet + `DataTypeConfiguration`
- `TenantDbContext` — added `CustomFieldDefinitions`, `CustomFieldValues` DbSets + configurations
- `ServiceCollectionExtensions` — registered `ICustomFieldDefinitionRepository`, `ICustomFieldValueRepository`, `IDataTypeRepository`, `ICustomFieldService`

**API**
- `GET /api/v1/data-types` — list all 8 data types (used by flow designer type picker)
- `POST /api/v1/custom-field-definitions` — create with scope (clientId/campaignId optional); 400 on unknown data type name
- `GET /api/v1/custom-field-definitions` — all for tenant; optional `?clientId=&campaignId=` for scope-filtered view
- `GET /api/v1/custom-field-definitions/{id}` — get by id
- `PATCH /api/v1/custom-field-definitions/{id}` — update label, displayOrder, isRequired, validationRules, isActive
- `GET /api/v1/call-records/{id}/custom-fields` — scope-resolved definitions + current values for a call
- `PUT /api/v1/call-records/{id}/custom-fields/{definitionId}` — upsert typed value; 400 on format error
- `DELETE /api/v1/call-records/{id}/custom-fields/{definitionId}` — remove value; 204

**Database**
- Migration `AddDataTypes` (ContactConnectionDbContext) — creates `public.data_types` with all 8 seed rows ✓
- Migration `AddCustomFields` (TenantDbContext) — creates `custom_field_definitions` + `custom_field_values` with all indexes ✓
- Both applied to target databases ✓

### Verified End-to-End

| # | Test | Expected | Result |
|---|------|----------|--------|
| 1 | `GET /data-types` | 8 types, correct agg metadata | ✓ |
| 2 | `POST /custom-field-definitions` (string) | 201, fieldName normalized to lowercase | ✓ |
| 3 | `POST /custom-field-definitions` (currency, required) | 201, isRequired=true | ✓ |
| 4 | `GET /custom-field-definitions` | 2 results | ✓ |
| 5 | `GET /call-records/{id}/custom-fields` (no values) | 2 definitions, both value=null | ✓ |
| 6 | `PUT .../custom-fields/{def1}` ("SUMMER25") | 200 | ✓ |
| 7 | `PUT .../custom-fields/{def2}` ("29.95") | 200 | ✓ |
| 8 | `GET /call-records/{id}/custom-fields` (values set) | promo_code="SUMMER25", donation_amount=29.95 | ✓ |
| 9 | DB snapshot | `{"promo_code": "SUMMER25", "donation_amount": 29.95}` in call_records.custom_fields | ✓ |
| 10 | `PATCH /custom-field-definitions/{id}` | label updated, isActive=false | ✓ |
| 11 | `DELETE .../custom-fields/{def2}` | 204 | ✓ |
| 12 | DB snapshot after delete | `{"promo_code": "SUMMER25"}` — donation_amount removed | ✓ |
| 13 | Unknown data type name | 400 | ✓ |
| 14 | Bad format value (currency "not-a-number") | 400 | ✓ |

**Build:** 0 warnings, 0 errors ✓

### Key Design Decisions

- `DataType` lives in `ContactConnectionDbContext` (public schema) — platform reference, seeded once; no cross-schema FK from tenant tables (use string `DataTypeName` as the link, validated at domain layer)
- `ScopeRank` on `CustomFieldDefinition` is a computed property (ignored by EF) — drives in-memory scope resolution in `CustomFieldService.ResolveMostSpecific()`
- `ClearTypedColumns()` called before every `Set*()` — ensures only one typed column ever populated per row
- Snapshot refreshed on every `SetValueAsync` and `DeleteValueAsync` — `call_records.custom_fields` always reflects current state without joins on read

---

## Session 16 — Agent UI Scaffold (ContactConnection.Web)

**Date:** 2026-04-24
**Start:** 7:12 AM PDT
**End:** 7:58 AM PDT
**Duration:** 46 minutes
**Cumulative Total:** ~704 min

### Goal

Strategic direction confirmed: build the full ContactConnection platform — not releasing ContactConnection Flow as a standalone product first. Reporting and dashboards are deferred until all other systems (telephony, commerce, flow, custom fields, chat) are complete.

Scaffold `ContactConnection.Web` — the React-based agent UI — with a 3-panel layout and a live flow panel wired to the flow engine backend. Plan and document the tenant-scoped enterprise chat system (placeholder in UI for now).

### Strategic Decisions Made

- **Full platform build order confirmed** — ContactConnection.Web Agent UI → Flow Designer → Chrome Extension → FreeSWITCH/Telephony → ContactConnection.Integrations → Chat → ContactConnection.HubService → Reporting & Dashboards
- **Chat System architecture added to CLAUDE.md** — tenant-scoped, Slack/Pumble-level features: public/private channels, DMs, threads, @mentions, emoji reactions, presence; 5-table data model; `ChatHub : Hub<IChatHubClient>`; Redis for presence

### What Was Built

**`ContactConnection.Web/` — React + Vite + TypeScript SPA**

Config files:
- `package.json` — Vite 4, React 18, Tailwind CSS v3, React Router v6, Zustand v4, `@microsoft/signalr` v8 (pinned to Node 16-compatible versions; Node 24.15.0 installed in background — upgrade packages in Session 17)
- `vite.config.ts` — proxies `/api` → `localhost:5135` and `/hubs` → `ws://localhost:5135`
- `tailwind.config.js` + `postcss.config.js` — Tailwind v3 PostCSS pipeline
- `tsconfig.json`, `tsconfig.app.json`, `tsconfig.node.json`
- `index.html`

Core source:
- `src/main.tsx` — React 18 `createRoot`, `<StrictMode>`
- `src/index.css` — Tailwind directives (`@tailwind base/components/utilities`)
- `src/App.tsx` — `BrowserRouter` with `/login` and `/agent` routes; `RequireAuth` wrapper redirects to login if no token
- `src/vite-env.d.ts` — Vite client type reference

Types / stores / API:
- `src/types/flow.ts` — `FlowNodeState`, `FlowOption`, `StartSessionRequest`, `AdvanceSessionRequest` (mirrors C# `FlowNodeState`)
- `src/stores/authStore.ts` — Zustand with `persist` middleware; holds `token`, `agentId`, `tenantSubdomain`; `setAuth()` + `clearAuth()`
- `src/api/client.ts` — `apiFetch` wrapper auto-injects `Authorization: Bearer` and `X-Tenant-Subdomain` headers from auth store
- `src/api/auth.ts` — `login()` sends raw `fetch` with tenant header before auth state is populated
- `src/api/flows.ts` — `list()`, `startSession()`, `getSession()`, `advance()`

Pages:
- `src/pages/LoginPage.tsx` — subdomain + email + password form; dark theme; calls `authApi.login()`, sets auth store, redirects to `/agent`
- `src/pages/AgentPage.tsx` — thin wrapper that renders `<AgentShell />`

Layout and panels:
- `src/components/AgentShell.tsx` — 3-panel grid: 240px left softphone | flex center flow | 300px right chat; top bar with "ContactConnection" brand + sign-out button
- `src/components/SoftphonePanel.tsx` — placeholder with phone icon + "Coming soon"
- `src/components/ChatPanel.tsx` — placeholder with chat icon + "Channels, DMs & threads / Coming soon"

Flow components (live, wired to backend):
- `src/components/FlowPanel.tsx` — fetches flow list on mount; flow picker dropdown + Start button; `HubConnectionBuilder` with auto-reconnect; `JoinSession`/`LeaveSession` on session change; `advance()` calls `POST /flow-sessions/{id}/advance`; state machine: `idle → loading → running → error`
- `src/components/NodeDisplay.tsx` — renders all node types: `script` (content + Continue), `input` (text/select/checkbox/date/phone inputs + Next), `end` (green check + terminal message); `branch`/`set_variable`/`api_call` all show Continue button

**Build verified:** `npm run build` — 85 modules, 0 errors, 4.13s ✓

### Infrastructure Note

Node.js v16.17.0 was the active version when the session started — incompatible with Vite 6 / Tailwind v4's native oxide bindings. Packages were pinned to Node 16-compatible versions. Node.js 24.15.0 (LTS) was installed via `winget install OpenJS.NodeJS.LTS` in background during the session. Session 17 will open with a fresh terminal on Node 24 and upgrade packages to latest versions.

### Pending for Session 17
- Open fresh terminal, verify `node --version` shows 24.x
- Upgrade `package.json` to latest Vite 6, Tailwind v4, React Router v7, React 19, Zustand v5
- `npm install` + `npm run build` on the upgraded packages
- Begin Flow Designer (visual no-code canvas, React Flow)

---

## Session 17

**Date:** 2026-04-26
**Start:** 6:45 AM PDT
**End:** 6:58 AM PDT
**Duration:** 13 minutes
**Cumulative Total:** ~717 min

### Goal

Upgrade `ContactConnection.Web` packages to current versions (Node 24 now active) and build the Flow Designer — the visual no-code canvas built on React Flow.

### Accomplished

**Package upgrades — ContactConnection.Web**
- Node 24.15.0 confirmed active (installed via winget in Session 16 background)
- Vite 4 → 6.4.2; Tailwind CSS v3 (PostCSS) → v4 (`@tailwindcss/vite` plugin, `@import "tailwindcss"` in CSS, no `tailwind.config.js` or `postcss.config.js` or `autoprefixer`)
- React 18.3 → 19.1; React Router v6 → v7.6; Zustand v4 → v5; TypeScript 5.6 → 5.8
- `@xyflow/react` v12 installed — React Flow canvas library
- Package count: 172 → 125 (Tailwind v4 Vite plugin replaced several PostCSS packages)
- Build: 0 TypeScript errors ✓

**Backend — FlowsEndpoints.cs**
- `PUT /api/v1/flows/{id}` — new endpoint; calls `Flow.UpdateDefinition(definition)`, bumps version, returns detail response
- `GET /api/v1/flows/{id}` — updated to return `definition` field via new `ToDetailResponse()` (previously excluded)
- `UpdateFlowRequest` record added

**Flow Designer — src/types/designer.ts**
- `ContactConnectionNodeType` union type (script, input, branch, set_variable, api_call, end)
- `NodeData` — flat record with all optional fields per node type (compatible with React Flow's generic `Node<T>`)
- `ContactConnectionNodeDef`, `ContactConnectionFlowDefinition` — match the JSON flow schema from ARCHITECTURE.md §9
- `NODE_META` — color, label, description, handle count per type (single/dual/none)
- `defaultNodeData(type)` — factory returning sensible defaults for each type

**Flow Designer — src/api/flows.ts**
- `FlowSummary`, `FlowDetail` interfaces exported
- Added `create`, `getDetail`, `updateDefinition`, `publish` methods for designer use

**Flow Designer — custom node components (src/components/designer/nodes/)**
- `NodeShell.tsx` — shared wrapper: colored header bar, ENTRY badge, target handle (top), source handle(s) (bottom); single/dual/none per type
- Branch/ApiCall dual handles: left handle green (true/success), right handle red (false/error) — visually labeled in node body
- `ScriptNode` (blue #3b82f6) — content preview
- `InputNode` (emerald #10b981) — fieldType + required indicator
- `BranchNode` (amber #f59e0b) — condition expression preview + true/false labels
- `SetVariableNode` (violet #8b5cf6) — assignment count
- `ApiCallNode` (indigo #6366f1) — method + URL preview + success/error labels
- `EndNode` (red #ef4444) — status preview

**Flow Designer — NodePalette.tsx**
- 176px left panel; colored draggable cards per node type; `dataTransfer` sets `application/reactflow-node-type`

**Flow Designer — NodePropertiesPanel.tsx**
- 288px right panel; type-specific form fields:
  - script: content textarea (5 rows), `{{namespace.field}}` hint
  - input: fieldType dropdown (text/select/checkbox/date/phone/email/address), options textarea (select only), required checkbox
  - branch: condition input, operator reference
  - set_variable: dynamic key-value assignment list with add/remove
  - api_call: method dropdown, URL, headers JSON textarea, body JSON textarea
  - end: status input
- "Set as Entry Node" button (hidden when already entry); "Delete Node" button
- Node ID shown for reference

**Flow Designer — FlowDesignerPage.tsx**
- `ReactFlowProvider` outer wrapper; `DesignerCanvas` inner component uses `useReactFlow`
- Drag-and-drop from palette via `onDrop` + `screenToFlowPosition`; first dropped node auto-set as entry
- Edge connections via `onConnect` + `addEdge` (smoothstep type)
- Node click → opens properties panel; pane click → deselects
- Delete key removes selected nodes + connected edges
- `toContactConnectionDef()` — React Flow state → ContactConnection JSON; stores `_pos` in each node for layout persistence
- `fromContactConnectionDef()` — ContactConnection JSON → React Flow state; restores positions from `_pos`
- Save: POST (new flow → redirects to `/designer/{id}`) or PUT (existing flow); status message auto-clears after 3s
- Publish: `POST /flows/{id}/publish`; status message auto-clears after 4s
- Load: fetches `GET /flows/{id}` on mount, parses `definition` JSON, restores full canvas state
- React Flow: `Background` grid, `Controls`, `MiniMap` (color-coded by node type)

**Routing + Navigation**
- `App.tsx` — `/designer` (new flow) and `/designer/:id` (edit existing), both `RequireAuth`-wrapped
- `AgentShell.tsx` — "Flow Designer" link in top bar navigates to `/designer`

**Build:** 0 TypeScript errors, 0 warnings ✓ (253 modules; SignalR annotation warnings from third-party ESM bundle only)

### Pending for Session 18
- Chrome Extension (web automation bridge) — Manifest V3, background service worker, content scripts, frame registry, annotation system

---

## Session 18 — Script Node Rich Text Editor + Branding

**Date:** 2026-04-26
**Start:** 7:01 AM PDT
**End:** 7:59 AM PDT
**Duration:** 58 minutes
**Cumulative Total:** ~775 min

### Goal

Live test the stack, diagnose and fix the input node loop bug, then build the Script node rich text editing experience (TipTap) and place ContactConnection branding assets across the app.

### Accomplished

**Bug fix — input node infinite loop**
- Root cause: TypeScript `AdvanceSessionRequest` used field `input?: string` but the C# record deserialized the property as `inputValue` (camelCase). The backend always received `inputValue: null` → `InputNodeHandler` treated every advance as "no input submitted" → returned the same node. Fixed by renaming the TypeScript field to `inputValue` and updating `FlowPanel.tsx` to send `{ inputValue: input }`.
- Secondary fix: `NodeDisplay.tsx` script content changed from plain text render to `dangerouslySetInnerHTML={{ __html: node.content }}` with `className="script-content"` so HTML-formatted script content renders correctly in the agent UI.

**TipTap rich text editor — RichTextEditor.tsx**
- Installed TipTap packages: `@tiptap/react`, `@tiptap/starter-kit`, `@tiptap/extension-text-style`, `@tiptap/extension-color`, `@tiptap/extension-highlight`, `@tiptap/extension-underline`, `@tiptap/extension-font-family`
- Extensions: StarterKit, TextStyle (named import — default import does not exist), Color, Highlight (multicolor), Underline, FontFamily, custom FontSize (inline TipTap Extension using `addGlobalAttributes` on `textStyle` mark), Image (base64, allowBase64: true)
- Toolbar: font family dropdown (Default/Arial/Georgia/Verdana/Times New Roman/Courier New), font size dropdown (10–28 px), Bold, Italic, Underline, text color picker, highlight color picker, bullet list, numbered list, insert image button, clear formatting, expand button
- Color pickers: swatch grid popover with backdrop dismiss; active color shown as underline bar on button
- Expand button: rendered only when `onExpand` prop is provided; far-right of toolbar
- `index.css`: `.script-editor` rules for TipTap surface; `.script-content` rules for agent UI rendering; image rules for both (`max-width: 100%`, border-radius, selection outline)

**Image support**
- Installed `@tiptap/extension-image`
- Paste: `editorProps.handlePaste` intercepts clipboard events, detects `image/*` MIME, reads as base64 Data URL via FileReader, inserts via ProseMirror transaction
- Insert button: hidden `<input type="file" accept="image/*">` triggered by ref; reads file as base64 Data URL via FileReader, calls `editor.chain().setImage({ src })`
- Images stored as inline base64 in script HTML — no server upload needed

**Popout modal editor**
- `ScriptEditorModal.tsx` — fixed full-viewport overlay (z-50), blurred dark backdrop, 90vw × 85vh white container, scrollable body with full RichTextEditor (no expand button), Done button + backdrop click closes
- `ScriptContentEditor.tsx` — wrapper holding `modalOpen` state; when modal open: compact editor unmounted, dashed placeholder shown so panel keeps shape; when closed: compact editor remounts from latest `content` prop
- `NodePropertiesPanel.tsx` — script case replaced `RichTextEditor` with `<ScriptContentEditor key={node.id} />` (key resets TipTap on node switch)

**ContactConnection branding**
- `Images/hubion-favicon.svg` and `Images/hubion-logo.svg` copied to `ContactConnection.Web/public/`
- `index.html` — `<link rel="icon" href="/hubion-favicon.svg" type="image/svg+xml">` added
- `LoginPage.tsx` — favicon icon (56px, centered) replaces plain "ContactConnection" h1; dark card background makes the blue gradient favicon pop
- `AgentShell.tsx` — favicon icon (24px) + "ContactConnection" white text replaces plain indigo text span in top bar
- `FlowDesignerPage.tsx` — full logo SVG (h-8, 32px tall) added to left of top bar before the Back button; vertical divider separates logo from nav controls; light bar background is the design target for the full wordmark logo

**Build:** 0 TypeScript errors ✓

### Pending for Session 19
- Chrome Extension (web automation bridge) — Manifest V3, background service worker, content scripts, frame registry, annotation system
- Continue working through remaining Flow Designer node types (Input, Branch, Set Variable, API Call, End)

---

## Session 19 — Variable Panel, Input Node Improvements, Flows Management Page

**Date:** 2026-05-03
**Start:** 7:49 AM PDT
**End:** 9:11 AM PDT
**Duration:** 82 minutes
**Cumulative Total:** ~857 min

### Goal

Live test the stack after the Hubion → ContactConnection rebrand, fix a build warning, add a variable reference panel to the Script Content Editor, fix the Input node canvas styling, add per-option exit handles and an output variable field to Input nodes, and build a Flows management page so saved flows can be listed, published, and opened for editing.

### Accomplished

**EF Core version conflict fix (0 warnings, 0 errors)**
- Root cause: Npgsql 10.0.1 transitively pulls EF Core 10.0.4, conflicting with the project's direct references to 10.0.5 — MSB3277 warnings in 4 projects.
- `Directory.Build.props` (solution root) — created; `PackageReference Update` approach documented but does not override transitive packages.
- Fix: added explicit `<PackageReference Include="Microsoft.EntityFrameworkCore.Abstractions" Version="10.0.5" />` and `...Relational...` to `ContactConnection.Worker.csproj` and `ContactConnection.Application.Tests.csproj`.
- Result: 0 warnings, 0 errors across all projects.

**Variable reference panel in Script Content Editor modal**
- `src/components/designer/VariablePanel.tsx` (new) — collapsible namespace tree; click any token to insert it at cursor; `call_record` and `caller` namespaces expanded by default; `input`, `api`, `flow` tagged with amber "dynamic" badge; all 7 flow engine namespaces covered with their full field lists.
- `RichTextEditor.tsx` — wrapped with `forwardRef<RichTextEditorHandle>`; `useImperativeHandle` exposes `insert(text)` → `editor.chain().focus().insertContent(text).run()`; `RichTextEditorHandle` interface exported.
- `ScriptEditorModal.tsx` — two-column layout: editor (flex-1) | variable panel (w-52, bg-gray-50, border-l); modal widened from `max-w-4xl` to `max-w-5xl`; variable panel calls `editorRef.current?.insert(token)` on click.

**Input node black border fix**
- Root cause: custom node type key `'input'` collides with React Flow's built-in `input` node type; React Flow applies `.react-flow__node-input { border: 1px solid #1a192b }` default CSS automatically.
- Fix: added `.react-flow__node-input { border: none !important; ... }` override plus selection/focus outline suppressions in `src/index.css`.

**Per-option exit handles on select Input nodes**
- `src/types/designer.ts` — added `'custom'` to handles union type; input node handles changed from `'single'` to `'custom'`; `outputVariable?: string` added to `NodeData` and `ContactConnectionNodeDef`; `outputVariable: ''` in `defaultNodeData`.
- `InputNode.tsx` — renders one green source Handle per option (evenly spaced by `left` percentage) when `fieldType === 'select'`; single grey handle for all other field types; shows `→ {{flow.varname}}` in emerald text when `outputVariable` is set.
- `NodePropertiesPanel.tsx` — "Output variable" text input field added to input node case; live hint text shows `Available downstream as {{flow.varname}}` when filled.

**Output variable auto-store in flow engine backend**
- `InputNodeHandler.cs` — after storing `ctx.Inputs[nodeId] = agentInput`, reads `outputVariable` from node definition; if non-empty, writes `ctx.FlowVars[outputVar] = agentInput` — eliminates the need for a separate Set Variable node to capture input values.

**Flows management page**
- `src/pages/FlowsPage.tsx` (new) — light-themed list page; header with CC logo, Back to agent, New Flow button; table: Name, Type, Status badge (Published emerald / Draft amber), Version, Updated date, Edit + inline Publish actions.
- `src/api/flows.ts` — added `created_at`/`updated_at` to `FlowSummary`; added `listAll()` → `GET /api/v1/flows?all=true`.
- `ContactConnection.Application/Interfaces/Repositories/IFlowRepository.cs` — added `GetAllByTenantAsync`.
- `ContactConnection.Infrastructure/Repositories/FlowRepository.cs` — implemented `GetAllByTenantAsync` (no `IsActive` filter, ordered by `UpdatedAt` desc).
- `ContactConnection.Api/Endpoints/FlowsEndpoints.cs` — list endpoint accepts `[FromQuery] bool all`; routes to `GetAllByTenantAsync` (drafts + published) or `GetActiveByTenantAsync` (published only).
- `App.tsx` — `/flows` route added with `RequireAuth`.
- `AgentShell.tsx` — top bar link changed from "Flow Designer" → `/designer` to "Flows" → `/flows`.

### Bugs Found and Fixed

1. **Docker volume mismatch after rebrand** — Directory rename changed Docker Compose project name from `hubion` to `contactconnection`; new empty volumes were created. Old `hubion_hubion_pgdata` volumes still exist. Fixed by running `docker compose -p hubion up -d` to reattach to original volumes.
2. **Save showed success but flow not in agent dropdown** — Flows saved as draft; `GET /api/v1/flows` only returns published flows. Not a bug — user needed to click Publish. Documented two-step lifecycle.
3. **No way to open existing flows** — Flow Designer had no load/open UI. Fixed by building `FlowsPage.tsx` with Edit buttons linking to `/designer/:id`.

### Branch Condition Analysis (checkbox values)

Confirmed: `NodeDisplay.tsx` always emits string `"true"` or `"false"` for checkbox inputs. `VariableResolver.EvaluateCondition` uses `StringComparison.OrdinalIgnoreCase` for `==`, so `{{flow.varName}} == true` resolves correctly. The actual bug in the user's test flow was a wrong variable reference (`{{flow.node_id}}` instead of the correct output variable name).

**Build:** 0 warnings, 0 errors ✓

---

## Session 20 — Flow Designer Polish + Database Wipe + Hubion Rename

**Date:** 2026-05-06
**Start:** 4:45 AM CDT
**End:** 5:36 AM CDT
**Duration:** 51 minutes
**Cumulative Total:** ~908 min

### Goal

Complete the input node connector-centering fix from Session 19's context limit, then wipe the TMS test data and rename all Hubion references to ContactConnection.

### Accomplished

**Input node connector centering fix**
- Root cause: `NodeShell` had `overflow-hidden` on the outer div, clipping React Flow handle dots; `InputNode` rendered handles inside the padded body div rather than as direct children of the outer container.
- `NodeShell.tsx` — removed `overflow-hidden`; added `rounded-t-[7px]` to header; added `position: 'relative'` to outer div; added `sourceHandles?: React.ReactNode` prop rendered as a direct child of outer container.
- `InputNode.tsx` — option chips redesigned as a full-width bottom strip (`flex-1` per chip) so chips align with handle positions; handles passed via `sourceHandles` prop at `bottom: 13, left: ((i+0.5)/N)*100%` — handles sit visually inside the chip strip.

**Edge labels**
- `EditableEdge.tsx` — destructures `label` from `EdgeProps`; renders label as white pill at path midpoint via `EdgeLabelRenderer`.
- `FlowDesignerPage.tsx` — sets `label: handle` on edges from select/branch/api_call nodes; labels appear automatically on connecting lines.

**Database wipe and test-tenant provisioning**
- Dropped and recreated `hubion_master` database; re-ran all migrations.
- Inserted `Test Tenant` directly via SQL (subdomain: `test-tenant`, schema: `tenant_test_tenant`).
- Created admin agent: `admin@contactconnection.local` / `Admin123!`; login verified.
- `Tenant.Create()` — added hyphen/space normalization in schema name generation.
- `LoginPage.tsx` — removed `placeholder="tms"` from subdomain input.

**Hubion -> ContactConnection rename (all code files)**
- `docker-compose.yml` — container names `hubion_*` -> `cc_*`; volume names updated; pgAdmin credentials updated to `admin@contactconnection.local` / `cc_dev`.
- `appsettings.json` — JWT Issuer/Audience updated to `contactconnection`/`contactconnection-api`.
- `JwtTokenService.cs` — fallback values updated to match.
- `TenantResolutionMiddleware.cs` — comment updated.
- `SubscriptionProcessingService.cs` — variable `hubionDb` renamed to `platformDb`.
- `package.json` — name: `contactconnection-web`.
- `authStore.ts` — Zustand persist key: `cc-auth`.
- `FlowDesignerPage.tsx` — variable `hubionNodes` renamed to `flowNodes`.
- `Hubion.Api.http` — deleted; replaced with `ContactConnection.Api.http`.
- Docker services restarted with new `cc_*` names.

**Build:** 0 warnings, 0 errors

### Credentials

| Field | Value |
|---|---|
| Tenant subdomain | `test-tenant` |
| Email | `admin@contactconnection.local` |
| Password | `Admin123!` |
| pgAdmin | `admin@contactconnection.local` / `cc_dev` |

---

## Session 21 — Database Wipe + Hubion Rename (continuation)

**Date:** 2026-05-07
**Start:** 5:58 AM CDT
**End:** 6:09 AM CDT
**Duration:** 11 minutes
**Cumulative Total:** ~919 min

### Accomplished

Continuation of Session 20 work — session was still in progress when the user provided timestamps. No additional work items beyond what is logged in Session 20 above.

---

## Session 22 — UI Polish: Branding, Dark Theme, Flow Designer

**Date:** 2026-05-08
**Start:** 4:54 AM CDT
**End:** 5:25 AM CDT
**Duration:** 31 minutes
**Cumulative Total:** ~950 min

### Accomplished

**Session timeout warning modal**
- `POST /api/v1/auth/refresh` — new protected endpoint; reads `sub` claim from valid Bearer token, re-issues a fresh JWT without requiring password.
- `useSessionTimeout.ts` — hook parses JWT `exp`, schedules warning at T−5 min and auto-logout at T=0; countdown state updates every second.
- `SessionTimeoutModal.tsx` — MM:SS countdown display; "Keep me logged in" calls refresh and reschedules timers; "Sign out now" calls logout immediately.
- `AgentShell.tsx` — wired in hook and modal; Sign Out button now routes to `/login` via `handleLogout`.

**Login page branding polish**
- Logo tagline changed from "CALL CENTER PLATFORM" to "Call Center Solutions, LLC" (both light and dark SVG variants); font-size 9.5→13, letter-spacing 3→0.5.
- Tagline resized further: font-size 13→21, baseline y=80→86 so text bottom aligns with the bottom of the front C arc.
- Form field labels recolored to `#38BDF8` (matches "Connection" wordmark blue).
- Subtitle changed from "Agent sign in" → "Sign In"; font-size 18px, solid white.
- Label font size increased from `text-xs` (12px) to 16px (12pt).

**Dark theme — Flows page and Flow Designer**
- `FlowsPage.tsx` — full dark restyle: gray-950 page, gray-900 header/table, dark badges (emerald/amber), sky-400 accent buttons, dark navbar logo.
- `FlowDesignerPage.tsx` — gray-900 top bar, dark flow name input, `colorMode="dark"` on ReactFlow canvas.
- `NodePalette.tsx` — gray-900 sidebar, gray-800 card bodies, gray-700 borders.
- `NodePropertiesPanel.tsx` — gray-900 panel, dark inputs/selects/textareas, sky-500 focus rings.
- `RichTextEditor.tsx` — `dark` prop added; dark toolbar (gray-800, gray-700 controls), dark editor surface via `script-editor-dark` CSS class.
- `ScriptContentEditor.tsx` — forwards `dark` prop; pop-out modal stays white.
- `index.css` — `.script-editor-dark .tiptap` sets gray-800 bg + gray-200 text.

**Canvas node dark theme**
- `NodeShell.tsx` — `bg-white` → `bg-gray-800`, unselected border gray-700, label text gray-100, handle color gray-600.
- All node components — secondary text colors adjusted for dark background (gray-400 content text, gray-500 empty states, green-400/red-400 true/false indicators, emerald-400 variable badges, gray-700 chip strip borders).

**Flow designer navbar logo**
- `cc-navbar-dark.svg` — removed opaque `#0A0F1A` rect (transparent background inherits navbar `bg-gray-900`); divider color updated to gray-600 (`#4B5563`).
- `FlowDesignerPage.tsx` — outer nav div changed to `items-stretch` (no padding); logo is first direct flex child and stretches to fill the full bar height; remaining content sits in a padded inner wrapper, vertically centred.

**Build:** 0 warnings, 0 errors ✓

---

## Session 23 — Email Node for Flow Designer

**Date:** 2026-05-10
**Start:** 1:11 PM CDT
**End:** 1:55 PM CDT
**Duration:** 44 minutes
**Cumulative Total:** ~994 min

### Accomplished

**Email node — backend**
- `IEmailValidationService` (Application) — `ValidateAsync(email, checkARecord, checkMX, checkDisposable)` → `EmailValidationResult` record; nullable bool fields for unchecked validations.
- `EmailValidationService` (Infrastructure) — DnsClient 1.8.0 for MX/A/AAAA record lookup; hardcoded disposable domain blocklist (~80 domains); regex format validation; `isDeliverable` composite = format + MX passes + not disposable.
- `EmailNodeHandler` — display on null input; required guard (re-displays without advancing if blank + required=true); stores flat-key FlowVars: `{outputVar}`, `{outputVar}.isFormatValid`, `.DomainExists`, `.MXExists`, `.isDisposable`, `.isDeliverable`; resolves via existing `{{flow.x.subprop}}` split-on-first-dot pattern with no resolver changes needed.
- `FlowNodeState` — added `Required` bool property; `NodeHandlerBase.BuildState` gains `required = false` default parameter.
- `ServiceCollectionExtensions` — registers `ILookupClient` (singleton, 5s timeout, cache enabled), `IEmailValidationService` (singleton), and `EmailNodeHandler` (scoped INodeHandler).

**Email node — frontend**
- `designer.ts` — added `'email'` to `ContactConnectionNodeType`; added `checkARecord?`, `checkMX?`, `checkDisposable?` to `NodeData` and `ContactConnectionNodeDef`; added email entry to `NODE_META` (cyan `#0891b2`, single handle); added email case to `defaultNodeData()` (MX + disposable checked by default).
- `EmailNode.tsx` — new canvas node; cyan header; shows output variable with `{{flow.x}}` badge; shows active validation checks summary.
- `FlowDesignerPage.tsx` — added `email: EmailNode` to `nodeTypes` map; added cyan to MiniMap color table.
- `NodePalette.tsx` — added `'email'` to `NODE_TYPES` array.
- `NodePropertiesPanel.tsx` — email case: output variable input with sub-property hint, required checkbox, and A/AAAA / MX / disposable checkboxes.
- `NodeDisplay.tsx` — email case: `type="email"` input with cyan submit button; separate `handleEmailSubmit` passes `inputValue` directly (not `|| undefined`) so blank optional emails submit correctly.
- `flow.ts` — added `'email'` to `nodeType` union; added `required?` to `FlowNodeState`; added `'email'` to `inputType` union.

**Build:** 0 warnings, 0 errors (dotnet + Vite) ✓

---

## Session 24 — Inline Script on Input and Email Nodes

**Date:** 2026-05-11
**Start:** 4:55 AM PDT
**End:** 5:06 AM PDT
**Duration:** 11 minutes
**Cumulative Total:** ~1005 min

### Accomplished

- Added `scriptLabel` and `scriptContent` properties to both input and email flow nodes.
- **Backend** — `FlowNodeState` gains `NodeScriptLabel` and `NodeScriptContent` (`string?`, settable); both `InputNodeHandler` and `EmailNodeHandler` read these from the node JSON, resolve variable tags via `IVariableResolver`, and attach them to every returned state via a shared `AttachInlineScript` helper.
- **Frontend** — `designer.ts`: `scriptLabel?` + `scriptContent?` added to `NodeData`, `ContactConnectionNodeDef`, and `defaultNodeData` for input and email; `flow.ts`: `nodeScriptLabel?` + `nodeScriptContent?` added to `FlowNodeState`; `NodePropertiesPanel.tsx`: shared `inlineScriptFields()` helper renders a script label input + `ScriptContentEditor` (dark) above the type-specific fields for both input and email, separated by a divider; `InputNode.tsx` + `EmailNode.tsx`: show a sky-blue "📄 {label}" badge on the canvas card when a script is attached; `NodeDisplay.tsx`: renders Script Label → Script → step label → input field in the agent UI.

**Build:** 0 warnings, 0 errors ✓

---

## Session 25 — Variable Resolution Fixes

**Date:** 2026-05-11
**Start:** 5:06 AM PDT
**End:** 5:24 AM PDT
**Duration:** 18 minutes
**Cumulative Total:** ~1023 min

### Accomplished

**`{{agent.*}}` variable resolution**
- `FlowEngine.StartAsync` now loads the agent record from `IAgentRepository` after building the session context and populates `ctx.Agent` with `id`, `first_name`, `last_name`, `full_name`, and `email`. Previously only `id` was set, so `{{agent.first_name}}` always resolved to blank.
- `IAgentRepository` injected into `FlowEngine` constructor.

**`SetVariableNodeHandler` — namespace-aware assignment**
- Handler now strips `{{...}}` wrappers from variable names (so `{{caller.first_name}}` works as the target) and dispatches to the correct context dictionary by namespace prefix: `caller` → `ctx.Caller`, `agent` → `ctx.Agent`, `tenant` → `ctx.Tenant`, `flow` → `ctx.FlowVars`. Previously all assignments wrote to `ctx.FlowVars` with the literal `{{caller.first_name}}` as the key, so downstream `{{caller.*}}` tags never resolved.

**Bug fix — missing using directive**
- `FlowEngine.cs` was missing `using ContactConnection.Domain.Entities` after a prior edit removed it; caused `FlowSession` type not found at build time.

**Build:** 0 warnings, 0 errors ✓

---

## Session 26 — Input Mask Engine, Flow-Aware Variable Panels, Navbar Logo Fixes

**Date:** 2026-05-14
**Start:** 4:29 PM CDT
**End:** 6:03 PM CDT
**Duration:** 94 minutes
**Cumulative Total:** ~1117 min

### Accomplished

**SVG logo centering (all 4 assets)**
- `cc-logo-dark.svg` — viewBox trimmed from `0 0 580 100` → `0 0 460 100`; opaque background rect removed (transparent, inherits parent background); right-side excess whitespace eliminated so logo appears visually centered.
- `cc-logo-light.svg` — viewBox trimmed from `0 0 580 100` → `0 0 460 100`.
- `cc-navbar-dark.svg` — viewBox trimmed from `0 0 380 56` → `0 0 230 56`; width updated to 230.
- `cc-navbar-light.svg` — viewBox trimmed from `0 0 380 56` → `0 0 230 56`; width updated to 230.

**Auto-focus + CRMPro-style glow effect**
- `NodeDisplay.tsx` — `focusRef = useRef<HTMLElement | null>(null)` + `useEffect([node])` auto-focuses the primary input field whenever the displayed node changes; `setLocalError(null)` also fires on node change to clear stale validation.
- `index.css` — `.input-focus-glow` (indigo) and `.input-focus-glow-cyan` (cyan) CSS classes: 6-layer `box-shadow` with linearly decreasing alpha values, emulating the feathered outer glow from CRMPro's `OnPaintBackground` (`ControlClasses.vb` lines 111–378). Applied to all input and email fields in `NodeDisplay.tsx`.

**Input node — field type pruning + text constraints**
- Removed `email`, `address`, `phone`, and `date` input types from the input node (specialized nodes will be built for these separately).
- Added `minChars`, `maxChars`, and `inputMask` properties to `NodeData`, `ContactConnectionNodeDef`, `defaultNodeData`, and `FlowNodeState`.
- `NodePropertiesPanel.tsx` — input case now shows: mask dropdown (None + 6 presets: Phone, Date, SSN, ZIP+4, Credit Card, Time + Custom option), custom mask text field (shown when Custom selected), min/max character fields (disabled when a mask is active).
- `InputNodeHandler.cs` — `AttachTextConstraints()` static method reads `minChars`, `maxChars`, and `inputMask` / `customMask` from node JSON and populates `FlowNodeState`.

**WinForms-style input mask engine (frontend)**
- `NodeDisplay.tsx` — `MASK_CHARS`, `MASK_REQUIRED`, `maskCharValid()`, `applyMask()`, `isMaskComplete()` implement the WinForms `MaskedTextBox` character set: `0`=digit, `9`=digit/space, `L`=letter, `?`=letter/space, `A`=alphanum, `a`=alphanum/space, `&`=any required, `C`=any optional; all other characters are literals auto-inserted.
- `handleTextChange` pipes raw input through `applyMask` when a mask is set.
- `handleSubmit` validates: if mask set → partial entry blocked regardless of required; empty + required → error; empty + not required → allow advance. If no mask → validates min/max character bounds. Sends `inputValue` (even empty string) so backend can distinguish "submitted blank" from "first display".
- Character count hint renders below the text input when min/max are set and no mask is active.
- `localError` state displays inline red validation messages.

**Flow-aware variable panels**
- `src/utils/flowGraph.ts` (new file) — `computeAncestorVars(nodeId, nodes, edges)`: reverse-BFS from the target node through the edge graph; extracts `input` nodes → `inputs[]`, `email` nodes → `flowVars[]` (outputVar + sub-properties), `set_variable` nodes → `flowVars[]` from assignments, `api_call` nodes → `apis[]`. Returns `FlowAncestorVars` shape.
- `VariablePanel.tsx` — rewritten to accept `flowVars?: FlowAncestorVars` and `dark?: boolean`; when `flowVars` provided, replaces static placeholder sections with real computed tokens; shows "None before this node" when a namespace is empty; full dark theme token set.
- `ScriptEditorModal.tsx` — accepts `nodeId?`, `nodes?`, `edges?`, `entryNodeId?`; computes `flowVars` via `useMemo`; passes to `VariablePanel`.
- `ScriptContentEditor.tsx` — passes `nodeId`, `nodes`, `edges`, `entryNodeId` through to `ScriptEditorModal`.
- `NodePropertiesPanel.tsx` — all `ScriptContentEditor` calls now include flow context props; set_variable case adds a collapsible Variables slide-out panel with `dark` theme + flow-aware tokens; `lastFocused` ref tracks which assignment input was active; `handleVarInsert` splices the clicked token at the cursor position using `selectionStart`/`selectionEnd` + `requestAnimationFrame` to restore focus.
- `FlowDesignerPage.tsx` — `NodePropertiesPanel` now receives `nodes`, `edges`, `entryNodeId`.

**Variable resolution fallback**
- `VariableResolver.cs` — unresolved `{{tag}}` expressions now emit `[not captured]` instead of the raw `{{tag}}` notation. Easier for agents to recognize a missing value at a glance.

**Navbar logo height fix — AgentShell + FlowsPage**
- `AgentShell.tsx` — header changed from `items-center py-2` + `h-7` on the logo to `items-stretch` pattern (logo is a direct flex child, no height constraint, fills full bar height naturally); buttons sit in a padded inner `flex-1` div, matching the `FlowDesignerPage` approach.
- `FlowsPage.tsx` — same `items-stretch` refactor; logo no longer capped at `h-8`; inner content div handles padding and `justify-between` layout.

**Build:** 0 warnings, 0 errors (dotnet + Vite + `tsc --noEmit`) ✓

---

## Session 27 — Phone Node + Object Variables + EditableEdge Fix

**Date:** 2026-05-14
**Start:** 6:07 PM CDT
**End:** 7:03 PM CDT
**Duration:** 56 minutes
**Cumulative Total:** ~1173 min

### Accomplished

**Phone node — full implementation**
- `PhoneNodeHandler.cs` (new) — handles `phone` node type; domestic mask `(000) 000-0000`, international `+099 (000) 000-0000` (3-digit zero-padded country code: 001=+1, 044=+44, 357=+357); validates against full ITU-T E.164 country code set; isTollFree derived from area code set {800,833,844,855,866,877,888}; isMobile=null (carrier lookup pending), doNotCall=false (DNC registry placeholder); stores output as JSON object in `ctx.FlowVars[outputVar]`.
- `ServiceCollectionExtensions.cs` — registered `PhoneNodeHandler` as scoped `INodeHandler`.
- `PhoneNode.tsx` (new) — teal canvas node; shows phone type badge, required/intl indicators, output variable tag, DNC badge.
- `NodePalette.tsx` — bug fix: added `'phone'` to `NODE_TYPES` array (was missing; `NODE_META` had it but array did not, so it never appeared in the palette).
- `FlowDesignerPage.tsx` — added `phone: PhoneNode` to `nodeTypes` map and `phone: '#0d9488'` to MiniMap color table.
- `NodePropertiesPanel.tsx` — `case 'phone'`: Output Variable, Required checkbox, Allow International checkbox (with mask hint), DNC Registry Check (disabled, "coming soon"), sub-property list note.
- `NodeDisplay.tsx` — phone node form: teal-glowing masked input, `node.validationError` display, teal "Validating…"/"Next" button; reuses existing mask engine.
- `index.css` — `.input-focus-glow-teal` CSS class added (6-layer feathered teal glow).
- `designer.ts` — added `'phone'` to `ContactConnectionNodeType`, `allowInternational?` and `dncCheck?` to `NodeData` + `ContactConnectionNodeDef`, `NODE_META` entry, `defaultNodeData` case.
- `flow.ts` — added `'phone'` to `nodeType` union.

**Object variable storage for email + phone nodes**
- `EmailNodeHandler.cs` — replaced 6 flat `ctx.FlowVars[$"{outputVar}.xxx"]` assignments with a single `BuildEmailObject` helper that serializes the full email result as a JSON string into `ctx.FlowVars[outputVar]`.
- `VariableResolver.cs` — added `ResolveFlowVar` static method: direct lookup first, then dot-notation JSON extraction via `JsonNode.Parse`; `{{flow.phone.isTollFree}}` resolves by finding "phone" key, parsing its JSON value, and extracting `["isTollFree"]`. Fully backward-compatible with flat string vars.
- `flowGraph.ts` — `FlowVarToken` interface extended with `isObject?` + `properties?`; email and phone cases use `addObject()` to declare structured output in the variable panel.
- `VariablePanel.tsx` — flow namespace section now renders object vars as a non-clickable group header with `[obj]` badge, and clickable indented sub-properties (e.g. `.isTollFree` → inserts `{{flow.phone.isTollFree}}`).

**EditableEdge waypoint visibility fix**
- `EditableEdge.tsx` — waypoint handles now only render when `selected === true`, fixing the bug where handles were invisible after initial load until `setEdges` was called. Added `zIndex: 10` to handles. Added midpoint hint (semi-transparent indigo dot + "double-click to add curve point" text) when edge is selected but has no waypoints yet.

---

## Session 28 — Address Node + Multi-Select/Copy-Paste + Input Pre-Population

**Date:** 2026-05-16
**Start:** 5:19 AM CDT
**End:** 7:06 AM CDT
**Duration:** 107 minutes
**Cumulative Total:** ~1280 min

### Accomplished

**Address node — full implementation**
- `AddressNodeHandler.cs` (new) — handles `address` node type; parses `AddressSubmission` JSON from frontend; validates required fields; builds output object with 20+ derived properties (isPOBox, isCanada, isMilitary, isOutlyingUS, isAKHI, isForeign, isVerified, fullAddress, formattedAddress1/2, etc.); stored as JSON in `ctx.FlowVars[outputVar]`.
- `IFlowEngine.cs` — added AllowInternational, ShowMiddleInitial, ShowCompany, RequiredFields, FieldScripts, PrefilledAddress to `FlowNodeState`.
- `AddressNode.tsx` (new) — orange canvas node; shows script badge, optional field indicators, output variable tag.
- `NodePalette.tsx` — added address to node type list.
- `FlowDesignerPage.tsx` — registered AddressNode + minimap color.
- `NodePropertiesPanel.tsx` — address case with tabbed field scripts (main + per-field tabs), required field checkboxes, showMiddleInitial/showCompany/allowInternational toggles.
- `NodeDisplay.tsx` — full address form: name row (optional MI), optional company, address line 1 with prefix select, address line 2 with prefix select, ZIP/ZIP+4/city/state row, country select (US+CA only or full ISO list when allowInternational), per-field focus script switching, orange glow styling.
- `flowGraph.ts` — address case with 24 output properties in the variable panel.
- `index.css` — `.input-focus-glow-orange` class added.
- `countries.ts` — 193 ISO 3166-1 alpha-2 entries (US/CA first, rest alphabetical); separate file to avoid content-filter policy on large inline arrays.
- `designer.ts` + `flow.ts` — address node type and field definitions added.
- `ServiceCollectionExtensions.cs` — AddressNodeHandler registered.

**Flow designer enhancements**
- Multi-select: `multiSelectionKeyCode` set to Shift/Meta/Control; shift+click and shift+drag box-select work natively.
- Copy/paste: Ctrl/Cmd+C copies selected nodes + internal edges to clipboard ref; Ctrl/Cmd+V pastes at +40/+40 offset with fresh node/edge IDs.
- Input select node redesign: replaced per-option exit handles with single exit handle + option-picker modal on connect; amber warning badge when options have no wired connection; `data.transition` stores option name, `sourceHandle: null` maps to default handle visually.

**Set_variable dot-notation nested writes**
- `SetVariableNodeHandler.cs` — `flow.obj.prop` assignments now merge into the existing JSON object rather than overwriting the whole variable; supports arbitrary depth (e.g. `flow.billing_address.firstName`).

**Pre-population of input nodes from upstream flow variables**
- `FlowNodeState` — added `DefaultValue` property for scalar pre-fill (input/email/phone).
- `InputNodeHandler.cs` — sets `DefaultValue` from `FlowVars[outputVar]` on first display.
- `EmailNodeHandler.cs` — extracts `.value` from stored email JSON object.
- `PhoneNodeHandler.cs` — extracts `.display_value` from stored phone JSON object.
- `AddressNodeHandler.cs` — `PrefilledAddress` dictionary populated from stored address JSON.
- `NodeDisplay.tsx` — `prevInputNodeId` guard initializes `inputValue` from `defaultValue` on node navigation; address form similarly uses `prefilledAddress`; neither re-initializes on same-node validation re-display.

**Build:** 0 warnings, 0 errors (dotnet + Vite) ✓

---

## Session 29 — Section Node + Execute Flow + Transition to Flow

**Date:** 2026-05-19
**Start:** 4:52 PM CDT
**End:** 6:55 PM CDT
**Duration:** 123 minutes
**Cumulative Total:** ~1403 min

### Accomplished

**Bug fixes**
- `PhoneNodeHandler.cs` — `ExtractPhoneDefault` helper reads `.display_value` from stored phone JSON object for pre-population; previously only `.value` (unformatted digits) was checked.
- `EmailNodeHandler.cs` — plain-string fallback added so pre-population works whether the email variable is stored as a raw string or as a JSON object with a `.value` property.
- `flowGraph.ts` — rewrote ancestor traversal to use `ancestorDistances()` returning `Map<string, number>`; ancestors sorted closest-first (most recent value wins); two-pass registration so email/phone/address object shapes are claimed before set_variable flat entries.

**Section node — full implementation**
- `SectionNode.tsx` (new) — white header with dark gray text "Section"; `bg-gray-800` body matching all other nodes; badges for `allowJumpFromAnywhere` and `clearPreviousValues`.
- `SectionNodeHandler.cs` (new) — marks previous section complete on natural entry; reads locked state from `FlowVars[outputVar]`; updates `ctx.CurrentSectionNodeId/Name/Locked`; returns next node ID (transparent to agent).
- `IFlowEngine.cs` — added `JumpTarget` class; added `JumpToSectionNodeId` to `AdvanceFlowRequest`; added `CurrentSectionName`, `SectionLocked`, `JumpTargets` to `FlowNodeState`.
- `FlowExecutionContext.cs` — added `CallStack` (`List<FlowCallFrame>`), `CurrentSectionNodeId/Name/Locked`, `CompletedSectionNodeIds` (`HashSet<string>`); extended `VariableStore` private record + serialization to persist all section state in Redis.
- `FlowEngine.cs` — jump interception in `AdvanceAsync`; clears `CurrentSectionNodeId/Name/Locked` before jump (prevents erroneously marking incomplete section as complete); marks current section complete when flow reaches terminal node; `AttachSectionInfo` populates `JumpTargets` (visited + `allowJumpFromAnywhere` sections); `ClearSectionVars` helper wipes node outputs when `clearPreviousValues = true`; `SectionNodeHandler` added to `AutoAdvanceTypes`.
- `FlowSessionsEndpoints.cs` — `AdvanceSessionRequest` extended with `JumpToSectionNodeId`; passed through to `AdvanceFlowRequest`.
- `NodePropertiesPanel.tsx` — section case: name field, outputVariable field, allowJumpFromAnywhere checkbox, clearPreviousValues checkbox.
- `NodePalette.tsx` — section added to NODE_TYPES; palette uses dark text when `meta.color === '#ffffff'`.
- `FlowDesignerPage.tsx` — SectionNode registered; minimap color `#d1d5db`.
- `NodeDisplay.tsx` — section bar at top showing `currentSectionName` + LOCKED badge; jump dropdown from `jumpTargets`; locked read-only rendering for input/phone/email/address nodes with Continue button; `onJump` prop.
- `FlowPanel.tsx` — `jump` callback via `flowsApi.advance(sessionId, { jumpToSectionNodeId })`; passes `onJump` to NodeDisplay.
- `designer.ts` + `flow.ts` — section nodeType added; `JumpTarget` interface; section fields on `NodeData`/`FlowNodeState`.
- `flowGraph.ts` — section/execute_flow/transition_to_flow cases: `break` (no output variables).
- `ServiceCollectionExtensions.cs` — `SectionNodeHandler` registered.

**Execute Flow node — full implementation**
- `ExecuteFlowNode.tsx` (new) — sky-blue NodeShell; shows target flow name with `↳` prefix.
- `ExecuteFlowNodeHandler.cs` (new) — loads sub-flow definition from `IFlowRepository`; validates `IsActive`; pushes `FlowCallFrame` (parent definition JSON + return node ID) onto `ctx.CallStack`; swaps `ctx.FlowDefinition` to sub-flow definition; returns sub-flow entry node.
- `FlowEngine.cs` — `AdvanceInternalAsync` call stack unwind: when sub-flow terminates and `ctx.CallStack.Count > 0`, pops frame, restores parent definition + return node, clears `scriptContext`, continues the auto-advance while loop.
- `FlowCallFrame` record added to `FlowExecutionContext.cs`: `(Guid FlowId, string DefinitionJson, string ReturnNodeId)`.
- `FlowDefinition` changed from `{ get; init; }` to `{ get; set; }` to allow definition swaps at runtime.
- `NodePropertiesPanel.tsx` — execute_flow case: flow dropdown populated from `flowsApi.listAll()`; draft warning shown if selected flow is not published.
- `FlowDesignerPage.tsx` — ExecuteFlowNode registered; minimap color `#0369a1`.
- `ServiceCollectionExtensions.cs` — `ExecuteFlowNodeHandler` registered.

**Transition to Flow node — full implementation**
- `TransitionToFlowNode.tsx` (new) — purple NodeShell; shows target flow name with `→` prefix.
- `TransitionToFlowNodeHandler.cs` (new) — loads target flow; validates `IsActive`; swaps `ctx.FlowDefinition` to target definition; returns target flow entry node (no call stack push — one-way handoff).
- `NodePropertiesPanel.tsx` — transition_to_flow case: same flow dropdown as execute_flow; description clarifies no-return behavior.
- `FlowDesignerPage.tsx` — TransitionToFlowNode registered; minimap color `#7e22ce`.
- `ServiceCollectionExtensions.cs` — `TransitionToFlowNodeHandler` registered.

**Build:** 0 warnings, 0 errors (dotnet + Vite) ✓

---

## Session 30 — Flow Designer / Execution Polish + Platform Portal Planning

**Date:** 2026-05-21
**Start:** 4:00 PM CDT
**End:** 5:10 PM CDT
**Duration:** 70 minutes
**Cumulative Total:** ~1473 min

### Accomplished

**Sub-flow section visibility fix**
- `FlowEngine.cs` — `CollectSectionTargets` scans current flow definition AND all parent call stack frames (newest-to-oldest); sections from parent flows visible in jump dropdown while inside a sub-flow.
- `FlowEngine.cs` — Jump handling in `AdvanceAsync` searches current definition first, then call stack frames; unwinds stack via `RemoveRange` to the owning frame before executing the jump.
- `FlowEngine.cs` — `HasAnySections` updated to check current definition + all call stack frames.

**Encountered sections always visible in jump dropdown**
- `FlowExecutionContext.cs` — added `EncounteredSectionNodeIds` (`HashSet<string>`); serialized in `VariableStore` (persists in Redis).
- `SectionNodeHandler.cs` — adds `ctx.CurrentNodeId` to `EncounteredSectionNodeIds` immediately when any section node is processed.
- `FlowEngine.cs` — `CollectSectionTargets` visibility: `isCurrentSection || isCompleted || isEncountered || allowJumpFromAnywhere`; sections passed through but abandoned remain visible; unreached sections stay hidden.

**ZIP — US or Canada dual-mode mask**
- `NodePropertiesPanel.tsx` — added `'ZIP — US or Canada'` preset (`'__zip_ca__'` sentinel).
- `NodeDisplay.tsx` — `zipCaMode()`, `applyZipCaMask()`, `isZipCaComplete()` helpers; auto-switches on first character (digit → US, letter → Canadian `L0L 0L0`); US auto-inserts `-` after 5th digit for ZIP+4; validates all three patterns on submit.

**Address node — US/Canada ZIP + auto-country + provinces**
- `NodeDisplay.tsx` — when `!allowInternational`, single dual-mode ZIP field replaces two-field layout; country auto-derives from `zipCaMode()`; country dropdown `disabled` + `opacity-60`; state resets on country switch.
- `NodeDisplay.tsx` — `CA_PROVINCES` (13 entries); state `<select>` switches between `US_STATES` / `CA_PROVINCES` based on `addrForm.country`; dropdown width `w-36` to fit "— Province —" label.
- Pre-population combines `zip`+`zip4` into single masked field; submit splits `DDDDD-DDDD` back for backend.

**Flow designer — single exit per handle**
- `FlowDesignerPage.tsx` — `normHandle()` helper normalizes `null`/`undefined`/`"default"` to same slot; `onConnect` removes existing edge for same `source + sourceHandle` before adding new one — works for both freshly drawn and saved-then-loaded edges.

**Build:** 0 warnings, 0 errors (dotnet Infrastructure + Vite) ✓

---

## Session 31 — Platform Portal: Tenant Management

**Date:** 2026-05-21
**Start:** 5:15 PM CDT
**End:** 5:48 PM CDT
**Duration:** 33 minutes
**Cumulative Total:** ~1506 min

### Accomplished

Full platform-level portal for tenant management — backend and frontend, end-to-end.

**Domain**
- `PlatformAdmin` entity — `public.platform_admins`; `FirstName`, `LastName`, `Email`, `PasswordHash`, `IsActive`, `CreatedAt`, `LastLoginAt`; `Create()`, `RecordLogin()`, `Activate()`, `Deactivate()`, `UpdatePasswordHash()`, `UpdateProfile()`; no tenant scope.

**Application**
- `IPlatformAdminRepository` — `GetByIdAsync`, `GetByEmailAsync`, `AddAsync`, `SaveChangesAsync`
- `IPlatformTokenService` — `GenerateToken(PlatformAdmin)` — no tenant claims in issued JWT
- `ITenantRepository` — extended with `GetAllAsync` for portal tenant list

**Infrastructure**
- `PlatformAdminConfiguration` — `platform_admins` table; unique index on `email`
- `PlatformAdminRepository` — uses `ContactConnectionDbContext` (public schema)
- `PlatformJwtTokenService` — HS256 JWT; claims: `sub`, `email`, `given_name`, `family_name`, `jti`, `role = "platform_admin"`; no tenant claims
- `ContactConnectionDbContext` — added `PlatformAdmins DbSet` + `PlatformAdminConfiguration`
- `ServiceCollectionExtensions` — registered `IPlatformAdminRepository` + `IPlatformTokenService`
- `TenantRepository` — added `GetAllAsync` (ordered by name)
- Old `HubionDbContextModelSnapshot.cs` removed (conflict with newly generated `ContactConnectionDbContextModelSnapshot.cs`)
- Migration `AddPlatformAdmins` — creates `public.platform_admins` table + unique email index; applied ✓

**API**
- `POST /api/v1/portal/auth/login` — platform admin login; identical 401 for unknown email + wrong password; checks `IsActive`
- `POST /api/v1/portal/auth/bootstrap` — creates first platform admin when none with that email exists; protected against duplicate creation
- `GET /api/v1/portal/tenants` — list all tenants (requires `PlatformAdmin` policy)
- `POST /api/v1/portal/tenants` — provision new tenant
- `GET /api/v1/portal/tenants/{id}` — get tenant detail
- `PATCH /api/v1/portal/tenants/{id}` — update billing contact + custom domain
- `PATCH /api/v1/portal/tenants/{id}/feature-flags` — replace feature flags JSONB
- `POST /api/v1/portal/tenants/{id}/activate` / `/deactivate`
- `Program.cs` — `"PlatformAdmin"` authorization policy (`RequireClaim("role", "platform_admin")`); portal endpoints registered

**Frontend**
- `portalAuthStore.ts` — Zustand store with `cc-portal-auth` localStorage key; separate from agent `cc-auth`
- `api/portal.ts` — typed API client; reads portal token automatically; `TenantFeatureFlags` interface matches actual C# property names (`telephonyNative`, `telephonyByod`, `webAutomation`, `omsBuiltIn`, `shopifyAdapter`, `advancedReporting`, `parallelQueuing`, `apiBuilder`)
- `PortalLoginPage.tsx` — `/portal/login`; dark card; no subdomain field (platform-level auth)
- `PortalShell.tsx` — own header (CC navbar logo + "PLATFORM PORTAL" label); nav tabs; sign-out; no softphone/chat panels
- `TenantListPage.tsx` — `/portal/tenants`; table with name, subdomain, plan, active/inactive badge, created date, manage link
- `ProvisionTenantPage.tsx` — `/portal/tenants/new`; auto-derives subdomain from name; plan tier + timezone selects
- `TenantDetailPage.tsx` — `/portal/tenants/:id`; details editor (billing contact, custom domain); activate/deactivate toggle; feature flag toggles with slide-switch UI; flash status messages
- `App.tsx` — `RequirePortalAuth` guard; `/portal/*` routes (login, tenant list, provision, detail); `/portal` redirects to `/portal/tenants`

**Bug fixes**
- `SectionNode.tsx`, `ExecuteFlowNode.tsx`, `TransitionToFlowNode.tsx` — changed `NodeProps<NodeData>` → `NodeProps & { data: NodeData }` to match the pattern used by all other node components (fixes pre-existing TS type errors)
- `TenantFeatureFlags` interface — corrected property names to match actual C# domain object; previous invented names (`enableTelephony` etc.) caused feature flag saves to silently write all-false to the database

**Build:** 0 warnings, 0 errors (dotnet API + Vite) ✓

### Next Session
- Additional portal features (active sessions view, platform admin management) OR
- Continue down the build order: FreeSWITCH + Telephony

---

## Session 32 — Entra Redirect Loop Fix

**Date:** 2026-05-23
**Start:** 6:30 AM CDT
**End:** 7:17 AM CDT
**Duration:** 47 min
**Cumulative Total:** ~1553 min

### What Was Done

**Azure Entra ID authentication — fixed portal sign-in redirect loop**

Diagnosed and resolved a persistent bug where the portal's Microsoft sign-in redirect flow would always return to `/portal/login` instead of completing authentication and landing on `/portal/tenants`.

**Root cause identified via HAR trace:**
- `handleRedirectPromise()` in `@azure/msal-browser` v5 defaults to `navigateToLoginRequestUrl: true`
- After processing the auth code on the callback page, MSAL internally calls `window.location.assign('/portal/login')` (the page where `loginRedirect()` was initiated) before resolving the promise
- This browser navigation wins over React Router's `navigate()`, meaning auth state is never set and the user is always returned to the login page
- The fix option is not a config setting — it is a **per-call argument** passed directly to `handleRedirectPromise()`

**Fix:** Single option added to the `handleRedirectPromise` call in `PortalAuthCallbackPage.tsx`:
```typescript
const result = await msalInstance.handleRedirectPromise({ navigateToLoginRequestUrl: false })
```

**Cleanup during investigation:**
- Reverted popup-based approach (`loginPopup`) that was attempted as a workaround — back to standard redirect flow
- Removed `MsalProvider` wrapper from portal login route (not needed; singleton used directly)
- Removed unused `MsalProvider` and `msalInstance` imports from `App.tsx`
- `msal.ts` — kept `msalInitialized` export (ensures initialization before `loginRedirect()` stores redirect state)
- `PortalLoginPage.tsx` — restored clean `loginRedirect()` with `await msalInitialized` guard
- `PortalAuthCallbackPage.tsx` — restored redirect flow with the one-line fix

**Azure portal settings confirmed correct:**
- Platform type: Single-page application (SPA)
- Redirect URI: `http://localhost:5173/portal/auth/callback`
- No client secrets (PKCE replaces this for SPAs)
- Access tokens + ID tokens checkboxes: unchecked (correct for PKCE flow)

**Result:** Sign-in with Microsoft → Microsoft login page → callback spinner → `/portal/tenants` ✓

### Next Session
- Additional portal features (active sessions view, platform admin management) OR
- Continue down the build order: FreeSWITCH + Telephony

---

## Session 33 — Portal Auth Simplification + Invite Flow Fixes + Agent Management + MFA Enforcement

**Date:** 2026-05-25
**Sub-session 1:** 7:11 AM CDT – 11:29 AM CDT (258 min)
**Sub-session 2:** 2:53 PM CDT – 3:33 PM CDT (40 min)
**Total Duration:** 298 minutes
**Cumulative Total:** ~1851 min

### What Was Done

#### Sub-session 1 (7:11 AM – 11:29 AM CDT)

**PlatformAdmin DB entity removed — Entra-only portal auth**

The `PlatformAdmin` table, entity, repository, and configuration were deleted entirely. Any Entra-authenticated user can now access the portal — no DB allowlist needed. The Entra login endpoint issues a portal JWT directly from `EntraIdentity` claims without any DB lookup.

- `IPlatformTokenService.GenerateToken` signature changed to accept `EntraIdentity` (not `PlatformAdmin`)
- `EntraIdentity` record updated — added `Oid` field (stable per-user Entra object ID, used as JWT `sub`)
- `EntraIdTokenValidator` — extracts `oid` claim from the Entra ID token
- `PlatformJwtTokenService` — no DB dependency; generates token purely from Entra claims
- `PortalAuthEndpoints` — only `entra-login` remains; bootstrap and add-admin endpoints removed
- `ContactConnectionDbContext` — `PlatformAdmins` DbSet and configuration removed
- Migration `DropPlatformAdmins` applied ✓
- `PortalLoginResponse.AdminId` changed from `Guid` to `string` (Entra OID is a string)

**Portal login splash screen**

Added the same animated splash screen (CC logo, "Call Center Solutions, LLC", credits, fade-out) to `PortalLoginPage.tsx` that was already present on the agent login page.

**Timezone abbreviations**

Created `src/utils/timezones.ts` — shared `TIMEZONES` array with abbreviations in all labels (e.g. "Eastern Time (EST / EDT)"). `OnboardingPage.tsx` and `ProvisionTenantPage.tsx` both import from this shared utility.

**Invite URL fix (App:BaseUrl config)**

Onboarding and admin invite email URLs were hardcoded to `https://contactconnection.cc` (no DNS in local dev). Fixed with `App:BaseUrl` config key:
- `appsettings.json`: `"App": { "BaseUrl": "https://contactconnection.cc" }`
- `appsettings.Development.json`: `"App": { "BaseUrl": "http://localhost:5173" }`
- All invite-sending endpoints use `configuration["App:BaseUrl"]`

**Resend invite — portal tenant list and detail**

- `PortalTenantsEndpoints` — added `POST {id}/resend-invite`: creates a fresh `TenantInvite`, saves, sends email
- `TenantListPage.tsx` — "Resend" button for pending tenants with invite email
- `TenantDetailPage.tsx` — "Resend invite" button in Onboarding section card

**Tenant admin agents management page**

- `AdminAgentsEndpoints.cs` — `/api/v1/admin/agents` (`TenantAdmin` policy): list, reset password, update (role/active toggle)
- `IAgentRepository` / `AgentRepository` — added `GetAllAsync`
- `Program.cs` — added `TenantAdmin` policy (`role = admin | supervisor`); registered endpoints
- `AdminAgentsPage.tsx` — table with role badge, active status toggle, inline password reset row
- `TenantAdminPage.tsx` — Agents card now live (renders as `<Link>`)

**Portal tenant detail — admin users section**

- `PortalTenantsEndpoints` — `GET {id}/agents` and `POST {id}/agents/{agentId}/reset-password`; sets `tenantContext.Current = tenant` manually before calling `IAgentRepository`
- `TenantDetailPage.tsx` — "Admin users" section when `onboardingComplete`; role badge, name, email, status, inline password reset

**Critical bug fix — agents never persisted after onboarding**

`OnboardingEndpoints.Complete` called `agents.AddAsync` but never `agents.SaveChangesAsync`. The agent was tracked in `TenantDbContext` but never written to the DB. Onboarding issued a valid JWT from the in-memory object so the session worked, but after logout login always failed with "invalid credentials." Same bug in `TenantAdminInviteEndpoints.Accept`.

- `OnboardingEndpoints.cs` — added `await agents.SaveChangesAsync(ct)` after `AddAsync`
- `TenantAdminInviteEndpoints.cs` — same fix in `Accept`

**Portal escape hatches for stuck/broken tenants**

- `POST {id}/reset-onboarding` — sets `OnboardingComplete = false`; now also calls `agents.DeleteAllAsync()` and `adminInvites.DeleteByTenantAsync()` so re-onboarding with the same emails works
- "Reset onboarding" button always visible regardless of `onboardingComplete` state (label changes to "Clear agents & invites" when already incomplete)
- `POST {id}/invite-admin` with `{ email }` — creates fresh `TenantAdminInvite`, saves, sends email; "Invite an additional admin" input row in Admin users section
- `Tenant.ResetOnboarding()` domain method added
- `IAgentRepository.DeleteAllAsync()` — EF Core `ExecuteDeleteAsync` bulk delete
- `ITenantAdminInviteRepository.DeleteByTenantAsync(tenantId)` — bulk delete by tenant

#### Sub-session 2 (2:53 PM – 3:29 PM CDT)

**Features step in onboarding wizard**

- `OnboardingPage.tsx` — new "Features" step inserted at step index 5 (before "Additional Admins"); card-style toggle rows for all 4 tenant feature flags (`telephony`, `omsBuiltIn`, `shopifyAdapter`, `tenantChat`); flags state pre-populated from `data.featureFlags` returned by `ValidateToken`; submitted in `CompleteOnboardingRequest.featureFlags`
- `OnboardingEndpoints.cs` — `ValidateToken` response includes `featureFlags = invite.Tenant.FeatureFlags`; `CompleteOnboardingRequest` gains `TenantFeatureFlags? FeatureFlags`; `Complete` handler calls `tenant.UpdateFeatureFlags(request.FeatureFlags)` when provided

**TOTP MFA enforcement — full end-to-end**

Domain / Infrastructure:
- `Agent` entity — `MfaSecret: string?`, `MfaEnabled: bool` (private set); `StoreMfaSecret()`, `EnableMfa()`, `ClearMfa()` domain methods
- `AgentConfiguration` — `mfa_secret` (max 64) + `mfa_enabled` columns mapped
- Migration `AddAgentMfa` applied to `tenant_tms` ✓
- `IMfaService` (Application) — `GenerateSecret()`, `GetOtpAuthUri()`, `Verify()`
- `MfaService` (Infrastructure) — Otp.NET; `Base32Encoding`, `Totp`, `VerificationWindow(previous:1, future:0)`
- `ITokenService.GeneratePreAuthToken` — 5-minute TTL, `role = "mfa_pending"`, same signing key

Backend endpoints:
- `Program.cs` — `MfaPending` authorization policy (`RequireClaim("role", "mfa_pending")`)
- `appsettings.json` — `App:IssuerName = "ContactConnection"` (TOTP issuer shown in authenticator apps)
- `POST /api/v1/auth/login` — branches on `tenant.Settings.MfaRequirement`:
  - `"off"` → full JWT as before
  - `"optional"` + `agent.MfaEnabled` → pre-auth token + `mfaSetupRequired: false`
  - `"on"` + `!agent.MfaEnabled` → pre-auth token + `mfaSetupRequired: true` (force setup)
  - `"on"` + `agent.MfaEnabled` → pre-auth token + `mfaSetupRequired: false`
- `GET /api/v1/auth/mfa/setup` (`MfaPending`) — generates TOTP secret, saves to `agent.MfaSecret` (not yet enabled), returns `{ secret, otpAuthUri }`
- `POST /api/v1/auth/mfa/setup/confirm` (`MfaPending`) — verifies TOTP, calls `agent.EnableMfa()`, issues full JWT
- `POST /api/v1/auth/mfa/verify` (`MfaPending`) — verifies TOTP against existing secret, issues full JWT
- `LoginResponse` — discriminated union: `mfaPending: bool`, `token?`, `preAuthToken?`, `mfaSetupRequired?`

Frontend:
- `auth.ts` — `LoginResponse` is now a TypeScript discriminated union; `mfaSetup()`, `mfaSetupConfirm()`, `mfaVerify()` API calls pass pre-auth token directly (bypass `api` client)
- `LoginPage.tsx` — on `mfaPending: true` navigates to `/mfa/setup` or `/mfa/verify` with `{ preAuthToken, subdomain, email }` in router state
- `MfaSetupPage.tsx` (new) — loads TOTP secret, renders QR code via `qrcode` npm library, shows manual key, submits 6-digit code; on success sets auth and navigates to `/admin` or `/agent`
- `MfaVerifyPage.tsx` (new) — prompts 6-digit code for agents with MFA already enabled; same post-verify flow
- `App.tsx` — `/mfa/setup` and `/mfa/verify` routes added (public, state-driven)

**Build:** 0 warnings, 0 errors (dotnet + Vite) ✓

### Next Session
- End-to-end test of MFA flows (setup + verify)
- Continue build order: FreeSWITCH + Telephony OR additional portal features

---

## Session 34 — Invite Email Subdomain + Tenant Admin Invite

**Date:** 2026-05-27
**Start:** 4:16 PM CDT
**End:** 4:41 PM CDT
**Duration:** 25 minutes
**Cumulative Total:** ~1876 min

### What Was Done

**MFA confirmed working end-to-end**

Full onboarding and login/MFA flow tested with both the initial tenant admin and an additional admin. First login after invite correctly prompts for MFA setup when `MfaRequirement = "on"`.

**Tenant subdomain included in all invite emails**

Neither invite email template included the tenant subdomain, leaving new users with no way to know what to enter in the login form's Tenant subdomain field.

- `TenantInviteEmail.HtmlBody` — added `subdomain` parameter; renders a dark indigo-bordered callout card below the CTA button: "Your sign-in subdomain / {subdomain} / Save this — you'll enter it in the Tenant subdomain field each time you sign in."
- `TenantAdminInviteEmail.HtmlBody` — same callout card added
- All four call sites updated to pass `tenant.Subdomain`:
  - `PortalTenantsEndpoints` — provision + resend invite (×2)
  - `PortalTenantsEndpoints` — invite additional admin
  - `OnboardingEndpoints` — additional admin invite during onboarding

**Invite admin in tenant admin portal agents page**

Tenant admins could view all agents/admins in their workspace but had no way to invite new admins from within the portal — only the platform admin could do it from the tenant management view.

- `AdminAgentsEndpoints.cs` — `POST /api/v1/admin/agents/invite` (`TenantAdmin` policy); creates `TenantAdminInvite`, saves, sends standard admin invite email (with subdomain)
- `adminAgents.ts` — `inviteAdmin(email)` API function added
- `AdminAgentsPage.tsx` — "Invite admin" button in page header; toggles an inline form (email input + Send invite + Cancel); success message shown on send; status flash persists after form closes

**Build:** 0 warnings, 0 errors (Vite) ✓ — dotnet build skipped (watch process holds PDB lock; changes are syntactically clean)

### Next Session
- Agent user creation (not yet built — tenant admins can only view/manage existing agents)
- Continue build order: FreeSWITCH + Telephony
