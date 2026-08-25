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
      warnings. Tier 2 item 1 is now fully closed. **Live click-through closed Session 86
      (2026-08-24):** the one remaining loose end (automated coverage existed, no live click had
      been done) is now verified — hit the real admin "Test" endpoint
      (`POST /api/v1/admin/api-definitions/{id}/endpoints/test`) against a scratch definition
      pointed at `postman-echo.com` with an XML body and an explicit `Content-Type: application/xml`
      header. Vendor echoed back `"content-type": "application/xml; charset=utf-8"` and the raw
      (unparsed) XML body — confirms the fix holds through the real HTTP path, not just the test
      double. Scratch definition deleted after verification.
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
- [x] **Outbound rate limiting / throttling.** — closed Session 86 (2026-08-24). New
      `IOutboundRateLimiter` (Application) / `RedisOutboundRateLimiter` (Infrastructure) —
      Redis-backed (not in-memory like the circuit breaker) because the whole point is a shared
      quota across every API instance, not just one process; fixed-window counter keyed per
      `definitionId` via an atomic `SET NX EX` seed + `INCR` (no Lua script needed, and the key
      can never end up without a TTL even if a caller dies mid-request). Keying per definitionId
      is what makes shared-quota protection actually work: a Portal definition backed by a
      platform-default credential has exactly one `definitionId` shared by every tenant using it,
      so they all draw from the same budget — no separate per-tenant wiring needed. New
      `RateLimitPerMinute` (nullable int) field on `TenantApiDefinition`/`PortalApiDefinition` —
      opt-in, defaults to unlimited, same "no behavior change until someone sets it" convention as
      `IsRetrySafe`. Denied calls throw `RateLimitExceededException`, caught by the same
      catch-all `ApiDefinitionExecutor`/`ApiEndpointTestHelper` already use for every other
      outbound failure — no new node-handler wiring needed, it just surfaces as the flow's normal
      `error` transition. Checked first in `ApiDefinitionExecutor.ExecuteAsync`, before any
      credential lookup or oauth2 token exchange, so a call that's going to be denied doesn't pay
      for that work first. Wired into both flow-engine paths (`ApiCallNodeHandler`,
      `GeneralApiCallNodeHandler`, via a new `RateLimitPerMinute` field on their `CallTarget`
      record) and `FlowSessionsEndpoints`' live address validation/ZIP/autocomplete resolution
      (threaded through the same `resolvedDefinitionId`/`ResolveApiEndpoint` plumbing
      `IsRetrySafe` already uses). The admin/portal "Test" button stays exempt by design, matching
      the existing pattern for the circuit breaker/retry/oauth2 cache — `rateLimiter` is an
      optional parameter on `ApiEndpointTestHelper.RunTestAsync` that the Test button call sites
      never pass, so a manual test click always reflects live config and is never blocked by
      traffic the flow engine generated. Frontend: new "Rate limit (requests/min)" field
      (optional, blank = unlimited) on the Admin/Portal Definition create form and the shared
      `ApiDefinitionDetailContent.tsx` edit form, plus a `{n}/min limit` badge on the detail
      header when set. **Verification:** 9 new tests (`RedisOutboundRateLimiterTests` against a
      real local Redis — unlimited/zero/negative always-allow, under-limit allowed,
      exceeds-limit denied with a correct `RetryAfterSeconds`, distinct definitionIds isolated,
      **10 concurrent callers sharing one definitionId correctly split 4 allowed / 6 denied**,
      window-rollover resets the budget; plus `ApiDefinitionExecutor`/`ApiEndpointTestHelper`
      dispatch tests with a mocked limiter) — `dotnet test` across the whole solution:
      **173/173 passing** (45 Domain, 20 Application, 82 Infrastructure, 26 Api). `dotnet build`
      and `npm run build`/`tsc -b` both clean, 0 errors. **Live-verified** against the running
      local stack: created a scratch Admin API Definition (category `address`, pointed at
      `postman-echo.com`, `rateLimitPerMinute: 2`) with a preferred `address_validation` endpoint,
      then called the real `POST /api/v1/flow-sessions/{id}/validate-address` path three times in
      a row as `admin@contactconnection.local` on `test-tenant` — calls 1 and 2 actually reached
      the vendor (real postman-echo responses came back), call 3 was denied with
      `"Rate limit exceeded (2/min) for this API definition. Retry after 37s."`; confirmed the
      Redis key `ratelimit:{definitionId}:{windowStart}` existed with the expected name. Scratch
      definition and Redis key deleted after verification. Also closed the Tier-2-item-1 loose end
      carried over from Session 85: live-verified the Content-Type fix in `ApiEndpointTestHelper.cs`
      (admin/portal "Test" button) against a real endpoint with a non-JSON (XML) body — see that
      item's note above.
      **Side effect worth recording:** `tenant_test_contact_center` (the second dev tenant schema)
      had drifted 7 migrations behind (last applied Session 82, missing everything through Session
      85's `AddCredentialAuditEntries`) — caught up via the real `POST
      /api/v1/portal/maintenance/migrate-tenants` reconciliation endpoint (Session 82) rather than
      hand-written SQL, so it's now current on every migration, not just this session's.
- [x] **Implement the `hmac` auth type.** — closed Session 86 (2026-08-24). New shared
      `HmacSigner` (Infrastructure.ApiExecution, used by both `ApiDefinitionExecutor` and
      `ApiEndpointTestHelper` so the signing convention is defined once) — computes
      `hex(HMAC(secret, payload))`, or, when the config's `includeTimestamp` is set, the
      Stripe/Svix-style `"t={unixSeconds},v1={hex(HMAC(secret, "{unixSeconds}.{payload}"))}"`
      (self-contained in one header, no second "timestamp" header needs its own config field).
      Supports SHA256/SHA512/SHA1/MD5, defaulting to SHA256 for an unrecognized value.
      **Mid-session correction, on user direction:** the first pass always signed the literal
      outgoing request body — the user pointed out real vendor HMAC schemes often sign a
      *specific subset/rearrangement* of fields (order id, a particular total, a caller field not
      even present in the outgoing body), not just the raw body, so the auth config needed access
      to the same flow-variable resolution as the body/headers/query params. Added an optional
      `payloadTemplate` field to the hmac auth config, using the identical `{{ns.field}}` tag
      syntax as those other templates; blank still signs the actual request body (the original,
      still-correct default for the common case). Resolution happens exactly where the other
      templates are resolved for that call site — **not** inside the low-level executor, which
      does no templating of its own by design:
      - CRM/telephony flow-engine paths (`ApiCallNodeHandler`, `GeneralApiCallNodeHandler`) —
        new `ResolveHmacPayload` helper in each, parses the auth config only for the hmac case,
        resolves `payloadTemplate` via the real `IVariableResolver`/`VariableContext` (so it can
        reference any of the 7 real namespaces — `call_record`, `caller`, `agent`, `tenant`,
        `input`, `api`, `flow` — not just body-adjacent fields), passes the resolved string down
        via a new `HmacPayload` field on `ApiDefinitionExecutionRequest`.
      - `ApiEndpointTestHelper` (admin/portal "Test" button + `FlowSessionsEndpoints`' live
        address validation/ZIP/autocomplete resolution) — resolves `payloadTemplate` via the same
        `SubstituteVars`/`ns`/`data` mechanism already used for that call's body/query/headers.
      - Both callers compute a single final `signaturePayload = resolvedTemplate ?? resolvedBody`
        before applying auth, so `ApplyAuth`/`ApplyAuthAsync`'s hmac case just signs whatever
        string it's handed — no dual-parameter plumbing needed.
      Frontend: new "Signed Payload Template" textarea on the HMAC section of `AuthConfigForm.tsx`
      (shared by Admin + Portal definition forms), with inline hint text explaining the
      blank-signs-the-body default. **Verification:** 14 new tests — `HmacSignerTests` (pure
      signing math: bare-hex vs. timestamped format, all 4 algorithms, unrecognized-algorithm
      fallback, deterministic, different payload/secret ⇒ different signature), plus dispatch
      tests in both `ApiDefinitionExecutorTests` (signs Body when no HmacPayload given, signs
      HmacPayload instead when given, no signature header when the credential is missing, default
      algorithm/header-name when unconfigured) and `ApiEndpointTestHelperTests` (payloadTemplate
      resolved via SubstituteVars and signed instead of the body, credential-missing case) —
      `dotnet test` across the whole solution: **191/191 passing** (45 Domain, 20 Application, 97
      Infrastructure, 29 Api). `dotnet build` and `npm run build`/`tsc -b` both clean, 0 errors.
      **Live-verified** against the running local stack: created a scratch General-category Admin
      API Definition pointed at `postman-echo.com` with hmac auth (`secretKey` → a scratch
      credential set via `PUT /api/v1/admin/credentials/{keyName}`), ran the real admin "Test"
      endpoint twice — first with no `payloadTemplate` (body `"hello-world"`), then with
      `payloadTemplate: "{{test.orderId}}:{{test.total}}"` and test data `orderId=555,
      total=19.99`. Both produced `x-signature` headers that postman-echo echoed back;
      independently recomputed both in Python (`hmac.new(secret, payload,
      hashlib.sha256).hexdigest()`) and confirmed **byte-for-byte matches** — `3e88f488...` for
      the body-signing case, `712a9945...` for the template case (a different signature than the
      body-signing case, confirming the template genuinely overrides the body rather than being
      ignored). Scratch definition and credential deleted after verification.
- [x] **Inbound webhook support.** — closed Session 87 (2026-08-24). Tenant-scoped only (user
      call — Portal/platform-shared webhooks descoped until a real portal-side consumer exists;
      no TFN/telephony domain entities exist yet, so a portal webhook would only ever reach
      "received and stored," never dispatched). New `WebhookEndpoint` entity — 1:1 sidecar to a
      `TenantApiEndpoint` (unique FK), opaque random `Token` used in the public URL, signature
      config (header name, SHA256/512/1/MD5, optional Stripe/Svix-style timestamped format,
      tolerance window). Shared secret deliberately not stored on the entity — lives in the
      existing `ITenantCredentialStore` under the deterministic key `webhook:{Id}`, same as every
      other credential. New `WebhookEvent` — append-only receipt log (raw body as `text`, not
      `jsonb`, to tolerate non-JSON/malformed bodies; `BodyHash` always computed as the dedup key;
      `ProcessingStatus`: received/processed/duplicate/rejected/failed). New
      `HmacSigner.VerifySignatureHeaderValue` — the true mirror of Session 86's
      `ComputeSignatureHeaderValue`, same file/algorithm dispatch, constant-time comparison via
      `CryptographicOperations.FixedTimeEquals`, rejects a stale timestamp outside tolerance
      (replay protection) even with an otherwise-correct signature. New public
      `POST /api/v1/webhooks/{token}` (`AllowAnonymous`) — relies entirely on the **existing**
      `TenantResolutionMiddleware` for tenant identification (host-header subdomain, same as
      every other tenant-scoped request); no middleware changes needed. Core logic factored into
      an internal, unit-testable `WebhookReceiveHandler` (mirrors the `ApiEndpointTestHelper`
      precedent): verify signature → dedup check (`ExistsAsync` on `(WebhookEndpointId,
      BodyHash)`, backed by a real unique index as race defense-in-depth) → parse JSON → evaluate
      the payload mapping → dispatch. **Payload mapping reuses the existing outcome-mapping DSL
      as-is** (user call) — `AddressResponseMappingEvaluator` (the evaluator already backing the
      `ResponseMapping` field's address-validation outcomes/conditions/fieldMappings, and its
      existing `ResponseMappingPanel` admin UI) is domain-agnostic despite the name, so it's
      called directly with zero new field-mapping UI or backend code. Dispatch wired for the one
      sub-type with a real domain sink today — `fulfillment_tracking`: outcome name `"shipped"`
      (requires resolved `orderLineId` + `trackingNumber`) → `OrderLine.Ship(...)`; `"delivered"`
      (requires `orderLineId`) → `OrderLine.MarkDelivered()`; both via new
      `IOrderRepository.GetByLineIdAsync` (a fulfillment webhook identifies a shipment by our
      `OrderLine.Id`, not `Order.Id` — the vendor is expected to have been given it as their
      reference at submit time, no new outbound wiring needed since `{{...}}` body templating
      already supports this). Any other/unrecognized sub-type or outcome is stored/logged only —
      documented gap, no dispatch target exists yet for `tfn_assignment_*`/`campaign_results`.
      Admin config: `GET/POST/PATCH/DELETE .../endpoints/{id}/webhook`,
      `.../webhook/regenerate-secret`, `.../webhook/regenerate-token`,
      `.../webhook/events?take=n` on `AdminApiEndpointsEndpoints`; secret is reveal-once (returned
      only by enable/regenerate-secret, never re-fetchable). Frontend: new `WebhookConfigPanel`
      (mirrors `CredentialAuditPanel`/`VersionHistoryPanel`'s modal shape) wired as a "Webhook"
      button per endpoint row in the shared `ApiDefinitionDetailContent.tsx` — enable/URL/secret-
      reveal/signature config/recent-events log; the six webhook methods are optional on the
      shared `DetailApi` interface (same pattern as `listTtsProviders?`) so the Portal wrapper
      simply omits them and the button doesn't render there. **Verification:** 30 new tests
      (9 `HmacSigner` round-trip/tamper/replay-window cases, 7 `WebhookEndpointRepository`/
      `WebhookEventRepository` cases against EF InMemory, 11 `WebhookReceiveHandler` dispatch
      cases covering signature valid/invalid/null-secret/stale-timestamp, dedup short-circuit,
      non-JSON body, shipped/delivered success, missing-field failure, no-matching-line failure,
      and unrecognized-sub-type stores-only) — `dotnet test` across the whole solution:
      **221/221 passing** (45 Domain, 20 Application, 116 Infrastructure, 40 Api). `dotnet build`
      and `npm run build`/`tsc -b` both clean, 0 errors. **Live-verified** against the running
      local stack: created a scratch Admin API Definition/Endpoint (category `fulfillment`,
      sub-type `fulfillment_tracking`) with a `shipped`/`delivered` outcome mapping, enabled its
      webhook, and drove the real `POST /api/v1/webhooks/{token}` path as `admin@
      contactconnection.local` on `test-tenant` against a scratch `Order`/`OrderLine`: a
      correctly-signed `shipped` payload updated the order line's `fulfillment_status`/
      `tracking_number` in Postgres and logged a `processed` `WebhookEvent`; a `delivered`
      follow-up updated it again; a bad-signature POST was correctly rejected with 401 and logged
      as `rejected`; and a byte-identical redelivery of the `shipped` payload returned 200 without
      creating a second event row or reprocessing (confirmed via direct DB query — event count
      stayed at 2 `processed` rows, not 3). Scratch definition, endpoint, webhook, credential,
      order, order line, and webhook events all deleted after verification.

## Tier 3 — Lower priority / forward-looking

- [x] **Credential expiry tracking/warnings.** — closed Session 88 (2026-08-25). Uses Azure Key
      Vault's own native `SecretProperties.ExpiresOn` — not a new field this app invented. New
      optional `expiresOn` parameter on `IPortalCredentialStore`/`ITenantCredentialStore.SetAsync`
      (defaults to `null` — no behavior change for existing callers, same convention as
      `IsRetrySafe`/`RateLimitPerMinute`); `KeyVaultCredentialStoreBase.SetAsync` now builds a
      `KeyVaultSecret` and sets `Properties.ExpiresOn` before calling `SetSecretAsync`, and
      `ListAsync` reads `props.ExpiresOn` back into a new field on `CredentialSummary`. Passed
      through both Redis-caching decorators (`CachedTenantCredentialStore`/
      `CachedPortalCredentialStore`) untouched — nothing to cache differently, expiry is metadata
      on the same secret. `NullCredentialStore`'s write path still fails with the existing
      clear-error message (Key Vault not configured). **A rotation footgun deliberately guarded
      against:** every `SetAsync` call creates a new Key Vault secret *version*, so an existing
      expiry does **not** automatically carry over when an admin rotates a secret's value without
      re-specifying it — a real way to silently lose the warning right when it matters least. The
      Admin/Portal Credentials "Update" form's Expires On field now prefills from the *current*
      item's `expiresOn` (converted to the date input's `YYYY-MM-DD`) rather than starting blank,
      so leaving it untouched during a routine value rotation keeps tracking the same date; a
      docstring on the interface and inline UI copy both call this out explicitly. `UpsertCredentialRequest`
      gained an optional `ExpiresOn` field; `AdminCredentialsEndpoints`/`PortalCredentialsEndpoints`
      thread it through to `SetAsync` and return it from the list endpoint. Frontend: new shared
      `CredentialExpiryBadge` component (`ContactConnection.Web/src/components/versioning/`) —
      color-coded (neutral / amber "Expiring soon" within 30 days / red "Expired") — as a new
      "Expires" column on both `AdminCredentialsPage.tsx` and `PortalCredentialsPage.tsx`, plus an
      amber summary banner above the table listing which keys are expiring/expired when any are.
      No new backend "expiring" endpoint — the existing list response already carries `expiresOn`,
      so the threshold math is pure client-side date arithmetic against data already being
      fetched, matching the effort level of a Tier 3 item. No notification/email path built — out
      of scope for this item; an admin has to visit the Credentials page to see the warning
      (documented limitation, not a silent gap). **Verification:** 2 new tests
      (`SetAsync_PassesExpiresOnThroughToInner` in both `CachedTenantCredentialStoreTests` and
      `CachedPortalCredentialStoreTests`) plus the 2 existing `SetAsync` verify-call assertions
      updated for the new parameter — `dotnet test` across the whole solution: **223/223 passing**
      (45 Domain, 20 Application, 118 Infrastructure, 40 Api). `dotnet build` and `npm run
      build`/`tsc -b` both clean, 0 errors. **Live-verified** against the running local stack and
      the **real** `contactconnection-kv` Key Vault (not a mock): logged in as
      `admin@contactconnection.local` on `test-tenant`, set a scratch credential with
      `expiresOn` 90 days out via the real `PUT /api/v1/admin/credentials/{keyName}` — `GET
      /api/v1/admin/credentials` echoed the exact date back; set a second scratch credential
      expiring in 10 days to exercise the warning-window math; then rotated the first credential's
      value **without** `expiresOn` and confirmed the new secret version correctly came back with
      `expiresOn: null` (validating the exact footgun the UI prefill guards against), with the
      credential audit trail (Session 85) still recording both `set` calls correctly alongside it.
      Both scratch credentials deleted after. Portal side not independently live-clicked this
      session — Entra ID is SSO-only with no password-based login to script against locally, and
      minting a throwaway JWT (the Session 85 precedent for portal-side verification) would have
      required extracting the real Key Vault signing key via a client-secret credential, which the
      harness's own permission classifier correctly declined as a sensitive-secret extraction; not
      worth working around. Portal coverage instead rests on: the identical
      `KeyVaultCredentialStoreBase.SetAsync`/`ListAsync` code path already live-verified on the
      tenant side (same class, different Key Vault prefix), `PortalCredentialsEndpoints.cs` being
      structurally identical to the tenant-side handler edited in lockstep, and
      `CachedPortalCredentialStoreTests`' own new passthrough test.
- [x] **mTLS / AWS SigV4 auth support** for the small number of vendors that require them. —
      closed Session 89 (2026-08-25). Both new auth types, added to the same auth-config dispatch
      switch as api_key/bearer/basic/oauth2/hmac in `ApiDefinitionExecutor` (flow engine) and
      `ApiEndpointTestHelper` (admin/portal "Test" button + `FlowSessionsEndpoints`' live
      resolution) — but architecturally very different from each other, so built and verified
      separately.
      **AWS SigV4** — new `AwsSigV4Signer` (pure computation, mirrors `HmacSigner`'s shape):
      signs the minimal required header set (`host`, `x-amz-date`, and `x-amz-security-token`
      when a session token credential is configured) per the AWS Signature Version 4 spec —
      canonical request → string to sign → derived signing key (`AWS4"+secret → date → region →
      service → aws4_request` HMAC-SHA256 chain) → `Authorization` header. New `aws_sigv4` auth
      config: `accessKeyIdKey`/`secretAccessKeyKey` (required credential keys),
      `sessionTokenKey` (optional, for temporary STS credentials), `region`, `service`. Unlike
      `hmac`'s `payloadTemplate`, there's no customizable payload subset — SigV4 is a spec-
      mandated algorithm signing the literal outgoing body, not a vendor convention with room for
      an override. **Verification:** 10 pure-signer tests in `AwsSigV4SignerTests` validated
      against the **official published AWS SigV4 test suite** (`aws-sig-v4-test-suite`, fetched
      live from its GitHub/npm mirrors rather than trusting a memorized secret key — an early
      attempt using a half-remembered secret key was caught and corrected this way) — 3 exact
      request/signature vectors (`get-vanilla`, `post-vanilla`, `get-vanilla-query-order-key`,
      the last covering canonical-query-string sorting with duplicate keys and mixed-case
      values) matched byte-for-byte, plus session-token/different-payload/different-secret/
      determinism cases. 6 more dispatch tests across both `ApiDefinitionExecutorTests` and
      `ApiEndpointTestHelperTests` (headers applied, credential-missing fallback, session-token
      header + signed-headers list). Frontend: new "AWS Signature V4" option in
      `AuthConfigForm.tsx` with Access Key ID / Secret Access Key / Region / Service / optional
      Session Token fields. **Live-verified** against the real admin "Test" endpoint hitting
      `postman-echo.com/get` — the vendor echoed back the exact `Authorization`/`X-Amz-Date`
      headers the executor sent, confirming the real HTTP path (not just the pure signer) works.
      **mTLS** — architecturally different from every other auth type here: client-certificate
      identity is a property of the TLS transport itself, not something attachable to an
      individual `HttpRequestMessage` the way a header or query param is. New
      `IMtlsHttpClientProvider`/`MtlsHttpClientProvider` (singleton, `ConcurrentDictionary`
      cached per `(definitionId, certificate content hash)`) resolves a dedicated
      `SocketsHttpHandler`-backed `HttpClient` carrying the configured client certificate — so a
      call doesn't rebuild the TLS handshake machinery every time, and rotating the stored
      certificate naturally produces a fresh client instead of silently reusing a stale one. Both
      `ApiDefinitionExecutor` and `ApiEndpointTestHelper` now select the HTTP client *before*
      sending (peeking at the auth config's `type` first) instead of always using the shared
      `"FlowEngine"` named client — the one real structural change this item required, exactly as
      flagged before starting. New `mtls` auth config: `certKey` (credential holding a
      base64-encoded PKCS#12/.pfx blob — cert + private key bundled as one opaque credential-store
      value, no new secret-storage mechanism), `certPasswordKey` (optional). Certificate loaded
      via `X509CertificateLoader.LoadPkcs12` (the modern non-obsolete API). Falls back to the
      shared client — not a hard failure — when the cert credential is missing or unusable,
      matching every other auth type's "proceed unauthenticated, let the vendor reject it"
      precedent. **Unlike** `tokenCache`/`resilience`/`rateLimiter`, `mtlsProvider` *is* wired
      into the admin/portal "Test" button (not withheld) — without the right client cert an
      mtls-configured vendor rejects the TLS handshake outright, so a Test click would be
      meaningless otherwise. **Verification:** 6 dispatch tests (client selection verified by
      instance reference, not headers) across both test projects. **Live-verified against a real
      external mTLS-only server** — `https://client.badssl.com`, badssl.com's public test fixture
      built exactly for this (documented client cert at `badssl.com/certs/badssl.com-client.p12`,
      password `badssl.com`). A control call with no cert configured got the server's actual
      `400 No required SSL certificate was sent` rejection; the real admin "Test" endpoint with
      the cert credential set returned the server's genuine green "client.badssl.com" success
      page, which only renders after a successful mutual-TLS handshake — full real end-to-end
      proof through Key Vault → `MtlsHttpClientProvider` → `SocketsHttpHandler` → actual TLS
      handshake, not a mock. Frontend: new "Mutual TLS (Client Certificate)" option in
      `AuthConfigForm.tsx`. **Solution-wide: 244/244 tests passing** (45 Domain, 20 Application,
      134 Infrastructure, 45 Api). `dotnet build` and `npm run build`/`tsc -b` both clean, 0
      errors. Scratch definitions/credentials deleted after both live verifications.
- [ ] **Sensitive-field masking for API request/response bodies.** Full vendor responses land
      unmasked in `flow_sessions.variable_store` once written to flow variables (confirmed —
      that's a real persisted JSONB column, not hypothetical). This is the same underlying
      problem as the planned call-recording masking work, just on the API-integration side —
      worth designing as one shared "sensitive field" concept rather than solving it twice.
      Natural to sequence alongside the call-recording/masking telephony nodes already planned.

---

**Tier 1 is fully closed. Tier 2 is fully closed.** Tier 3: 2 of 3 items closed (credential
expiry tracking, mTLS/AWS SigV4 auth) — 244/244 tests passing across 4 test projects
(`Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`, `Api.Tests`).
**Next up (Session 90):** the last Tier 3 item — sensitive-field masking for API
request/response bodies landing in `flow_sessions.variable_store`. Not an urgent production
risk. Worth designing as one shared "sensitive field" concept alongside the planned
call-recording/masking telephony work rather than solving it twice — see that item's own note
above. Worth noting inbound webhooks (Session 87) only wired dispatch for `fulfillment_tracking`
— `tfn_assignment_*`/`campaign_results` webhooks are received and logged but not acted on, since
no TFN/telephony domain entity exists yet to dispatch to; that gap closes naturally once the
FreeSWITCH + Telephony session builds those entities, not as further API-hardening work.
