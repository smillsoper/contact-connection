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
- [ ] **Draft/versioning for API Definitions & Endpoints — IN PROGRESS, backend done, frontend
      pending (Session 84, 2026-08-09).** Scope expanded per user direction: not just Definitions/
      Endpoints, and not just draft/publish — full retained version history (every version kept
      forever, revert by selecting any past version, per-version created-by/edited-by) across
      **Flows too**. Built as a generic, reusable subsystem: new `EntityVersion` table (both
      schemas) + `IVersionHistoryService` (keyed `"tenant"`/`"portal"` DI) + `ActorResolver`.
      Wired into all 5 entity types' Create/Update (auto-snapshot) plus new
      `GET .../versions` / `POST .../versions/{n}/revert` endpoints — backend is live and build/
      test-verified. **What's left: the frontend.** No UI yet to browse history or click revert,
      across three surfaces (Flow Designer, Admin API Definition/Endpoint detail, Portal API
      Definition/Endpoint detail — the latter two share one component). Don't mark this `[x]`
      until that UI exists and has been live-verified, not just build-clean.
- [ ] **Audit trail for credential & definition changes — PARTIALLY closed (Session 84,
      2026-08-09).** The version history work above fully covers the "definition changes" half —
      every write to a Definition/Endpoint/Flow now has full audit-quality history (who, when,
      what, nothing ever deleted). **The "credential" half is untouched** — `Set`/`Delete` on
      `IPortalCredentialStore`/`ITenantCredentialStore` still have zero audit trail (no record of
      who changed a secret or when). Same `EntityVersion`/`IVersionHistoryService` mechanism could
      plausibly cover this, but credentials are secrets — the "snapshot" would need to record
      *that* a change happened (actor, timestamp, key name), not the value itself. Needs its own
      pass, not just reusing the entity-snapshot pattern verbatim.
- [x] **Fix oauth2 "Test Auth" raw token exposure.** — closed Session 84 (2026-08-09): `AuthTestHelper.TestOAuth2`
      now redacts `tokenField` and `refresh_token` (if present) inside `rawResponse` via a
      `JsonNode` rewrite before serializing, and `tokenPreview` is redacted unconditionally
      (previously only truncated for tokens >24 chars — short tokens leaked in full). Structure/
      field names still visible for debugging; values are not. Non-JSON responses still fall
      through unredacted (no reliable way to locate the token in them) — acceptable, matches
      existing "surface as-is" fallback behavior for that edge case. `dotnet build` 0 errors.

## Tier 2 — Medium priority

- [ ] **Automated test coverage for the execution/caching/resilience layer.** `ApiDefinitionExecutor`,
      `ApiEndpointTestHelper`, `CachedTenantCredentialStore`/`CachedPortalCredentialStore`,
      `RedisOAuth2TokenCache`, `VendorResilienceExecutor` (circuit breaker + retry, Session 84),
      and `TenantVersionHistoryService`/`PortalVersionHistoryService` (Session 84) all currently
      have zero test coverage — needs a `ContactConnection.Infrastructure.Tests` and/or
      `ContactConnection.Api.Tests` project (neither exists yet; only Domain.Tests and
      Application.Tests do). More overdue now than when this item was first written — the retry/
      circuit-breaker logic in particular has real edge-case branches (connection-level vs.
      ambiguous failure classification, request cloning) that deserve real test coverage, not
      just code review.
- [ ] **Cache-stampede protection for the OAuth2 token cache.** Concurrent cache misses on the
      same credentials currently trigger N simultaneous token exchanges instead of 1 (Session 83
      caching work). Not a correctness bug, but wasteful under load — add a distributed lock or
      single-flight pattern around the exchange.
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

**Next up (Session 85):** Finish Tier 1 item 3 — build the version history frontend. Backend is
fully live: `GET/POST .../versions[/{n}/revert]` on `/api/v1/flows`,
`/api/v1/admin/api-definitions[/{id}/endpoints]`, `/api/v1/portal/api-definitions[/{id}/endpoints]`.
Needs: a reusable version-history panel/modal (list newest-first, active badge, created-by/at,
change summary, revert button + confirmation), wired into `FlowDesignerPage.tsx` and
`ApiDefinitionDetailContent.tsx` (shared by Admin + Portal, so one wiring covers both scopes' Definitions
and Endpoints). Once live-verified, mark item 3 `[x]` — and consider whether to also close the
credential-audit half of item 4 in the same pass. After Tier 1 fully closes, move to Tier 2,
starting with automated test coverage (now more overdue given Session 84's resilience/versioning code).
