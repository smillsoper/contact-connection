# API Builder Hardening Checklist

**Created:** Session 83 (2026-08-09), following an honest architecture assessment of the API
Definitions / Endpoints / Preferences / execution system, requested by the user after the
credential + OAuth2 token caching work.

**Scope:** `ContactConnection.Infrastructure/ApiExecution/*`, `ContactConnection.Infrastructure/Credentials/*`,
`ContactConnection.Api/Endpoints/Admin*ApiDefinitions*`, `Admin*ApiEndpoints*`, `Admin*ApiPreferences*`,
`Portal*ApiDefinitions*`, `Portal*ApiEndpoints*`, `ApiEndpointTestHelper`, and the live address
validation/ZIP lookup/autocomplete resolution in `FlowSessionsEndpoints`.

**Verdict at time of writing:** well-designed for a single well-behaved vendor and correctly
solves multi-tenant credential ownership, but not yet enterprise-grade — the Tier 1 items below
are real production risk for a real-time telephony product, not nice-to-haves.

## How to use this file

- Work top-to-bottom by tier unless the user redirects.
- Check an item `[x]` and append `— closed Session N (date): <one-line summary>` once it's
  built, built, and verified to this project's usual standard (clean build, tests where they
  exist, live-verified where practical — not just "compiles").
- If an item is partially addressed or deliberately descoped, say so explicitly rather than
  leaving it ambiguous.
- Update the "Next up" pointer at the bottom after every session that touches this file.

---

## Tier 1 — High priority (real production risk today)

- [x] **Circuit breaker / fast-fail for a down vendor.** — closed Session 84 (2026-08-09): new
      `VendorResilienceExecutor` (Polly-based), circuit state keyed **per API Definition** so one
      dead vendor can't trip the breaker for another vendor sharing the same `HttpClient`. Opens
      after ≥4 calls with ≥50% failures in a 30s window, breaks for 30s, fails fast while open
      instead of paying the full timeout. Wired into `ApiDefinitionExecutor` (flow-engine node
      handlers) and `FlowSessionsEndpoints`' live address validation/ZIP/autocomplete resolution;
      the admin/portal "Test" button intentionally stays outside it (always live). **No automated
      test coverage yet** — see Tier 2.
- [x] **Retry-with-backoff for transient failures.** — closed Session 84 (2026-08-09), same change
      as above. User's call on the real design trap (blind retry risks duplicate order/fulfillment
      submissions on ambiguous failures): **per-endpoint opt-in**, not blanket retry. New
      `IsRetrySafe` flag on `TenantApiEndpoint`/`PortalApiEndpoint` — GET/HEAD/PUT/DELETE are
      always retry-safe by HTTP semantics regardless of the flag; POST/PATCH only retry an
      ambiguous failure (timeout/5xx) when the endpoint's own `IsRetrySafe` is set. A pure
      connection-level failure (request never reached the vendor) always retries regardless of
      method. Surfaced as a checkbox in the endpoint form (POST/PATCH only), with the duplicate-
      risk explanation inline.
- [x] **Draft/versioning for API Definitions & Endpoints.** — closed Session 85 (2026-08-16). Full
      retained version history (every version kept forever, revert by selecting any past version,
      per-version created-by/edited-by) across Flows, TenantApiDefinition/TenantApiEndpoint (admin),
      and PortalApiDefinition/PortalApiEndpoint (portal). Backend from Session 84 unchanged
      (`EntityVersion` table + `IVersionHistoryService` + `ActorResolver`, `GET .../versions` /
      `POST .../versions/{n}/revert` on all 5 entity types). **Frontend added this session:** new
      shared `EntityVersionSummary` type (`ContactConnection.Web/src/api/versioning.ts`) and
      reusable `VersionHistoryPanel` modal component
      (`src/components/versioning/VersionHistoryPanel.tsx`) — newest-first list, "Current" badge on
      the active version, created-by/at, change summary, revert button with confirmation,
      auto-refreshes the list after a revert. Wired into all three surfaces:
      `ApiDefinitionDetailContent.tsx` (shared by Admin + Portal — "History" button on the
      definition header card, "History" link per row in the endpoints table) and
      `FlowDesignerPage.tsx` (top-bar "History" button next to Save/Publish, shown once a flow has
      been saved; revert reloads the canvas from the newly-active definition via a new shared
      `loadFlow()` callback). **Correction during user verification:** the CRM Flow Designer
      (`FlowDesignerPage.tsx`, route `/designer`) and the Telephony Flow Designer
      (`TelephonyDesignerPage.tsx`, route `/telephony-designer`) turned out to be two entirely
      separate page components, not one component branching on flow type as assumed — the initial
      pass only wired the History button into the CRM one, so telephony call flows had no History
      button in the actual UI. Fixed by applying the identical button/panel/`loadFlow()` wiring to
      `TelephonyDesignerPage.tsx`; `npm run build` re-verified clean (0 errors) afterward. New API
      client functions: `listAdminApiDefinitionVersions`/
      `revertAdminApiDefinition`/`listAdminApiEndpointVersions`/`revertAdminApiEndpoint`
      (`adminApiDefinitions.ts`), the Portal equivalents (`portal.ts`), and `flowsApi.listVersions`/
      `flowsApi.revert` (`flows.ts`). **Verification:** `dotnet build` (0 errors) and `npm run
      build` (`tsc -b && vite build`, 0 errors) both clean. Live-verified against the running local
      stack (docker services + API on :5135 + Vite on :5173) by driving the exact HTTP calls the new
      UI makes as `admin@contactconnection.local` on `test-tenant`: for a real Admin API Definition
      (`TMS Reject API`), one of its Endpoints (`Add Reject`), and a real Flow (`Order Offers
      (sub-flow)`) — updated each twice to build history, confirmed `GET .../versions` returned
      newest-first with the correct active flag/actor/timestamp/summary, called
      `POST .../versions/1/revert`, and confirmed the entity's live content rolled back and a new
      "Reverted to version 1" version appeared as active. All three entity types round-tripped
      correctly; test edits were reverted back to their original content afterward. HTTP-level
      verification found the CRM/Telephony designer split (see correction above); user then
      confirmed on-screen in the actual browser UI that History → Revert works for API Definitions,
      Endpoints, CRM flows, and Telephony call flows — full click-through verification complete.
- [x] **Audit trail for credential & definition changes.** — closed Session 85 (2026-08-16). The
      "definition changes" half was already covered by Session 84/85's version history. This
      session closed the remaining "credential" half: new `CredentialAuditEntry` entity — append-
      only, records actor/timestamp/key name/action (`set`/`delete`), deliberately never the
      secret value — with its own `ICredentialAuditService` (keyed `"tenant"`/`"portal"` DI,
      mirroring `IVersionHistoryService`'s split) and `TenantCredentialAuditService`/
      `PortalCredentialAuditService` implementations. Lives in the same schema as the store it
      audits (tenant schema for tenant credentials, public schema for portal credentials) via new
      `credential_audit_entries` tables — migrations `AddCredentialAuditEntries` applied to both
      `TenantDbContext` (on `tenant_test_tenant`) and `ContactConnectionDbContext`. Wired into
      `AdminCredentialsEndpoints`/`PortalCredentialsEndpoints`: `Upsert`/`Delete` now resolve the
      actor via `ActorResolver` and call `audit.RecordAsync(...)` after a successful store write;
      new `GET .../credentials/{keyName}/audit` endpoint lists an individual key's history,
      newest-first. **Frontend:** new read-only `CredentialAuditPanel` component (no revert button
      — unlike `VersionHistoryPanel`, there is nothing to revert to; the secret only ever lives in
      Key Vault) wired as a "History" link per row on both `AdminCredentialsPage.tsx` and
      `PortalCredentialsPage.tsx`. **Verification:** `dotnet build` (0 errors) and `npm run build`
      (0 errors) both clean. Live-verified against the running local stack for both scopes: Admin
      side via real tenant-admin login (`test-tenant`) — Set → Update → Delete on a scratch key
      produced 3 audit rows newest-first (`delete`, `set`, `set`) with correct actor/timestamp, and
      the key correctly disappeared from the live credential list while its audit trail persisted.
      Portal side has no password-based login to test against directly (see correction below), so
      verified via a throwaway HS256 JWT minted locally with the dev `Jwt:SigningKey` (matching
      `PlatformJwtTokenService`'s exact claim shape: `role=platform_admin`, same issuer/audience) —
      Set → Delete round-tripped identically. **Correction found during this pass:** the project's
      own memory notes described portal login as `POST /api/v1/portal/auth/login` with email/
      password (plus a `bootstrap` endpoint) — that's stale. `PortalAuthEndpoints.cs` only exposes
      `POST /api/v1/portal/auth/entra-login` now; the platform migrated to Entra ID SSO-only in an
      earlier session (confirmed by the `RemovePlatformAdminPasswordHash` migration and the absence
      of any `platform_admins` table in the current schema). Memory note corrected.
- [x] **Fix oauth2 "Test Auth" raw token exposure.** — closed Session 84 (2026-08-09): `AuthTestHelper.TestOAuth2`
      now redacts `tokenField` and `refresh_token` (if present) inside `rawResponse` via a
      `JsonNode` rewrite before serializing, and `tokenPreview` is redacted unconditionally
      (previously only truncated for tokens >24 chars — short tokens leaked in full). Structure/
      field names still visible for debugging; values are not. Non-JSON responses still fall
      through unredacted (no reliable way to locate the token in them) — acceptable, matches
      existing "surface as-is" fallback behavior for that edge case. `dotnet build` 0 errors.

## Tier 2 — Medium priority

- [x] **Automated test coverage for the execution/caching/resilience layer.** — closed Session 85
      (2026-08-18). New `tests/ContactConnection.Infrastructure.Tests` project
      (xUnit + Moq + EF Core InMemory), added to `ContactConnection.slnx`; `ContactConnection.
      Infrastructure.csproj` now declares `<InternalsVisibleTo Include="ContactConnection.
      Infrastructure.Tests" />` so tests can construct the `internal` service classes directly.
      **Covered, 33 tests, all passing:**
      - `VendorResilienceExecutor` (circuit breaker + retry, Session 84) — the highest-priority
        target per this item's own note. A scripted `HttpMessageHandler` (no real network) drives
        every branch: success/4xx/5xx with retry allowed vs. not, connection-level
        (`SocketException`-wrapped) vs. generic `HttpRequestException` classification, this-
        attempt's-own-timeout vs. caller-cancellation (`TaskCanceledException` handling — the
        `when (!ct.IsCancellationRequested)` filter), per-attempt request cloning (body + headers
        survive retries, original request instance stays reusable), circuit-opens-after-threshold
        fail-fast behavior, and per-`definitionId` circuit isolation.
      - `TenantVersionHistoryService` / `PortalVersionHistoryService` (Session 84/85) — snapshot/
        deactivate-previous/list-newest-first/get-by-version semantics, revert-records-a-new-
        version-never-rewinds, and no cross-entity leakage, against a real (in-memory)
        `TenantDbContext`/`ContactConnectionDbContext`.
      - `TenantCredentialAuditService` / `PortalCredentialAuditService` (Session 85) — record/list/
        newest-first/scoped-by-key-name, and that a credential's audit trail survives its deletion.
      **Extended same session, 34 more tests, all passing:**
      - `CredentialCacheSupport`, `CachedTenantCredentialStore`, `CachedPortalCredentialStore`,
        `RedisOAuth2TokenCache` — against a **real** local Redis (`cc_redis` via docker-compose;
        see `RedisFixture`) rather than a mock, since `IDatabase` has many version-fragile
        optional-parameter overloads not worth faking. Covers cache-hit-skips-inner,
        cache-miss-populates, Set/Delete-evicts-so-a-rotated-credential-is-never-served-stale,
        the "not found" sentinel, TTL bounding, `GetForTenantAsync`'s explicit-subdomain scoping
        being independent of ambient `TenantContext`, and `ListAsync` intentionally never caching.
      - `ApiDefinitionExecutor` — request building (method/query-merge/body), all four auth
        dispatch branches (api_key header vs. query placement, bearer, basic, oauth2 cache-hit vs.
        token-exchange-then-cache vs. exchange-fails-proceeds-unauthenticated), and
        timeout/unexpected-exception normalization, with `IVendorResilienceExecutor` mocked
        (its own behavior is covered separately above).
      - **A real, previously-unknown bug was found and fixed in the process:** a Content-Type
        header set via a definition/endpoint's `Headers` config silently never applied. Root
        cause: `HttpRequestHeaders.TryAddWithoutValidation("Content-Type", ...)` returns `false`
        and stores nothing — .NET treats Content-Type as a content header, not a request header —
        so the code that later read it back via `httpRequest.Headers.TryGetValues("Content-Type",
        ...)` never found it and silently fell back to `application/json` every time, regardless
        of what was configured. Confirmed against real `HttpRequestHeaders` behavior (a standalone
        repro), then confirmed the identical pattern existed in **both** call sites —
        `ApiDefinitionExecutor.cs` (flow engine calls) and `ApiEndpointTestHelper.cs` (admin/portal
        "Test" button + live address validation/ZIP lookup/autocomplete in `FlowSessionsEndpoints`)
        — since the latter was explicitly ported from the former. Fixed both: the configured
        Content-Type is now captured while building the request and applied directly to the
        `StringContent`, instead of being round-tripped through a header collection that silently
        drops it. `ApiEndpointTestHelper.cs` has no automated test coverage of its own yet (see
        below), so this half of the fix was verified by compilation only (0 `CS` errors) — worth an
        explicit live click-through next time the admin/portal "Test" button is exercised with a
        non-JSON body.
      **Finished same session:** new `tests/ContactConnection.Api.Tests` project (23 tests) closes
      the last gap — `ApiEndpointTestHelper` (`ContactConnection.Api`'s sibling of
      `ApiDefinitionExecutor`, backing the admin/portal "Test" button and `FlowSessionsEndpoints`'
      live address validation/ZIP/autocomplete resolution). `ContactConnection.Api.csproj` now
      also declares `<InternalsVisibleTo Include="ContactConnection.Api.Tests" />`. Mirrors the
      `ApiDefinitionExecutor` test suite's shape (request building, all four auth types including
      oauth2 cache-hit/miss/exchange-failure, resilience-present-vs-absent dispatch, error
      handling) plus this helper's own extras: path/query-param `{{ns.field}}` template
      resolution, `_skipIfEmpty` query handling, malformed-JSON-in-config tolerance, JSON response
      pretty-printing, and — critically — an explicit regression test proving the Content-Type fix
      (found earlier this session) actually took effect here too. `dotnet test` across the whole
      solution: **155/155 passing** (45 Domain, 20 Application, 67 Infrastructure, 23 Api), 0
      warnings. Tier 2 item 1 is now fully closed.
- [x] **Cache-stampede protection for the OAuth2 token cache.** — closed Session 85 (2026-08-18).
      New `IOAuth2TokenCache.GetOrCreateAsync(cacheKey, exchange, ct)` — on a cache miss, only one
      caller (across threads and, via a Redis `LockTakeAsync`/`LockReleaseAsync` distributed lock,
      process instances) actually invokes `exchange`; every other concurrent caller for the same
      key polls briefly for that result instead of also hitting the vendor's token endpoint. If
      the lock holder doesn't finish within the wait budget (stuck, crashed, or just slow),
      waiters fall back to running `exchange` themselves — bounded, never blocks indefinitely.
      `RedisOAuth2TokenCache`'s lock TTL/wait budget/poll interval are constructor parameters
      (production defaults 10s/8s/150ms) so tests can use tiny values to exercise the fallback
      path without actually waiting seconds. `ApiDefinitionExecutor` (flow engine, `IOAuth2TokenCache`
      required) and `ApiEndpointTestHelper` (FlowSessionsEndpoints' live resolution — the only
      caller that ever passes a `tokenCache`; the admin/portal "Test" button still never does, by
      design, so a manual test click is never made to wait on someone else's in-flight exchange)
      both refactored to call it instead of a bare Get-then-Set — `ApiEndpointTestHelper`'s inline
      token-exchange logic was also extracted into a shared `ExchangeTokenAsync` helper in the
      process. **Verification:** 6 new tests in `RedisOAuth2TokenCacheTests` against a real local
      Redis — cache hit skips `exchange` entirely, miss invokes it once and persists the result,
      exchange-returns-null caches nothing, **10 concurrent misses for the same key invoke
      `exchange` exactly once** (the actual stampede scenario), concurrent misses for different
      keys aren't serialized against each other, and a deliberately-stuck external lock holder
      (simulated by taking the lock directly) causes the waiter to correctly fall back after its
      wait budget rather than hanging. Existing `ApiDefinitionExecutorTests`/
      `ApiEndpointTestHelperTests` oauth2 tests updated to mock `GetOrCreateAsync` instead of the
      now-bypassed `GetAsync`/`SetAsync`. `dotnet test` across the whole solution: **161/161
      passing** (45 Domain, 20 Application, 73 Infrastructure, 23 Api).
- [ ] **Outbound rate limiting / throttling.** No protection against a runaway or looping flow
      hammering a vendor. Particularly important for tenants using a shared *platform-default*
      credential — one noisy tenant can exhaust the shared quota for every other tenant on that
      default.
- [ ] **Implement the `hmac` auth type.** Currently a documented no-op in both
      `ApiDefinitionExecutor` and `ApiEndpointTestHelper` — any vendor requiring request signing
      (several payment/shipping carriers do) can't be integrated today.
- [ ] **Inbound webhook support.** Everything today is synchronous request/response. Sub-types
      like `fulfillment_tracking` and `tfn_assignment_*` map naturally to vendor-pushed webhooks
      in the real world; polling-only support is a structural gap, not a config one.

## Tier 3 — Lower priority / forward-looking

- [ ] **Credential expiry tracking/warnings.** Entra (and many vendors') client secrets expire on
      a schedule; nothing here warns an admin before a credential lapses and silently breaks a
      live integration.
- [ ] **mTLS / AWS SigV4 auth support** for the small number of vendors that require them.
- [ ] **Sensitive-field masking for API request/response bodies.** Full vendor responses land
      unmasked in `flow_sessions.variable_store` once written to flow variables (confirmed —
      that's a real persisted JSONB column, not hypothetical). This is the same underlying
      problem as the planned call-recording masking work, just on the API-integration side —
      worth designing as one shared "sensitive field" concept rather than solving it twice.
      Natural to sequence alongside the call-recording/masking telephony nodes already planned.

---

**Tier 1 is fully closed. Tier 2 items 1 and 2 are fully closed too** — 161/161 tests passing
across 4 test projects (`Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`, `Api.Tests`).
**Next up (Session 86):** One loose end first — live-verify the Content-Type fix in
`ApiEndpointTestHelper.cs` (admin/portal "Test" button) against a real endpoint with a non-JSON
body; it has automated test coverage now but no live click-through yet. Then Tier 2 item 3:
outbound rate limiting/throttling — no protection today against a runaway or looping flow
hammering a vendor, particularly important for tenants sharing a platform-default credential
(one noisy tenant can exhaust the shared quota for everyone else on that default). After that:
implement the `hmac` auth type, then inbound webhook support, in that order per the checklist.
