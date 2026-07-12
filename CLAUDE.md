# Claude Code — Session Memory

**Read ARCHITECTURE.md.** It is the authoritative reference for all development decisions. Every session should start by reading it.

DevLog.md needs to be updated every session with the session start date and time, session end date and time, session duration, total duration, and what was done for the session.  sessions 1 and 2 were logged without timestamps. These are REQUIRED GOING FORWARD. Ask for the current date and time for logging. Don't assume times or durations. always ask for timestamps and calculate durations.

---

## Current Project Status

**As of:** 2026-05-25 (Session 33 complete)

### Solution Structure

```
ContactConnection.slnx
├── ContactConnection.Domain          ← Core domain models, value objects, enums. No dependencies.
├── ContactConnection.Application     ← Business logic, use cases, interfaces. Depends on Domain only.
├── ContactConnection.Infrastructure  ← EF Core + Npgsql, repositories, services. Depends on Application + Domain.
├── ContactConnection.Api             ← ASP.NET Core Web API. Depends on Application + Infrastructure.
└── ContactConnection.Worker          ← .NET Worker Service. BackgroundService jobs. Depends on Application + Infrastructure.
```

Clean Architecture dependency chain: `Domain ← Application ← Infrastructure ← Api`
Target framework: **net10.0** across all projects.
Build status: **0 warnings, 0 errors.**

---

### What Is Done

**Infrastructure / Docker**
- `docker-compose.yml` — full local dev stack running:
  - PostgreSQL 16 on `5432` (healthy)
  - Redis 7 on `6379` (healthy)
  - Nginx on `80` — wildcard subdomain proxy to API on host port `5135`
  - pgAdmin 4 on `5050` — `admin@hubion.dev` / `hubion_dev`
  - MailHog on `8025` — SMTP trap on `1025`
  - FreeSWITCH — commented out, enabled when telephony session begins
- `nginx/local.conf` — subdomain routing + SignalR WebSocket support
- Hosts file entries: `hubion.local`, `tms.hubion.local`, `demo.hubion.local` → `127.0.0.1`
- `Original Application/` is in `.gitignore` — local reference only, never committed

**Secrets**
- `.NET User Secrets` initialized for `ContactConnection.Api` — connection string NOT in any committed file
- Local connection: `Host=localhost;Port=5432;Database=hubion_master;Username=hubion;Password=hubion_dev`

**Domain**
- `Tenant` entity — `Tenant.Create(name, subdomain, planTier, timezone)`, private setters
- `TenantFeatureFlags` value object — 8 flags, all default off
- `CallRecord` entity — full schema per ARCHITECTURE.md §19
  - `handle_time_seconds` — PostgreSQL generated stored column
  - Typed JSONB: `Addresses` (CallAddresses), `CommitmentEvents` (List\<CommitmentEvent\>)
  - Opaque JSONB strings (owned by future engines): `FlowExecutionState`, `ApiResponseCache`, `TelephonyEvents`
  - PCI lifecycle fields: `SensitiveData`, `SensitiveDataStoredAt`, `SensitiveDataWipedAt`, `SensitiveWipeReason`
  - Static constant classes: `CallSource`, `CallRecordType`, `CallRecordStatus`
- `CallInteraction` entity — full schema per ARCHITECTURE.md §22
  - Static constant classes: `InteractionType`, `InteractionStatus`
- `AddressData` value object — full production field set (prefix, street, unit, flags: IsPOBox, IsCanada, IsForeign, IsMilitary, IsOutlyingUS, IsAKHI, IsVerified)
- `CallAddresses` value object — `Billing` + `Shipping` envelope (JSONB on call_records)
- `CommitmentEvent` value object — lock entry with supervisor override fields

**Application**
- `ITenantRepository`
- `ITenantProvisioningService`
- `ICallRecordRepository`
- `IAgentRepository`
- `IPasswordHasher`
- `ITokenService`
- `TenantContext` — scoped service holding the resolved `Tenant` for the current request

**Infrastructure**
- `ContactConnectionDbContext` — `public` schema, `tenants` table
- `TenantDbContext` — no default schema, `agents` + `call_records` + `call_interactions`; search_path applied per connection
- `TenantDbContextFactory` — builds `TenantDbContext` with `Search Path=tenant_{schema},public`
- `ScopedTenantDbContextFactory` — resolves tenant from `TenantContext`, creates context for current request
- `TenantDbContextDesignTimeFactory` — EF tooling targets `tenant_test_tenant` (dev test schema; never use TMS here)
- All EF configurations: `TenantConfiguration`, `AgentConfiguration`, `CallRecordConfiguration`, `CallInteractionConfiguration`
- All repositories: `TenantRepository`, `AgentRepository`, `CallRecordRepository`
- `TenantProvisioningService` — creates schema + runs `MigrateAsync` on provision
- `PasswordHasher` — BCrypt enhanced hashing, work factor 12 (singleton)
- `JwtTokenService` — HS256, 480-min expiry, full tenant+agent claims
- `ServiceCollectionExtensions.AddInfrastructure()`

**Commerce Engine (Session 7)**
- `Product` entity — all scalar fields relational; complex structures (pricing tiers, personalization, ship methods, flags) as JSONB; `ProductInventoryStatus` enum; `CanAddToCart()` guard; variant self-reference via `ParentProductId`
- `ProductKit` entity — fixed (`CreateFixed`) and variable (`CreateVariable`) kit components
- Commerce value objects: `PaymentInstallment`, `QuantityPriceBreak`, `ProductShipMethod`, `PersonalizationPrompt`, `ProductFlag`, `AutoShipInterval`, `TierRange`, `CartDocument`, `CartItem`, `CartPaymentBreakdown`
- `CallRecord.Cart` — JSONB column on `call_records` (typed `CartDocument`); `SetCart()` method
- `IProductRepository`, `IPricingService` Application interfaces
- `PricingService` — MixMatch → QPB → base fallback; weight/subtotal shipping tier resolution; installment splitting with rounding correction in payment 1
- `ProductRepository` — lazy factory pattern; `ILike` search on description
- `products` + `product_kits` tables in tenant schema; `cart` column on `call_records`
- API: `POST/GET /api/v1/products`, `GET /api/v1/products/{id}`, `GET /api/v1/products/sku/{sku}`, `GET/PUT /api/v1/call-records/{id}/cart`
- Migration `AddProductsAndCart` applied to `tenant_tms` ✓

**Test Projects (Session 14)**
- `tests/ContactConnection.Domain.Tests/` — 45 passing xUnit tests across 4 test classes:
  - `ProductInventoryTests` — `CanAddToCart`, `Reserve`, `Release`, `Confirm` for all InventoryStatus values and DecrementOnOrder states
  - `OrderLineLifecycleTests` — `FromCartItem` snapshot, `Ship`, `MarkDelivered`, `Cancel`, `CreateFromSubscription`
  - `OrderLifecycleTests` — `Cancel`; `RefreshStatus` all transitions; `CreateFromSubscription` null CallRecordId guard
  - `SubscriptionLifecycleTests` — `IsDue` (incl. past-date via backing field reflection), `RecordShipment`, `Pause/Resume/Cancel`
- `tests/ContactConnection.Application.Tests/` — 20 passing xUnit tests across 2 test classes:
  - `PricingServiceResolvePaymentsTests` — base fallback, QPB threshold/miss/best-break, MixMatch threshold/miss/priority/exclusion
  - `PricingServiceCalculateTotalsTests` — empty cart, subtotal/shipping/tax, shipping-exempt, weight-tier/subtotal-tier/precedence, multi-payment breakdowns, split/no-split shipping, RoundSplit remainder
- `PricingService` constructed with real `FlatRateTaxProvider` + `TaxProviderFactory` (pure computation, no mocking needed)
- Both projects added to `ContactConnection.slnx`; `dotnet test` runs all 65 tests in ~600ms

**Commerce Engine — Category/attribute system (Session 13)**
- `ProductCategory` entity — hierarchical tree via self-referential `ParentId`; `Name`, `Slug`, `DisplayOrder`, `IsActive`; `Children` navigation; `Create()`, `Rename()`, `Activate()`, `Deactivate()`
- `ProductAttribute` entity — attribute definitions per tenant; `Name`, `Slug`, `DisplayOrder`, `IsActive`; `Values` navigation; `AddValue()` returns new value
- `ProductAttributeValue` entity — discrete values for an attribute (e.g. "Red" for "Color"); `AttributeId` FK; public `Create()` factory
- `Product` updated — `Categories` and `AttributeValues` many-to-many navigations; `AssignToCategory()`, `RemoveFromCategory()`, `SetAttributeValue()` (one-per-attribute), `RemoveAttributeValue()` domain methods
- `IProductCategoryRepository` — `GetRootsAsync`, `GetChildrenAsync`, `GetByIdAsync`, `AddAsync`
- `IProductAttributeRepository` — `GetByIdAsync`, `GetValueByIdAsync`, `GetAllAsync`, `AddAsync`, `AddValueAsync`, `SaveChangesAsync`
- `IProductRepository.SearchAsync` — extended with `categoryId` + `attributeValueIds` (AND-faceted) parameters
- `ProductCategoryConfiguration` — `product_categories` table; self-referential FK; tenant+slug unique index
- `ProductAttributeConfiguration` — `product_attributes` table; `HasMany(Values).WithOne()` cascade
- `ProductAttributeValueConfiguration` — `product_attribute_values` table
- `ProductConfiguration` — two `UsingEntity` many-to-many blocks: `product_category_map` + `product_attribute_assignments`
- `ProductCategoryRepository`, `ProductAttributeRepository` — lazy factory pattern
- `ProductRepository.SearchAsync` — `Include(Categories)`, `Include(AttributeValues)`, `Where(p.Categories.Any(...))`, per-facet AND filter
- API: `POST /categories`, `GET /categories?parentId=x`, `GET /categories/{id}`
- API: `POST /attributes`, `GET /attributes`, `GET /attributes/{id}`, `POST /attributes/{id}/values`
- API: `POST /products/{id}/categories/{categoryId}`, `DELETE /products/{id}/categories/{categoryId}`
- API: `POST /products/{id}/attribute-values/{valueId}`, `DELETE /products/{id}/attribute-values/{valueId}`
- `GET /products` updated — `categoryId` + `attributeValueIds` query params; product response embeds `categories[]` + `attributeValues[]`
- Migration `AddCategoriesAndAttributes` applied to `tenant_tms` ✓
- Bug fix: `ProductAttributeValue` values added via `DbSet.AddAsync` directly (not through collection tracking) to avoid `DbUpdateConcurrencyException` in EF Core 10

**Commerce Engine — Subscription lifecycle + ContactConnection.Worker (Session 12)**
- `Order.CallRecordId` nullable; `Order.CreateFromSubscription` + `OrderLine.CreateFromSubscription` factories
- `Subscription` entity — AutoShip enrollment snapshot; `IsDue()`, `RecordShipment()`, `Pause()`, `Resume()`, `Cancel()`; `SubscriptionStatus` static class
- `ISubscriptionRepository`, `ISubscriptionOrderCreator` (Application); `SubscriptionRepository`, `SubscriptionOrderCreator`, `SubscriptionConfiguration` (Infrastructure)
- `OrderService` auto-creates subscriptions on order commit for any `AutoShip=true && IntervalDays>0` lines
- API: `GET /call-records/{id}/subscriptions`, `GET /subscriptions/{id}`, `POST .../pause`, `.../resume`, `.../cancel`
- `ContactConnection.Worker` project — `Microsoft.NET.Sdk.Worker`; `SubscriptionProcessingService : BackgroundService`; hourly cadence; per-tenant scoped processing via `TenantContext.Current` injection; per-subscription error isolation
- Migration `AddSubscriptions` applied ✓

**Commerce Engine — Order entity (Session 11)**
- `Order` entity — `CreateFromCart(id, tenantId, callRecordId, cart, lines)`; `Status` lifecycle (`confirmed` → `partially_shipped` / `shipped` / `delivered` / `cancelled`); `PaymentBreakdowns` JSONB snapshot; `Cancel()`, `RefreshStatus()` (derives order status from lines, stamps `ShippedAt`/`DeliveredAt`)
- `OrderLine` entity — full `CartItem` snapshot at commit time; `FulfillmentStatus` lifecycle; `Ship(trackingNumber)`, `MarkDelivered()`, `Cancel()`; `FromCartItem()` factory
- `IOrderRepository`, `IOrderService` — `CreateFromCartAsync` idempotent; confirms inventory (`IInventoryService.ConfirmCartAsync`) on first commit
- `OrderRepository`, `OrderService`, `OrderConfiguration`, `OrderLineConfiguration`
- `TenantDbContext`: `Orders` + `OrderLines` DbSets; migration `AddOrders` applied ✓
- API: `POST /call-records/{id}/order` (201 create / 200 idempotent), `GET /call-records/{id}/order`, `GET /orders/{id}`, `POST /orders/{id}/cancel`, `POST /orders/{id}/lines/{lineId}/ship`, `.../deliver`, `.../cancel`

**Commerce Engine — Inventory reservation (Session 10)**
- `Product.QtyReserved` — units held in active carts; does not decrement `QtyAvailable` until confirmed
- `Product.Reserve(qty)` — soft hold; returns false (no-op) if `CanAddToCart` fails; only tracks if `DecrementOnOrder = true`
- `Product.Release(qty)` — releases soft hold (floor 0); called on cart replace/clear
- `Product.Confirm(qty)` — converts soft hold to real decrement; decrements both `QtyAvailable` and `QtyReserved` (floor 0)
- `Product.CanAddToCart(qty)` — `NoBackorder` case now uses `QtyAvailable - QtyReserved - qty >= MinimumQty`
- `IInventoryService` (Application) — `ReserveCartAsync` (all-or-nothing, returns failing SKUs), `ReleaseCartAsync`, `ConfirmCartAsync`
- `InventoryService` (Infrastructure) — lazy factory pattern; single-query load of all referenced products; all-or-nothing validate-then-apply
- `ProductConfiguration` — `qty_reserved integer` mapped
- `SetCart` endpoint — releases old reservations, reserves new cart, restores old on failure (409 with `unavailableSkus`), only saves after reserve confirmed
- Migration `AddInventoryReservation` applied to `tenant_tms` ✓

**Commerce Engine — Tax service integration (Session 9)**
- `ITaxProvider` (Application) — `ProviderKey` + `CalculateTaxAsync` → `TaxResult(Rate, TaxAmount, Jurisdictions?)`; `JurisdictionTax` for future multi-state breakdown
- `ITaxProviderFactory` (Application) — `Resolve(providerKey?)` falls back to default (empty-key) provider
- `FlatRateTaxProvider` (Infrastructure) — default provider (`ProviderKey = ""`); applies `CartDocument.TaxRate` to taxable subtotal; no I/O
- `TaxProviderFactory` (Infrastructure) — dispatch dictionary built from all registered `ITaxProvider` instances at startup
- `PricingService` — now takes `ITaxProviderFactory`; `CalculateTotalsAsync` (renamed from sync) delegates tax to resolved provider
- `CartDocument.TaxProvider` field — `null`/`""` = flat rate; future values: `"avalara"`, `"taxjar"`
- Bug fix: payment installment breakdown now correctly assigns full shipping/tax to payment 1 when split=false (previously divided by N)
- `JsonStringEnumConverter` registered globally via `ConfigureHttpJsonOptions` — all enums serialize as strings API-wide
- DI: `FlatRateTaxProvider` → singleton `ITaxProvider`; `TaxProviderFactory` → singleton `ITaxProviderFactory`

**Commerce Engine — Offer separation (Session 8)**
- `Offer` entity — sales configuration layer; `ProductId` FK, `Name`, `FullPrice`, `Shipping`, `TaxExempt`, `ShippingExempt`, `AllowPriceOverride`, `MixMatchCode`, upsell fields, AutoShip fields, ship-to/delivery options, `IsActive`, `ValidFrom`/`ValidTo` (campaign window), 7 JSONB pricing columns, `IsAvailable()` guard
- `Product` refactored — now physical item only: Sku, Description, Weight, inventory fields, geographic surcharges, AliasSKUs, Keywords; `Offers` navigation collection
- `CartItem` updated — `OfferId` added; `ProductId` + `Sku` explicitly documented as order-time snapshots
- `IOfferRepository`, `OfferRepository`, `OfferConfiguration` — lazy factory pattern, 3 indexes
- `IPricingService.ResolvePayments` — signature changed to take `Offer` (not `Product`)
- `OffersEndpoints` — POST (create), GET (active list), GET/{id}, POST/{id}/activate, POST/{id}/deactivate, GET/product/{productId}
- `ProductsEndpoints` — CreateProductRequest stripped to physical fields only; product response embeds Offers
- Migration `SeparateProductFromOffer` applied — 24 pricing columns dropped from products, offers table created ✓

**Flow Engine (Session 5)**
- `Flow` entity + `FlowSession` entity — full lifecycle methods
- `FlowType` (`crm` | `telephony`), `FlowSessionStatus` (`active` | `complete` | `abandoned`)
- `IVariableResolver` + `VariableResolver` — `{{namespace.field}}` tags, all 7 namespaces, condition evaluator
- `VariableContext` — runtime data bag passed to resolver (call_record, caller, agent, tenant, input, api, flow)
- `FlowExecutionContext` — full runtime state, Redis serialization
- `INodeHandler` + `NodeHandlerBase` — dispatch interface + shared helpers
- Node handlers: `ScriptNodeHandler`, `InputNodeHandler`, `BranchNodeHandler`, `SetVariableNodeHandler`, `ApiCallNodeHandler`, `EndNodeHandler`
- `FlowEngine` — Start/Advance/GetCurrentState, auto-advance loop, Redis (12hr TTL) + PostgreSQL on complete; `isStart` parameter distinguishes first-display from agent-advance to prevent script-node infinite loop
- `IFlowNotifier` (Application) + `FlowNotifier` (API) — Clean Architecture SignalR abstraction
- `FlowRepository`, `FlowSessionRepository` — lazy factory pattern
- `FlowConfiguration`, `FlowSessionConfiguration` — EF configs, JSONB columns, indexes
- `StackExchange.Redis` + `Microsoft.Extensions.Http` packages added to Infrastructure

**API**
- `TenantResolutionMiddleware` — resolves tenant from `X-Tenant-Subdomain` header, populates `TenantContext`
- JWT Bearer authentication — ValidateIssuer/Audience/Lifetime/SigningKey, 1-min clock skew; WebSocket token from query string; **`MapInboundClaims = false`** (required — keeps `sub` as `"sub"`, not remapped to `ClaimTypes.NameIdentifier`)
- `POST /api/v1/auth/login` — BCrypt verify + JWT issue; identical 401 for unknown email and wrong password
- `POST /api/v1/agents` — AllowAnonymous() bootstrap endpoint
- `GET /api/v1/agents/{id}` — RequireAuthorization()
- `POST /api/v1/tenants` — provision tenant (creates record + PostgreSQL schema + runs migrations)
- `GET /api/v1/tenants/{id}`
- `GET /api/v1/call-records/{id}` — tenant-scoped, returns full record with interactions
- `POST /api/v1/flows` — create draft flow
- `GET /api/v1/flows` — list active flows for tenant
- `GET /api/v1/flows/{id}` — get flow by id
- `POST /api/v1/flows/{id}/publish` — publish draft
- `POST /api/v1/flow-sessions` — start session, returns first node state
- `GET /api/v1/flow-sessions/{id}` — get current node state (reconnect)
- `POST /api/v1/flow-sessions/{id}/advance` — submit agent input, get next node
- `FlowHub : Hub<IFlowHubClient>` at `/hubs/flow` — JoinSession, LeaveSession, JoinSupervisorView

**Database (verified in PostgreSQL)**
- `public.tenants` — all columns, indexes
- `tenant_tms.agents` — all columns, unique email index
- `tenant_tms.call_records` — all columns, generated column, 7 indexes
- `tenant_tms.call_interactions` — all columns, FK cascade
- `tenant_tms.flows` — definition JSONB, indexes
- `tenant_tms.flow_sessions` — variable_store + execution_history JSONB, indexes
- Each has its own `__EFMigrationsHistory` table

---

### CRMPro Flow Engine Analysis (Session 4 — informs Session 5 build)

**Tag syntax:** `<*...*>` with executable VB.NET between delimiters. ContactConnection uses `{{namespace.field}}` (declarative, safe, no-code).

**Namespace mapping (CRMPro → ContactConnection):**
- `CallDetail.*` → `{{call_record.*}}` + `{{caller.*}}`
- `User.*` → `{{agent.*}}`
- `ScriptForm.[ControlName].Text` → `{{input.[node_id]}}`
- `WebServiceControl.Response` → `{{api.[node_id].*}}`
- Programmatically set values → `{{flow.*}}`

**Script structure:** AccordionControl = top-level container; each AccordionItem panel = one flow step. Branching = button click handlers calling `SelectedIndex = N`. ContactConnection replaces this with a `branch` node with declared condition + named transitions.

**Node types confirmed by real CRMPro usage:**
- `script` ← RichTextBoxControl/LabelControl with ScriptBox template
- `input` ← TextBox, ComboBox, CheckBox, DateTimePicker, AddressControl
- `branch` ← procedural If/Then in button click handlers
- `api_call` ← WebServiceControl (all fields template-based with `<*...*>`)
- `set_variable` ← PostCallCode response mapping after API call
- `end` ← final accordion panel

**Pending decision:** Branch condition complexity — simple (`==`, `>`, `contains`) or full boolean (`&&`, `||`). Recommendation: simple first.

---

### What Is NOT Done Yet — Commerce Engine Gap Roadmap (Sessions 8–13)

These gaps were identified in Session 7 analysis. Each session tackles one gap end-to-end with verification before moving to the next.

| Session | Gap | Key work |
|---|---|---|
| **8** | Product/Offer separation | ✓ Complete — Session 8 |
| **9** | Tax service integration | ✓ Complete — Session 9 |
| **10** | Inventory reservation | ✓ Complete — Session 10 |
| **11** | Order entity | ✓ Complete — Session 11 |
| **12** | Subscription lifecycle | ✓ Complete — Session 12 |
| **13** | Category/attribute system | ✓ Complete — Session 13 |

**Custom Fields (Session 15)**
- `public.data_types` — 8 seeded types (string, integer, decimal, currency, boolean, date, datetime, json); each carries `IsAggregatable` + `AggregationFunctions` to drive reporting widget builder
- `custom_field_definitions` — tenant/client/campaign scoped; scope resolution: campaign > client > tenant
- `custom_field_values` — typed columns (one populated per row); `(call_record_id, definition_id)` unique
- `call_records.custom_fields` JSONB — denormalized snapshot updated on every set/delete; fast read without joins
- `ICustomFieldService` — scope resolution, typed parsing, snapshot refresh
- API: `GET /data-types`, `POST/GET/PATCH /custom-field-definitions`, `GET/PUT/DELETE /call-records/{id}/custom-fields/{defId}`

---

### Strategic Decision (Session 16)

Building the **full ContactConnection platform** — not releasing ContactConnection Flow as a standalone product first. Reporting and dashboards will be built **last** as all other systems (telephony, commerce, flow, custom fields, chat) feed into it.

**Remaining build order:**
1. **ContactConnection.Web — Agent UI** ✓ Complete (Session 16)
2. **ContactConnection.Web — Flow Designer** ✓ Complete (Session 17)
3. **ContactConnection.Web — Script Node Editor** ✓ Complete (Session 18)
4. **ContactConnection.Web — Flow Designer enhancements** ✓ Complete (Session 19): variable panel in script editor modal, input node per-option handles, output variable field, Flows management page (`/flows`)
5. **ContactConnection.Web — UI polish + dark theme** ✓ Complete (Sessions 20-22): tagline branding, login page styling, dark theme throughout Flows/Designer, canvas node dark theme, navbar logo flush layout
6. **ContactConnection.Web — Specialized flow nodes** ✓ Complete (Sessions 23–29): Email node with DNS validation + blocking; inline script on input/email nodes; phone node; address node; section node (named sections, lock/jump, read-only locked display); execute_flow node (sub-flow call stack, shared variables, unwind on return); transition_to_flow node (one-way handoff)
7. **Platform Portal — Tenant Management** ✓ Complete (Sessions 31–33)
4. **FreeSWITCH + Telephony** (ESL, parallel queue engine, screen pop)
5. **ContactConnection.Integrations + API Builder** (no-code adapter framework)
6. **Chat System** (tenant-scoped, enterprise features)
7. **ContactConnection.HubService** (dedicated SignalR hub)
8. **Reporting & Dashboards** (last — all data feeds in by this point)

---

### Agent UI Layout Decision (Session 16)

The agent UI uses a **3-panel layout**:
- **Left (~240px):** Softphone — WebRTC soft phone, call controls, agent status
- **Center (flex):** Flow/Script display — the flow engine pushes node state here
- **Right (~300px):** Chat — tenant-scoped enterprise chat

---

### Chat System Architecture (planned — not yet built)

Tenant-scoped enterprise chat embedded in the agent UI right panel. Replaces need for Slack/Pumble/Ryver as a separate tool. All agent communication stays inside the platform.

**Features:** public/private channels, direct messages (1:1 and group), thread replies, emoji reactions, @mentions with notifications, agent presence (online/away/busy/offline), unread counts, message history with pagination, file attachments (future), pinned messages (future), search (future)

**Data model (tenant schema):**
```sql
chat_channels        — id, tenant_id, name, slug, description, type (public/private/direct), created_by, is_archived
chat_channel_members — channel_id, agent_id, role (admin/member), joined_at, last_read_at
chat_messages        — id, channel_id, agent_id, content, parent_message_id (null=top-level), is_edited, is_deleted, created_at, edited_at
chat_message_reactions — message_id, agent_id, emoji, reacted_at
chat_mentions        — id, message_id, mentioned_agent_id, is_read, created_at
```

**Real-time:** `ChatHub : Hub<IChatHubClient>` — JoinChannel, LeaveChannel, SendMessage, StartTyping, StopTyping
**Presence:** `agent_presence` tracked in Redis (key per agent, TTL heartbeat)
**DMs:** `type = 'direct'`, members = participants, slug derived from sorted agent IDs

---

### Flow Designer — ContactConnection.Web (Session 17)

- **Package upgrades** — Vite 6.4, Tailwind v4 (CSS `@import "tailwindcss"`, `@tailwindcss/vite` plugin, no more `tailwind.config.js` or `postcss.config.js`), React 19.1, React Router v7.6, Zustand v5.0, TypeScript 5.8; `node_modules` reduced from 172 → 125 packages
- **`@xyflow/react` v12** installed — React Flow canvas library
- **Backend** — `PUT /api/v1/flows/{id}` endpoint added (calls `Flow.UpdateDefinition`, bumps version); `GET /api/v1/flows/{id}` now includes `definition` field via `ToDetailResponse()`; `UpdateFlowRequest` record added
- **`src/types/designer.ts`** — `ContactConnectionNodeType`, `NodeData`, `ContactConnectionNodeDef`, `ContactConnectionFlowDefinition`, `NODE_META` (colors/descriptions), `defaultNodeData()` factory
- **`src/api/flows.ts`** — extended with `create`, `getDetail`, `updateDefinition`, `publish` for designer; `FlowSummary`/`FlowDetail` types exported
- **Custom node components** (`src/components/designer/nodes/`):
  - `NodeShell.tsx` — shared base; colored header + ENTRY badge; target/source handles (single, dual, none)
  - `ScriptNode` (blue), `InputNode` (emerald), `BranchNode` (amber), `SetVariableNode` (violet), `ApiCallNode` (indigo), `EndNode` (red)
  - Branch/ApiCall nodes have dual source handles (true/false, success/error) with color coding
- **`NodePalette.tsx`** — 180px left panel; draggable node type cards; sets `dataTransfer`
- **`NodePropertiesPanel.tsx`** — 288px right panel; type-specific form fields (script: content textarea; input: fieldType + required + options; branch: condition expression; set_variable: dynamic key-value assignments; api_call: method/url/headers/body; end: status); Set Entry Node button; Delete Node button
- **`FlowDesignerPage.tsx`** — full canvas page:
  - `ReactFlowProvider` wrapper + `DesignerCanvas` inner component (uses `useReactFlow`)
  - Drag-and-drop nodes from palette via `onDrop` + `screenToFlowPosition`
  - Edge connections via `onConnect` + `addEdge` (smoothstep)
  - Node click → properties panel; pane click → deselect
  - Delete key removes selected nodes
  - `toContactConnectionDef()` — converts React Flow state → ContactConnection JSON definition (with `_pos` for layout persistence)
  - `fromContactConnectionDef()` — converts ContactConnection JSON → React Flow state (restores node positions)
  - Save: POST (new flow) or PUT (existing flow) to API; updates URL to `/designer/{id}`
  - Publish: calls `POST /flows/{id}/publish`; status message auto-clears
  - Load existing flow via `GET /flows/{id}` on mount (parses `definition` JSON)
  - React Flow: Background grid, Controls, MiniMap (color-coded by node type)
- **`App.tsx`** — added `/designer` (new flow) and `/designer/:id` (edit existing) routes; both `RequireAuth`-wrapped
- **`AgentShell.tsx`** — "Flow Designer" link in top bar navigates to `/designer`
- Build: **0 errors** ✓; 253 modules (React Flow adds ~170 modules to bundle)

---

### Script Node Rich Text Editor — ContactConnection.Web (Session 18)

- **Bug fix** — `AdvanceSessionRequest` field renamed from `input` → `inputValue` in TypeScript to match C# record; `FlowPanel.tsx` updated to send `{ inputValue: input }`; fixes input node infinite loop
- **`NodeDisplay.tsx`** — script content rendered via `dangerouslySetInnerHTML` with `className="script-content"` so HTML formatting displays correctly in agent UI
- **TipTap packages installed** — `@tiptap/react`, `@tiptap/starter-kit`, `@tiptap/extension-text-style` (named import), `@tiptap/extension-color`, `@tiptap/extension-highlight`, `@tiptap/extension-underline`, `@tiptap/extension-font-family`, `@tiptap/extension-image`
- **`RichTextEditor.tsx`** — full-featured toolbar: font family (Default/Arial/Georgia/Verdana/Times New Roman/Courier New), font size (10–28px, custom TipTap `FontSize` extension via `addGlobalAttributes`), B/I/U, text color picker, highlight color picker (each with swatch grid popover), bullet list, numbered list, insert image button, clear formatting, expand button (optional `onExpand` prop)
- **Image support** — `editorProps.handlePaste` for clipboard paste (screenshot/copy-from-browser); hidden file `<input>` for insert button; both encode as base64 Data URL — no server upload; CSS added for `img` in both `.script-editor` and `.script-content`
- **`ScriptEditorModal.tsx`** — fixed-position full-viewport modal (z-50), backdrop blur, 90vw × 85vh white container, full RichTextEditor (no expand), Done + backdrop-click close
- **`ScriptContentEditor.tsx`** — wrapper with `modalOpen` state; when modal open: compact editor unmounted and placeholder shown; when closed: compact editor remounts from `content` prop; exported for use in NodePropertiesPanel
- **`NodePropertiesPanel.tsx`** — script case uses `<ScriptContentEditor key={node.id}>` (key resets TipTap when switching nodes)
- **Branding** — `hubion-favicon.svg` + `hubion-logo.svg` copied to `ContactConnection.Web/public/`; `index.html` favicon link; login page: 56px favicon icon centered in card header; agent shell header: 24px favicon + white "ContactConnection"; flow designer header: full logo (h-8) with divider separator
- **`index.css`** — image rules for `.script-editor` and `.script-content`

---

### What Is NOT Done Yet — Other

- **`ContactConnection.Web`** — Agent UI ✓ complete (Session 16 + 17): login, 3-panel layout, flow panel (live), softphone + chat placeholders, Flow Designer
- **`ContactConnection.Web` — Flow Designer** ✓ complete (Session 17) — see below
- **`ContactConnection.Web` — Script Node Editor** ✓ complete (Session 18): TipTap rich text editor with font family/size, bold/italic/underline, text color, highlight color, bullet/numbered lists, image paste + insert, popout modal editor; ContactConnection branding assets deployed across all pages
- **Chrome Extension** — web automation bridge not yet built ← next
- **Chat System** — backend domain/API/SignalR not yet built (UI placeholder exists)
- **`ContactConnection.HubService`** — dedicated SignalR hub project not yet created
- **`ContactConnection.Integrations`** — adapter framework project not yet created
- **Test projects** — `ContactConnection.Domain.Tests` ✓ (45 tests), `ContactConnection.Application.Tests` ✓ (20 tests); `ContactConnection.Infrastructure.Tests` and `ContactConnection.Api.Tests` not yet created

---

### Key Architecture Decisions (from ARCHITECTURE.md)

- **Single Record of Truth** — one `call_records` row is authoritative for everything on a call
- **Separate PostgreSQL schema per tenant** — `tenant_{subdomain}` schema; `public` for platform tables
- **search_path routing** — `TenantDbContext` uses unqualified table names; `Search Path=tenant_{schema},public` on the connection routes to the right schema at runtime
- **Secrets** — Azure Key Vault (prod), .NET User Secrets (local). Never in config files or source. Setting `KeyVault:VaultUri` makes the Api and Worker read every app secret from Key Vault automatically via the Azure Key Vault configuration provider (`AddAzureKeyVault` in each `Program.cs`) — no code changes needed at any call site. Auth to Key Vault itself uses `DefaultAzureCredential` (Managed Identity in Azure, `az login` locally) — no bootstrap secret required. Local dev with `KeyVault:VaultUri` unset (the default) is completely unaffected and keeps using User Secrets.
- **All timestamps `timestamptz`** — UTC in DB, ISO 8601 on wire, IANA timezone at display
- **JSONB for flexible/unqueried fields** — typed columns for WHERE/GROUP BY/ORDER BY/aggregates
- **Commitment events = lock registry** — appended to call record; record state IS lock state

---

### Running the Stack

```bash
docker compose up -d                              # start all services
docker compose down                               # stop all services
dotnet watch run --project ContactConnection.Api             # hot-reload API (localhost:5135)
cd ContactConnection.Web && npm run dev                      # Vite dev server (localhost:5173), proxies /api + /hubs to :5135
dotnet ef migrations add <Name> --context TenantDbContext --project ContactConnection.Infrastructure --startup-project ContactConnection.Api
dotnet ef database update --context TenantDbContext --project ContactConnection.Infrastructure --startup-project ContactConnection.Api
dotnet ef migrations add <Name> --context ContactConnectionDbContext --project ContactConnection.Infrastructure --startup-project ContactConnection.Api
dotnet ef database update --context ContactConnectionDbContext --project ContactConnection.Infrastructure --startup-project ContactConnection.Api
```

pgAdmin: http://localhost:5050
MailHog: http://localhost:8025

---

### Secrets in Azure Key Vault (production)

Set `KeyVault:VaultUri` (an App Service/Container App setting — not a secret, just the vault's URI) and grant the app's Managed Identity a Key Vault RBAC role (`Key Vault Secrets User`). Create these secrets — the `--` becomes `:` when the app reads them via `IConfiguration`:

| Key Vault secret name | Config path | Used by |
|---|---|---|
| `Jwt--SigningKey` | `Jwt:SigningKey` | Api |
| `ConnectionStrings--DefaultConnection` | `ConnectionStrings:DefaultConnection` | Api, Worker |
| `ConnectionStrings--Redis` | `ConnectionStrings:Redis` | Api, Worker |
| `Resend--ApiKey` | `Resend:ApiKey` | Api |
| `FreeSWITCH--EslPassword` | `FreeSWITCH:EslPassword` | Api, Worker |

The SignalWire PAT (used only as a Docker build arg for the FreeSWITCH image, never read by .NET config) isn't reachable via the config provider — store it in Key Vault as a plain secret named `SignalWirePAT` and pull it into the local `.env` before building:

```powershell
./scripts/fetch-docker-secrets.ps1 -VaultName <your-vault-name>
docker compose build freeswitch
```
