# ContactConnection Platform — Architecture Document

**Prepared:** March 2026  
**Prepared by:** Call Center Solutions, LLC  
**Version:** 1.0 — Pre-Build Foundation Document  
**Status:** Authoritative reference for all Claude Code development sessions

---

## Table of Contents

1. [Governing Philosophy](#1-governing-philosophy)
2. [Platform Identity and Vision](#2-platform-identity-and-vision)
3. [Ownership and IP](#3-ownership-and-ip)
4. [Migration Strategy](#4-migration-strategy)
5. [Solution Structure](#5-solution-structure)
6. [Multi-Tenancy Architecture](#6-multi-tenancy-architecture)
7. [Security and Secrets Management](#7-security-and-secrets-management)
8. [Database Architecture](#8-database-architecture)
9. [JSON Flow Engine](#9-json-flow-engine)
10. [Visual Flow Designer](#10-visual-flow-designer)
11. [Variable Resolution Engine](#11-variable-resolution-engine)
12. [Web Automation Layer](#12-web-automation-layer)
13. [Chrome Extension Architecture](#13-chrome-extension-architecture)
14. [FreeSWITCH Telephony](#14-freeswitch-telephony)
15. [Parallel Queue Engine](#15-parallel-queue-engine)
16. [Reporting Architecture](#16-reporting-architecture)
17. [Integration Framework](#17-integration-framework)
18. [Built-In OMS](#18-built-in-oms)
19. [Call Record — Single Record of Truth](#19-call-record--single-record-of-truth)
20. [Custom Fields — Type System](#20-custom-fields--type-system)
21. [Flow State Immutability](#21-flow-state-immutability)
22. [Multi-Interaction Call Model](#22-multi-interaction-call-model)
23. [Timestamp and Timezone Strategy](#23-timestamp-and-timezone-strategy)
24. [PCI Compliance and Sensitive Data](#24-pci-compliance-and-sensitive-data)
25. [Infrastructure and Deployment](#25-infrastructure-and-deployment)
26. [Local Development Environment](#26-local-development-environment)
27. [Commercial Model](#27-commercial-model)
28. [Competitive Position](#28-competitive-position)
29. [Planned Claude Code Sessions](#29-planned-claude-code-sessions)

---

## 1. Governing Philosophy

### Single Record of Truth

Every decision in this architecture flows from one governing principle:

> **A single call record is the authoritative source for everything that happened on a call — from the moment it arrived at FreeSWITCH to disconnect, through every post-call process, for the lifetime of the record.**

This means:

- Telephony CDR data and CRM data live on the same record — never in separate systems requiring reconciliation
- Every node traversed in every flow is logged with full context and timestamp
- Every commitment event, field lock, API call, and state transition is appended to the record
- Every interaction within a call is a child of that record — never a separate record
- Talk time is calculated once from a single start and end timestamp — impossible to double-count
- The record is the audit trail — not a separate logging system

This principle is the answer to every data modeling question that arises during the build. When in doubt, ask: does this decision preserve the Single Record of Truth?

### Additional Governing Principles

**Explicit over implicit** — Configuration is always stored with the thing being configured, never inherited silently from parent context. Timezone, encryption, delivery method — all explicit per job, per report, per tenant.

**Honest reporting** — Metrics reflect operational reality, not optimistic accounting. Abandon rate includes all abandon types. Callback abandoned is an abandon. True contact rate is the authoritative metric.

**No-code ownership** — Business logic belongs to the people who understand the business. Flow design, script authoring, API configuration — all owned by operations staff, not developers.

**Open architecture** — Standard protocols (SIP, REST, JSON), open formats, no proprietary lock-in. Tenants bring their own telephony, their own integrations, their own workflows.

**Inclusive pricing** — Every feature available to every tenant. No per-feature charges. No per-MB storage fees. Platform economics are the platform's problem, not the tenant's.

---

## 2. Platform Identity and Vision

**Product Name:** ContactConnection  
**Domain:** hubion.io  
**Sub-product:** ContactConnection Flow (standalone scripting and web automation — first to market)  
**LLC:** Call Center Solutions, LLC  
**Beta Partner:** TMS Call Centers, Roseburg, Oregon (21-year relationship, no-pay beta, written agreement, copyright retained by LLC)

### What ContactConnection Is

ContactConnection is a unified multi-tenant cloud contact center platform where telephony, agent scripting, CRM, web automation, commerce, and reporting are a single coherent system — not products bolted together.

It is not a CRM with telephony added. It is not a telephony platform with a CRM bolted on. It is a platform designed from the ground up around the operational reality of call centers, built by someone who has lived inside that reality for 21 years.

### What ContactConnection Is Not

- It is not a rewrite of CRMPro (the existing .NET 4.8 system)
- It is not a migration project — CRMPro continues running in production throughout
- It is not a generic CRM adapted for call centers
- It is not built to compete on price with enterprise platforms — it competes on capability, honesty, and pricing philosophy

### ContactConnection Flow — First to Market

ContactConnection Flow is a standalone extractable piece of the full platform containing:

- Visual no-code call flow designer
- JSON flow execution engine
- Chrome/Edge browser extension for web automation
- Agent UI with integrated browser surface

ContactConnection Flow generates revenue while the full platform is built. Every line of code written for ContactConnection Flow is code already written for ContactConnection. Flows built in ContactConnection Flow import directly into the full platform without conversion.

---

## 3. Ownership and IP

- All platform IP owned by Call Center Solutions, LLC
- Copyright established from original build
- Written agreement with beta call center — no-pay beta, ownership never in question
- Physical device separation — LLC development on a dedicated machine with no VPN to call center infrastructure
- Timestamped development journal maintained
- GitHub repository under LLC account
- Shopify app owned by the call center (built as employee work) — ContactConnection's Shopify adapter is LLC IP that calls the call center's app APIs. These are distinct.

---

## 4. Migration Strategy

**Selected approach: Strangler Fig — Parallel .NET 8 Build**

- CRMPro (.NET 4.8 WinForms) continues running in production, untouched
- ContactConnection (.NET 8) is built alongside it as a new product
- Both systems share the same PostgreSQL instance during transition
- Schema changes to existing tables are additive only — nothing breaks CRMPro
- New ContactConnection tables and schemas are additive alongside existing schema
- Agents can run both systems simultaneously during transition
- Remote agents: Guacamole/VDI for CRMPro + local browser for ContactConnection, simultaneously
- Scripts migrate gradually via hybrid automated/manual process
- CRMPro retires organically as ContactConnection script coverage reaches 100%
- No cutover event. No deadline pressure. No disruption.

### Why Not Upgrade CRMPro to .NET 8

CRMPro contains deprecated APIs (BinaryFormatter, etc.) that do not exist in .NET 8. Rather than carrying problematic baggage into a new platform, the clean rebuild approach produces a better system and eliminates accumulated technical debt. The existing code serves as a living reference implementation and feature specification — not as source material for migration.

### Existing System Reference

The following CRMPro components serve as design reference for ContactConnection:

- **Script designer** — WinForms designer host with custom controls, generates designer.vb, .resources, and code files. Reference for JSON flow schema design.
- **Tag processor engine** — compiles dynamic script text to scriptbox.dll at design time. Reference for Variable Resolution Engine design.
- **API builder** — dynamic tag-based values in URL, method, headers, body, response. Reference for API call node and adapter framework design.
- **CefSharp automation** — browser control, JS injection, iframe interaction, recording masking. Reference for Chrome extension design.
- **Address control** — prefix, street, unit, city, state, zip with flags (IsCanada, IsForeign, IsPOBox, IsMilitary). Reference for address domain model.
- **Export processor** — tag-based file generation for Excel, CSV, PDF, Word, text. Reference for export pipeline design.
- **Offer/inventory/cart engine** — kits, variable kits, quantity price breaks, mix and match breaks, per-item addressing, commission, typed flags, inventory control. Reference for OMS commerce engine design.
- **Custom field system** — typed fields backed by data types reference table, object value with runtime type resolution. Reference for custom field type system.
- **100 production scripts** — generated .vb and .resources files available for all scripts. Reference for JSON node type taxonomy.

---

## 5. Solution Structure

Clean Architecture — dependency flow always inward toward Domain.

```
/ContactConnection.sln
│
├── /src
│   ├── ContactConnection.Domain
│   │   └── Core domain models, value objects, enums
│   │       No dependencies on any other project
│   │
│   ├── ContactConnection.Application
│   │   └── Business logic, use cases, interfaces
│   │       Depends only on Domain
│   │
│   ├── ContactConnection.Infrastructure
│   │   └── PostgreSQL (EF Core 8 + Npgsql)
│   │       External service clients
│   │       Adapter implementations
│   │       Depends on Application and Domain
│   │
│   ├── ContactConnection.Api
│   │   └── ASP.NET Core Web API
│   │       Controllers, middleware, SignalR hubs
│   │       Depends on Application and Infrastructure
│   │
│   ├── ContactConnection.Web
│   │   └── React frontend application
│   │       Agent UI, flow designer, supervisor dashboard
│   │       Client portal, admin surfaces
│   │
│   ├── ContactConnection.Worker
│   │   └── .NET 8 BackgroundService
│   │       Export processor, fulfillment triggers
│   │       Sensitive data wipe jobs, reporting rollups
│   │       CDR processing
│   │
│   ├── ContactConnection.HubService
│   │   └── ASP.NET Core SignalR hub
│   │       Agent real-time connections
│   │       Flow state push, telephony events
│   │
│   └── ContactConnection.Integrations
│       └── Integration adapter framework
│           All external service adapters
│           Generic adapter runtime
│
├── /tests
│   ├── ContactConnection.Domain.Tests
│   ├── ContactConnection.Application.Tests
│   ├── ContactConnection.Infrastructure.Tests
│   └── ContactConnection.Api.Tests
│
├── /freeswitch
│   ├── /conf                   FreeSWITCH config (in git)
│   └── /sounds                 IVR audio files
│
├── /nginx
│   ├── local.conf              Local dev proxy config
│   └── prod.conf               Production proxy config
│
├── docker-compose.yml          Local development stack
├── docker-compose.prod.yml     Production overrides
└── ARCHITECTURE.md             This document
```

---

## 6. Multi-Tenancy Architecture

### Tenant Isolation Model

**Separate PostgreSQL schema per tenant.** Each tenant gets their own schema within the shared database instance. This provides:

- Meaningful data isolation without full database separation
- Independent backup and restore per tenant if needed
- Clean migration path to dedicated database for enterprise tenants
- No risk of cross-tenant data leakage in queries

### Tenant Data Model

```sql
-- Platform level (public schema)
tenants
├── id                    uuid primary key
├── name                  varchar
├── subdomain             varchar unique        -- tms, clientname
├── custom_domain         varchar               -- crm.theircallcenter.com
├── schema_name           varchar unique        -- tenant_tms
├── plan_tier             varchar               -- basic, professional, enterprise
├── timezone              varchar               -- IANA: 'America/Los_Angeles'
├── is_active             boolean
├── trial_expires_at      timestamptz
├── billing_contact       varchar
├── feature_flags         jsonb                 -- controls enabled capabilities
└── created_at            timestamptz

-- Within each tenant schema
clients                   -- call center's end clients
campaigns                 -- per client
agents                    -- call center agents
agent_skills              -- skill profiles per agent
queues                    -- routing queues
flows                     -- call flow definitions
call_records              -- the heart of the platform
call_interactions         -- per-interaction within calls
```

### Feature Flags

Feature flags on the tenant record control what capabilities are available. Enabling a capability for a tenant is a database update — no code change, no deployment:

```json
{
  "telephony_native": true,
  "telephony_byod": true,
  "web_automation": true,
  "oms_built_in": false,
  "shopify_adapter": true,
  "advanced_reporting": true,
  "parallel_queuing": true,
  "api_builder": true
}
```

Client-level feature flags within a tenant control per-client capabilities:

```json
{
  "has_own_payment_gateway": true,
  "has_own_order_management": false,
  "built_in_fulfillment": true,
  "shopify_integration": true
}
```

### URL Architecture

Subdomain per tenant via wildcard DNS and reverse proxy:

```
*.hubion.io  →  Proxy  →  Tenant resolution  →  API
tms.hubion.io           →  TMS Call Center tenant
clientname.hubion.io    →  Client Name tenant
tms-admin.hubion.io     →  LLC platform administration
```

Wildcard SSL via Cloudflare or Let's Encrypt covers every subdomain automatically. New tenant subdomain is live immediately on provisioning — no DNS change required per tenant.

Custom domain support for enterprise tenants: `crm.theircallcenter.com` CNAME to ContactConnection proxy, SSL managed automatically.

### Tenant Provisioning Flow

```
LLC creates tenant in platform admin
    ↓
Tenant record created (public schema)
    ↓
PostgreSQL schema created: CREATE SCHEMA tenant_{subdomain}
    ↓
Schema migrations run for new tenant
    ↓
Subdomain registered in routing table
    ↓
Tenant admin credentials provisioned
    ↓
https://{subdomain}.hubion.io is live
```

---

## 7. Security and Secrets Management

### Principles

- No secrets in configuration files or source code — ever
- No connection strings in appsettings.json
- No API keys in any committed file
- Secrets live in Azure Key Vault (production) or environment variables (local dev)
- .NET 8 user secrets for local development only

### Encrypted Credential Store

Client API keys entered at runtime by call center staff — never pre-configured in code:

```sql
client_api_credentials
├── id                    uuid
├── tenant_id             uuid
├── client_id             uuid
├── credential_label      varchar         -- 'Shopify Production'
├── encrypted_key         bytea           -- AES-256 encrypted
├── key_hint              varchar(4)      -- last 4 chars only
├── created_by            uuid
├── created_at            timestamptz
├── last_used_at          timestamptz
└── is_active             boolean
```

At entry time: plaintext key encrypted with platform master key before writing. Plaintext never touches the database or logs.

At runtime: decryption happens in memory, in the service that needs it. Plaintext key lives only for the duration of the API call.

Master encryption key: Azure Key Vault in production. Never in code, config, or environment variables directly.

### Authentication Architecture

- **Master tenant (LLC admin):** Azure AD
- **Tenant staff (call center supervisors, admins):** Google Workspace, email/password, SAML, OIDC
- **Client portal users:** email/password, SSO if client provides it
- **Agents:** simplified login via tenant-scoped credentials
- **API access:** tenant API keys for external systems and webhooks

---

## 8. Database Architecture

### Technology

- **PostgreSQL 16** via Npgsql and EF Core 8
- **PgBouncer** for connection pooling in production
- **Separate schema per tenant** for isolation
- **JSONB** for flexible nested data
- **Typed relational columns** for anything queried in WHERE, GROUP BY, ORDER BY, or aggregates

### Hybrid Relational / JSONB Strategy

**Decision rule:**

```
Will this field appear in a WHERE clause,
GROUP BY, ORDER BY, or aggregate function
in a reporting query?
    YES → proper column with index
    NO  → JSONB

Is this field variable in structure between
campaigns or clients?
    YES → JSONB (with GIN index if queried)
    NO  → proper column
```

### Core Schema Design Principles

- All timestamps as `timestamptz` (with time zone) — never `timestamp`
- All timestamps stored in UTC — conversion at display layer only
- UUIDs as primary keys throughout — `gen_random_uuid()`
- Soft deletes where audit history matters — `is_active` flag
- Row-level security on tenant schemas
- Generated columns for derived values (handle_time_seconds)

---

## 9. JSON Flow Engine

### Overview

The JSON flow engine is the most architecturally significant component of ContactConnection. A universal DSL (Domain Specific Language) expressed as JSON drives both agent scripting and telephony routing. The engine is a server-side interpreter — the agent UI is a thin display layer reacting to SignalR pushes.

The same engine powers:
- CRM call flows (agent scripting, data capture, web automation)
- Telephony flows (IVR, queue routing, screen pop)

### Flow Definition Schema

```json
{
  "flow_id": "uuid",
  "flow_type": "crm | telephony",
  "version": 1,
  "name": "DR Sales Flow",
  "entry_node": "node_001",
  "nodes": {
    "node_001": {
      "type": "script",
      "label": "Opening Greeting",
      "content": "Hello {{caller.first_name}}, thank you for calling...",
      "transitions": {
        "default": "node_002"
      }
    },
    "node_002": {
      "type": "branch",
      "label": "Existing Customer?",
      "condition": "{{account.is_existing}} == true",
      "transitions": {
        "true": "node_003",
        "false": "node_010"
      }
    }
  }
}
```

### CRM Node Type Taxonomy

**Display / Interaction**
- `script` — agent reads text with resolved template variables
- `input` — agent captures value (text, select, checkbox, date, address, phone, email)
- `display` — shows read-only data from call record

**Logic / Flow Control**
- `branch` — conditional split based on variable expression
- `switch` — multi-path branch based on variable value
- `loop` — repeat a sub-flow section
- `sub_flow` — embed another flow by reference (reusable components)

**Data / Integration**
- `api_call` — internal or external HTTP call via adapter framework
- `set_variable` — assign or compute a variable value
- `db_lookup` — direct call record / account data fetch
- `cart_action` — add/remove/update items in interaction cart

**Web Automation**
- `web_automation_step` — external site interaction via Chrome extension

**Interaction Control**
- `interaction_complete` — disposition current interaction, prompt for additional
- `end` — terminate flow with status

### Telephony Node Type Taxonomy

- `answer` — answer incoming call
- `ivr_menu` — play audio, collect DTMF input, branch on result
- `queue` — place call in agent queue (supports parallel queuing)
- `screen_pop` — bridge to CRM flow on agent answer
- `transfer` — blind or supervised transfer
- `voicemail` — record voicemail to mailbox
- `set_variable` — assign telephony context variable
- `end` — terminate call with status

### Flow Execution Context

```csharp
public class FlowExecutionContext
{
    public Guid FlowId { get; init; }
    public Guid SessionId { get; init; }
    public Guid CallRecordId { get; init; }
    public Guid InteractionId { get; init; }
    public string CurrentNodeId { get; set; }
    public Dictionary<string, object> VariableStore { get; }
    public List<NodeExecutionRecord> ExecutionHistory { get; }
    public HashSet<string> LockedFields { get; }
    public List<CommitmentEvent> CommitmentEvents { get; }
}
```

### Flow Engine Loop

```
Load current node definition from JSON
    ↓
Resolve template variables in content
    ↓
Push node state to agent UI via SignalR
    ↓
Wait for agent action
    ↓
Evaluate transition, move to next node
    ↓
Append to execution history
    ↓
Repeat
```

### Flow Relaunch

When a flow is relaunched against an existing call record (incomplete call recovery, supervised edit, follow-up call, QA review):

1. Load call record
2. Apply all existing commitment events — locks are established before agent sees anything
3. Determine entry node based on relaunch mode
4. Continue

The record's state IS the lock state. Deterministic, auditable, requires no reconstruction.

**Relaunch modes:**
- `Continue` — incomplete call, resume at last incomplete node
- `Edit` — supervised correction, entry at earliest editable node
- `Review` — read-only replay from beginning (QA)
- `Service` — follow-up call, service entry point with post-commitment actions

---

## 10. Visual Flow Designer

### Overview

A no-code web-based canvas owned by account services and operations staff — not developers. The flow designer is the mechanism by which script authorship transfers from development to the people who understand the business.

### Technology

**React Flow** — purpose-built node graph library for the canvas surface. Each node type is a React component with a settings panel.

### Design Principles

- Business language labels throughout — "Decision" not "branch", "Save a Value" not "set_variable"
- Sensible defaults on every node — works out of the box with minimal configuration
- Validation on publish — not during building
- Plain English error messages — "Node 'Verify Address' has no path to an ending point"
- Advanced settings collapsed by default
- Library of reusable sub-flow building blocks
- Preview mode with sample data — no real call required to validate a flow

### Reusable Sub-Flow Library

Common patterns built once and reused across flows:
- Identity verification
- Address capture and verification
- Payment capture
- Subscription management
- Shopify order lookup
- Any flow segment worth standardizing

### Publishing

Save the flow JSON to PostgreSQL. Publish makes it active immediately — no compilation, no deployment, no developer involvement. Version is captured automatically on every publish.

---

## 11. Variable Resolution Engine

### Overview

A first-class platform component — not an implementation detail repeated across surfaces. Every component that needs dynamic value resolution uses this single service.

The same tag syntax works in:
- Flow node script content
- API call URL, method, headers, body, response mapping
- Export file templates
- Agent annotation bubble content
- Email and notification templates
- Address verification calls
- Web automation step configurations

### Variable Context Namespaces

```
{{caller.*}}            Inbound call data (caller ID, DNIS, etc.)
{{account.*}}           Loaded account/contact record fields
{{cart.*}}              Current interaction cart state
{{flow.*}}              Variables set during flow execution
{{input.[node_id]}}     Value captured at a specific input node
{{api.[node_id].*}}     Value from a specific api_call node result
{{agent.*}}             Current agent context
{{tenant.*}}            Tenant configuration values
{{call_record.*}}       Full call record fields
```

### Interface

```csharp
public interface IVariableResolver
{
    string Resolve(string template, ExecutionContext context);
    T Resolve<T>(T template, ExecutionContext context);
    ValidationResult Validate(string template, ExecutionContext context);
    IEnumerable<string> ExtractReferences(string template);
}
```

### Design Note

This is the same concept proven across four surfaces in CRMPro (script display, export processor, API builder, web automation). ContactConnection formalizes it as a single shared service with consistent syntax and shared context. A user who learns the tag syntax in the flow designer already knows it in the API builder.

---

## 12. Web Automation Layer

### Overview

The most differentiated capability in the ContactConnection platform. A visual call flow node hands off seamlessly to external website automation — forms fill themselves out, multi-page flows complete automatically, agent guidance bubbles appear on client websites. No other CRM platform has this as an integrated, call-flow-aware, configurable capability.

### What It Does

- Navigate external websites within the agent's browser context
- Populate form fields with call record data using adaptive injection
- Handle multi-page flows with cross-navigation state persistence
- Display agent guidance annotations on external sites
- Detect payment fields and trigger call recording masking automatically
- Support arbitrary iframe content including cross-origin frames

### Web Automation Step Node

```json
{
  "node_id": "node_checkout",
  "type": "web_automation_step",
  "label": "Shopify Checkout",
  "frame_target": {
    "type": "top"
  },
  "ready_condition": {
    "operator": "OR",
    "conditions": [
      {
        "type": "url_matches",
        "pattern": "*/checkout*"
      },
      {
        "type": "element_ready",
        "selector": "#checkout-form",
        "requires_visible": true,
        "requires_enabled": true
      }
    ]
  },
  "actions": [
    {
      "type": "set_field",
      "selector": "#email",
      "value": "{{call_record.email}}"
    }
  ],
  "branch_conditions": [
    {
      "type": "dom_check",
      "check": "element_exists",
      "selector": "#error-banner",
      "transition": "node_handle_error"
    }
  ],
  "default_transition": "node_confirmation",
  "on_failure": "node_automation_error"
}
```

### Page Readiness Strategy

Each automation step defines what "ready to proceed" means — not timing assumptions:

```
Step completes
    ↓
Flow knows expected next state
    ↓
Waits for EITHER URL pattern match
OR specific element visible and enabled
    ↓
Condition met → small buffer delay → execute next step
Timeout exceeded → handle failure with descriptive error
```

### Domain Configuration

Per-domain configuration controls injection strategy:

```json
{
  "domain": "checkout.clientsite.com",
  "framework": {
    "declared": "react",
    "allow_runtime_detection": false
  },
  "injection_strategy": {
    "field_population": "native_setter_with_events",
    "wait_defaults": {
      "timeout": 15000,
      "delay": 500
    },
    "late_load_buffer": 300
  },
  "selectors": {
    "email_field": "#checkout_email",
    "submit_button": "[data-testid='pay-now']"
  },
  "iframes": [
    {
      "selector": "iframe#payment-frame",
      "modify_sandbox": true,
      "permitted_host": "payment-processor.com"
    }
  ],
  "flags": {
    "needs_manual_review": false,
    "last_injection_success": "2026-03-17T14:23:11Z"
  }
}
```

### Agent Annotation System

Orange comic-strip bubbles and other UI elements overlaid on external sites — visible only to agents, invisible to the actual site's users:

```json
{
  "annotations": [
    {
      "id": "anno_001",
      "type": "bubble",
      "anchor_selector": "#obscure-field",
      "anchor_position": "top-right",
      "content": {
        "type": "instruction",
        "text": "Enter the 6-digit member ID from caller's welcome email",
        "color": "orange"
      },
      "dismiss": "on_field_focus"
    },
    {
      "id": "anno_002",
      "type": "action_button",
      "anchor_selector": "#shipping-method",
      "content": {
        "type": "action_button",
        "label": "Use Standard Shipping",
        "action": "click_element",
        "target_selector": "#shipping-standard"
      },
      "dismiss": "on_action"
    }
  ]
}
```

**Annotation types:** bubble, highlight, tooltip, action_button, warning_banner, progress_indicator

**Dismiss behaviors:** on_field_focus, on_field_complete, on_action, on_navigation, manual, never

Selectors stored in platform config — changing a selector after a site update takes seconds and requires no extension update.

---

## 13. Chrome Extension Architecture

### Overview

A Chrome/Edge browser extension is a first-class ContactConnection platform component — not a workaround. It provides the bridge between the ContactConnection agent UI and external websites, enabling web automation, agent annotation, and recording masking that no pure web application can provide.

### Browser Compatibility

Built to Chrome Manifest V3. Runs natively in:
- Google Chrome ✓
- Microsoft Edge ✓ (same Chromium engine since 2020)
- Brave, Opera, Vivaldi, Arc ✓ (all Chromium)

Submit to both Chrome Web Store and Microsoft Edge Add-ons Store. One codebase, doubled distribution.

### Extension Components

**Background Service Worker**
- Persistent coordination layer
- Maintains authenticated session with ContactConnection platform
- Receives injection commands via SignalR connection
- Maintains frame registry for active tab
- Routes commands to correct frame's content script
- Reports results back to platform

**Content Scripts**
- Injected into external pages via `all_frames: true`
- Direct DOM access on target pages
- Execute field population, cart.js calls, annotation injection
- Report page state back to background worker
- Run in isolated world by default; MAIN world via script tag injection when page JS scope access needed

**Portal Window Focus** *(added S167 — build alongside screen recording)*
- Web pages can't raise another tab; the extension can: `chrome.tabs.update(tabId, { active: true })`
  + `chrome.windows.update(windowId, { focused: true })` (needs the `tabs` permission)
- Tracks the user's agent-portal tab (`/agent`); the platform asks the extension to bring it forward
  when work lands there from elsewhere — first case: a supervisor **Take Over** from the dashboard
  (S167) moves the call + script into their already-open portal, which today only says "switch to
  that tab"
- Second case: **agents** — when the queue logic selects an agent for a call (offer, auto-connect,
  screen pop / script pop) and they aren't on the portal tab, the extension switches them to it as
  part of delivery, so a call never waits in a background tab
- Channel: the dashboard page messages the extension via `externally_connectable` (or its content
  script via `window.postMessage`); fall back to the on-screen note when the extension isn't installed

**Frame Registry**
- Tracks all frames (top + iframes) for the active tab
- `frameId → { url, parentFrameId, contentScriptReady }`
- `chrome.tabs.sendMessage` with `{ frameId }` targets specific frames
- Handles cross-origin iframes via `all_frames` + permitted hosts

### Framework Detection and Adaptive Injection

**Priority order:**
1. User-declared framework in domain config (skip detection)
2. Runtime detection with confidence scoring
3. Universal fallback strategy
4. Flag to admin dashboard for manual configuration review

**Per-framework injection strategies:**

- **React:** native HTMLInputElement value setter + `input`/`change` event dispatch
- **Vue:** direct value set + `input` event dispatch
- **Angular:** value set + `input`/`change`/`blur` event dispatch
- **Vanilla:** direct value set + `change` event
- **Unknown:** try all strategies, conservative delay buffer

### Recording Mask on Sensitive Fields

Payment field focus detection triggers automatic call recording pause:

```
Agent navigates to payment iframe
    ↓ frame loaded, content script active
Field focus event on sensitive field detected
    ↓ domain-level or field-level sensitivity match
Platform notified via background worker → SignalR
    ↓
FreeSWITCH ESL: uuid_record {callId} pause
    ↓
Audit log: masked_at, field, frame_url, call_id
    ↓
Agent completes card entry
    ↓
Field blur or navigation detected
    ↓
FreeSWITCH ESL: uuid_record {callId} resume
    ↓
Audit log: resumed_at, duration_masked
```

Full PCI compliance audit trail generated automatically.

### Enterprise Deployment

**Chrome:** Force-install via Chrome Enterprise policy / Google Admin  
**Edge:** Force-install via Microsoft Intune or Group Policy  
**VDI:** Install on base VDI image — every VDI instance gets it automatically  
**Updates:** Chrome Web Store auto-update, transparent to agents

---

## 14. FreeSWITCH Telephony

### Architecture

FreeSWITCH controlled via ESL (Event Socket Library) from a .NET hosted service. SIP is the universal standard — all internal telephony is SIP.

```
SIP Trunk Provider
    ↓ SIP
FreeSWITCH Core (Docker container)
    ↓ ESL TCP socket (port 8021)
ContactConnection ESL Service (.NET hosted service)
    ↓ events and commands
Flow Engine + Call Record
    ↓ SignalR
Agent Browser (WebRTC soft phone)
```

### SIP Standardization

All telephony uses SIP internally:
- Every carrier, trunk provider, and softphone speaks SIP
- One protocol to master — no proprietary interfaces
- WebRTC for agent soft phone — Chrome/Edge handles SIP-over-WebRTC via mod_verto
- FreeSWITCH translates between WebRTC and SIP transparently

### Tenant Telephony Options

```sql
tenant_telephony_config
├── type                  -- native_freeswitch | bring_your_own
├── provider_name         -- for BYOD: Five9, NICE, Genesys, etc.
├── screen_pop_method     -- webhook | api_poll | sip_header
├── cdr_import_method     -- api | sftp_drop | webhook
└── recording_access      -- api_url_template | sftp | none
```

**Native FreeSWITCH:** full unified record, complete call trace, all features  
**Bring-your-own:** screen pop via webhook, CDR import, inbound event API for abandon/callback events

### Inbound Telephony Event API

For BYOD tenants — events the platform wouldn't otherwise know about:

```
POST /api/v1/telephony/events
Authorization: Bearer {tenant_api_key}

{
  "event_type": "queue_abandon | prequeue_abandon | 
                 blind_transfer_away | blocked_prankster |
                 callback_requested | callback_attempted |
                 callback_completed | callback_abandoned |
                 callback_expired | callback_cancelled",
  "occurred_at": "2026-03-17T14:23:11Z",
  "contact_id": "ext_contact_xyz",
  "caller_id": "+15551234567",
  "campaign_id": "tenant_campaign_ref",
  "metadata": {}
}
```

Each event creates a call record stub for complete reporting coverage.

### Screen Pop Bridge

The `screen_pop` telephony node bridges telephony flow to CRM flow at the moment of agent answer:

```json
{
  "node_id": "node_screen_pop",
  "type": "screen_pop",
  "create_call_record": true,
  "launch_flow_id": "crm_flow_uuid",
  "transitions": {
    "default": "node_post_call"
  }
}
```

Call record created and CRM flow launched simultaneously at agent answer. Agent sees script before saying hello.

### WebRTC Soft Phone

Agent's browser IS their phone — no separate softphone required:
- Chrome/Edge WebRTC connects to FreeSWITCH mod_verto
- Call controls (answer, hold, mute, transfer, wrap-up) integrated in agent UI
- No desktop softphone installation
- Works on any OS — Linux workstations, Chromebooks, Windows, Mac

---

## 15. Parallel Queue Engine

### Overview

True parallel queuing — a call is simultaneously a candidate in multiple queues. The first available agent from any queue wins. This is structurally impossible in sequential-fallback platforms like NICE InContact.

### Use Case

Elite agents receive higher commission. Regular agents receive lower commission. Both groups queue the call simultaneously. If an elite agent becomes available within the first 30 seconds, they get the call and earn higher commission. If not, a regular agent answers immediately rather than waiting through a full timeout.

### Architecture

```csharp
public class ParallelQueueSession
{
    public Guid CallId { get; init; }
    public DateTimeOffset EnteredAt { get; init; }
    public List<QueueEntry> QueueCandidates { get; init; }
}

public class QueueEntry
{
    public Guid QueueId { get; init; }
    public int Priority { get; init; }
    public decimal CommissionRate { get; init; }
    public int? ExclusiveWindowSeconds { get; init; }
    public int? DelaySeconds { get; init; }
    public QueueEntryStatus Status { get; set; }
}
```

### Atomic Claim

Prevents two agents in different queues from claiming the same call simultaneously. Uses PostgreSQL row-level locking or Redis atomic operations. Only one agent can claim — all other queues notified immediately on claim.

### Commission Recording

Winning queue's commission rate recorded on call record. Full audit trail:

```sql
call_routing_outcomes
├── call_id
├── winning_queue_id
├── winning_agent_id
├── commission_rate_applied
├── queues_entered[]          -- all queues, entry/exit times, reasons
├── time_to_answer
└── parallel_session_id
```

### Queue Routing Variations

- **Priority windowed parallel** — elite queue exclusive for N seconds, then opens in parallel
- **Skill weighted parallel** — multiple skill groups simultaneously
- **Geographic parallel** — on-site and remote agents simultaneously
- **Campaign parallel** — multiple campaign agent pools simultaneously

### Skill-Based Routing

```sql
agent_skills
├── agent_id
├── skill_id              -- spanish_speaker, retention, billing, etc.
├── proficiency           -- 1-5
└── priority              -- order skills considered for routing

queue_routing_rules
├── queue_id
├── required_skills[]
├── preferred_skills[]
├── priority_method       -- longest_idle, most_skilled, round_robin, priority
└── overflow_queue_id
```

---

## 16. Reporting Architecture

### Honest Reporting Philosophy

ContactConnection reports what actually happened — not what looks best. This is a deliberate product decision and competitive differentiator.

### The Abandon Family

All four types treated as a unified family — caller wanted to speak with an agent and didn't:

```
Abandon Family
├── Pre-queue abandon        -- left during IVR before entering queue
├── In-queue abandon         -- left while waiting for agent
├── Post-queue abandon       -- reached voicemail/overflow, no live contact
└── Callback abandon         -- requested callback, did not answer it
```

### Callback Lifecycle

Full state tracking — not just requested and completed:

```
callback_requested
    ↓
callback_scheduled        -- window assigned
    ↓
callback_attempted        -- outbound placed, ringing
    ↓
callback_completed        -- caller answered ✓
callback_abandoned        -- caller did not answer (IS an abandon)
callback_expired          -- window passed, never attempted
callback_cancelled        -- caller called back in before callback fired
```

### True Contact Rate

```
completed / (completed + all_abandon_types)
```

Not attempts. Not requests. Actual successful connections as a proportion of genuine demand.

### Two-Layer Architecture

**Operational store (main PostgreSQL)**
- Seconds old
- Powers live agent dashboards and supervisor floor view
- Real-time campaign metrics for client portal
- Active call states

**Analytical store (materialized views / TimescaleDB)**
- Periodic rollups from operational data
- Powers historical reporting and trend analysis
- Prevents heavy reporting queries from impacting live performance
- Custom period comparisons, campaign analysis

### Dashboard Widget System

```sql
dashboard_definitions
├── tenant_id
├── role                  -- agent, supervisor, client, admin
├── layout                -- grid positions (jsonb)
└── widgets[]
    ├── widget_type       -- calls_handled, aht, conversion_rate, etc.
    ├── campaign_filter
    ├── time_period       -- today, wtd, mtd, custom
    ├── display_type      -- number, sparkline, bar, table
    └── position          -- grid x, y, w, h
```

Each widget type is a backend query + frontend component pair. Adding a metric is adding one query and one component. No developer involvement for supervisors configuring their own dashboards.

### Call Trace View

A visual timeline replay of every event on a call — from FreeSWITCH arrival to sensitive data wipe. Every node traversed, every branch condition evaluated, every API call made, every commitment event. Timestamped, clickable for full detail, instantly retrievable. This is replay, not reconstruction.

### Flow Version Logging

Every call record captures which flow version was active:

```
flow_id: DR_Sales_Flow
flow_version: v3.2
published_at: 2026-02-14T09:00:00Z
```

Before/after comparison when a flow is updated is meaningful — you know exactly which calls ran on which version.

---

## 17. Integration Framework

### Adapter Pattern

All external integrations implement common interfaces:

```csharp
public interface IServiceAdapter
{
    string AdapterName { get; }
    Task<AdapterResult> ExecuteAsync(
        AdapterRequest request,
        TenantCredentials credentials,
        CancellationToken ct);
}

public interface IPaymentAdapter : IServiceAdapter
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request);
    Task<RefundResult> RefundAsync(RefundRequest request);
    Task<TokenResult> TokenizeAsync(TokenizeRequest request);
}

public interface IShippingAdapter : IServiceAdapter
{
    Task<RateResult> GetRatesAsync(RateRequest request);
    Task<LabelResult> GenerateLabelAsync(LabelRequest request);
    Task<TrackingResult> GetTrackingAsync(string trackingNumber);
}

public interface IAddressAdapter : IServiceAdapter
{
    Task<VerificationResult> VerifyAsync(AddressRequest request);
    Task<SuggestResult> SuggestAsync(string partialAddress);
}
```

### Three-Tier Adapter Ecosystem

**Tier 1 — Platform adapters (LLC built, available to all tenants)**
- Payment: Stripe (primary), Authorize.Net, Braintree
- Address: USPS Validation API (platform default), SmartyStreets
- Shipping: EasyPost (covers USPS/UPS/FedEx/DHL via single integration), ShipStation
- Tax: TaxJar or Avalara
- Returns: Loop Returns, carrier return labels

**Tier 2 — Tenant custom adapters (no-code API builder, private to tenant)**

**Tier 3 — Tenant requested adapters (evaluated for platform promotion)**

### No-Code API Builder

Tenants build their own adapters without writing code. The builder generates a structured adapter definition JSON stored in the database. A generic adapter runtime interprets the definition at execution time — no code generation, no compilation.

Builder capabilities:
- Connection: base URL, authentication type (API key, Bearer, Basic, OAuth2), timeout, retry
- Endpoint: method, path, path variables, headers, request body
- Dynamic values: full variable resolution engine tag syntax throughout
- Response mapping: extract and store response values as flow variables
- Response processing: transform response before delivering to flow
- Test values: separate test value fields per variable
- Test button: shows full request, full response, variable mapping result inline

```json
{
  "adapter_id": "uuid",
  "tenant_id": "uuid",
  "type": "tenant_custom",
  "visibility": "private",
  "base_url": "https://api.clientsystem.com/v2",
  "auth": {
    "type": "bearer_token",
    "credential_ref": "cred_456"
  },
  "endpoints": [
    {
      "id": "get_customer",
      "method": "GET",
      "path": "/customers/{customerId}",
      "path_variables": {
        "customerId": "{{call_record.customer_id}}"
      },
      "response_map": [
        { "source": "customer.firstName", "target": "call_record.first_name" }
      ],
      "success_conditions": [
        { "type": "status_code", "operator": "equals", "value": 200 }
      ]
    }
  ]
}
```

### Adapter Request Workflow

Tenants submit requests for platform adapters via the in-app request form. LLC reviews against evaluation criteria (breadth of demand, API documentation quality, auth complexity, test credential availability). Accepted requests promoted to Tier 1 and made available to all tenants. Requesting tenant gets early access.

### Shopify Integration

The call center's Shopify app (owned by call center, built as employee work) exposes REST APIs that ContactConnection's Shopify adapter calls. The adapter is LLC IP. The app is call center IP. Clean separation.

Shopify adapter actions available as flow nodes:
- order.get, order.cancel, order.add_note, order.update_email
- order.create_return, customer.add_note
- Cart injection via cart.js through web automation extension
- Checkout flow via web automation (Shopify payment gateway requires storefront checkout — cannot be bypassed via API)

---

## 18. Built-In OMS

### Two Client Models Per Tenant

**Integration model** — platform as data hub. Client has their own systems. Data flows via adapters.

**Full platform model** — platform as system of record. Client has no external systems. ContactConnection handles everything end to end.

**Hybrid** — some built-in, some external. Configured per client via feature flags.

### Order Lifecycle

```
pending → processing → fulfilled → shipped → delivered → returned
```

### Order Record

Central document referencing call record, interaction record, customer record, payment, fulfillment, and returns with full audit trail of every state change.

### Payment Flow (Full Platform Model)

- Agent captures card data in ContactConnection CRM secure input
- Tokenized via payment gateway (Stripe primary) — raw card never written to database
- Token stored in order record
- Charge executed via gateway API on order confirmation
- Agent confirms total with caller before charge

### Fulfillment Flow

- Triggered automatically on order confirmation
- EasyPost (or configured fulfillment adapter) generates label
- Tracking number written to order record and call record
- Tracking webhook updates order status automatically

### Address Verification Integration

Verification happens inline as agent completes address entry:
- USPS Validation API called on field completion
- Verified/suggested/unverifiable result shown immediately
- Address flags populated automatically (IsPOBox, IsMilitary, IsCanada, IsForeign)
- Agent confirms standardized address — no manual flag setting

### Tax Calculation

TaxJar or Avalara called before agent quotes total to caller:
- Order items + origin address + destination address → tax amount
- Agent quotes complete total including tax
- Caller confirms, payment charged with correct total

---

## 19. Call Record — Single Record of Truth

### Overview

The call record is the single authoritative source for everything that happened on a call. From the moment the call arrives at FreeSWITCH to disconnect, through every post-call process, for the lifetime of the record. CDR data and CRM data live here together — never requiring reconciliation between separate systems.

### Schema

```sql
call_records (
  -- Identity
  id                      uuid primary key,
  tenant_id               uuid not null,
  client_id               uuid not null,
  campaign_id             uuid not null,
  agent_id                uuid,

  -- Call metadata
  source                  varchar,        -- inbound/outbound/callback
  record_type             varchar,        -- full/stub
  overall_status          varchar,        -- active/complete/incomplete

  -- Caller identity (relational — queried frequently)
  caller_id               varchar,
  account_number          varchar,
  first_name              varchar,
  last_name               varchar,
  email                   varchar,
  phone                   varchar,

  -- Timing (relational — aggregated constantly)
  call_start_at           timestamptz,
  call_end_at             timestamptz,
  handle_time_seconds     integer generated always as (
                            extract(epoch from (call_end_at - call_start_at))
                          ) stored,

  -- Financial summary (relational — reporting)
  total_amount            numeric(10,2),
  tax_amount              numeric(10,2),
  payment_status          varchar,

  -- Fulfillment summary (relational — operations)
  fulfillment_status      varchar,
  tracking_number         varchar,

  -- Telephony
  contact_id_external     varchar,
  recording_url           varchar,

  -- JSONB — variable/nested, not frequently queried by field
  addresses               jsonb,
  commitment_events       jsonb default '[]',
  flow_execution_state    jsonb default '{}',
  custom_fields           jsonb default '{}',   -- denormalized snapshot
  api_response_cache      jsonb default '{}',
  telephony_events        jsonb default '[]',

  -- Sensitive data lifecycle
  sensitive_data          jsonb,                -- encrypted, time-bounded
  sensitive_data_stored_at  timestamptz,
  sensitive_data_wiped_at   timestamptz,
  sensitive_wipe_reason     varchar,

  -- Audit
  created_at              timestamptz default now(),
  updated_at              timestamptz default now()
)
```

### Indexes

```sql
create index idx_call_records_tenant on call_records(tenant_id);
create index idx_call_records_campaign_date on call_records(campaign_id, call_start_at desc);
create index idx_call_records_agent_date on call_records(agent_id, call_start_at desc);
create index idx_call_records_caller on call_records(tenant_id, caller_id);
create index idx_call_records_account on call_records(tenant_id, account_number);
create index idx_call_records_commitment_events on call_records using gin(commitment_events);
create index idx_call_records_custom_fields on call_records using gin(custom_fields);
create index idx_call_records_active on call_records(tenant_id, agent_id) where overall_status = 'active';
```

---

## 20. Custom Fields — Type System

### Overview

Campaign-specific fields defined in the flow designer, stored with full type fidelity. The same concept as CRMPro's custom field system, made more structured and more powerful.

### Schema

```sql
data_types (
  id                    uuid primary key,
  type_name             varchar unique,     -- string, integer, decimal, currency, etc.
  clr_type              varchar,            -- System.String, System.Decimal, etc.
  postgres_type         varchar,            -- text, numeric, bigint, etc.
  validation_pattern    varchar,
  display_format        varchar,
  is_aggregatable       boolean,
  aggregation_functions jsonb               -- ["sum","avg","min","max","count"]
)

custom_field_definitions (
  id                    uuid primary key,
  tenant_id             uuid not null,
  client_id             uuid,               -- null = tenant-wide
  campaign_id           uuid,               -- null = client-wide
  field_name            varchar not null,
  display_label         varchar not null,
  data_type_id          uuid not null,
  is_required           boolean,
  validation_rules      jsonb,
  display_order         integer,
  is_active             boolean
)

custom_field_values (
  id                    uuid primary key,
  call_record_id        uuid not null,
  field_definition_id   uuid not null,
  -- Typed columns — one populated per row based on data_type
  value_string          text,
  value_integer         bigint,
  value_decimal         numeric(18,6),
  value_boolean         boolean,
  value_date            date,
  value_datetime        timestamptz,
  value_json            jsonb,              -- multiselect, complex types
  stored_at             timestamptz default now(),
  unique(call_record_id, field_definition_id)
)
```

### Design Notes

- Typed columns preserve type fidelity — `SUM(value_decimal)` works without casting
- `is_aggregatable` and `aggregation_functions` on data_type drive reporting widget builder — prevents meaningless aggregations
- Denormalized JSONB snapshot on call_record for fast display without joins on active records
- Scope hierarchy: campaign > client > tenant — most specific definition wins
- Adding a new campaign with unique field requirements is a database insert, not a schema migration

---

## 21. Flow State Immutability

### Overview

Once a critical action commits data to an external system, the data that action was based on becomes structurally immutable. Not just visually locked — rejected at the flow engine, API, and UI layers simultaneously.

### Commitment Events

Declared in the flow definition — not hardcoded:

```json
{
  "node_id": "node_submit_order",
  "type": "api_call",
  "on_success": {
    "transition": "node_confirmation",
    "commitment_events": [
      {
        "event": "order_submitted",
        "locks": [
          "call_record.cart",
          "call_record.addresses.billing",
          "call_record.addresses.shipping",
          "call_record.payment_token"
        ],
        "lock_label": "Order submitted — these fields are locked",
        "allows_supervisor_override": true,
        "override_requires_reason": true
      }
    ]
  }
}
```

### Three-Layer Enforcement

**Flow engine:** `TrySetValue` returns false for locked fields, logs the attempt  
**API layer:** Returns `409 Conflict` with locked field list and lock reason  
**UI layer:** Renders locked fields with clear visual indicator and lock reason — not just disabled

### Supervisor Override

```
Agent requests override
    ↓
Request sent to supervisor dashboard via SignalR
    ↓
Supervisor approves with mandatory reason
    ↓
Specific field unlocked for this session only
    ↓
Agent corrects the field
    ↓
Field re-locks immediately after edit
    ↓
Audit trail: original value, corrected value, supervisor, reason, timestamp
```

### Record State IS Lock State

Commitment events appended to call record are the lock registry. Flow relaunch applies existing commitment events before the agent sees anything. Deterministic — same record always produces same lock state.

---

## 22. Multi-Interaction Call Model

### Overview

A single call is a container. What happens inside that container can be multiple distinct, independently dispositioned interactions. Agents can handle multiple caller needs on one call without asking them to call back.

This eliminates:
- Double-counted talk time (a critical reporting integrity issue)
- Lost revenue from callers who don't call back for secondary needs
- Caller frustration from "please call back for that"
- Inaccurate AHT and utilization metrics

### Call Record / Interaction Relationship

```
call_records (the telephony container)
├── id, agent_id, caller_id
├── call_start_at, call_end_at
├── handle_time_seconds        ← one value, one source of truth
├── recording_url
└── interactions[]
    ├── Interaction 1 — order_sale — disposition: sale
    │   ├── flow_id, flow_version
    │   ├── cart_id → cart_001
    │   ├── commitment_events[]
    │   └── custom_fields
    └── Interaction 2 — subscription_change — disposition: modified
        ├── flow_id, flow_version
        ├── commitment_events[]
        └── custom_fields
```

### call_interactions Schema

```sql
call_interactions (
  id                    uuid primary key,
  call_record_id        uuid not null,
  interaction_number    integer,            -- sequence within call
  type                  varchar,            -- order_sale, account_change, etc.
  flow_id               uuid,
  flow_version          integer,
  disposition           varchar,
  flow_execution_state  jsonb,
  commitment_events     jsonb default '[]',
  custom_fields         jsonb default '{}',
  cart_id               uuid,
  started_at            timestamptz,
  completed_at          timestamptz,
  status                varchar             -- active/complete/incomplete
)
```

### Interaction Types

order_sale, lead_capture, account_change, subscription_change, customer_service, payment_update, return_request, information_only, outbound_follow_up, autoship_attempt

### Between Interactions

```json
{
  "node_id": "node_end_interaction",
  "type": "interaction_complete",
  "disposition_options": ["sale", "no_sale", "caller_declined"],
  "prompt": "Does the caller need anything else?",
  "additional_interaction_types": ["order_sale", "subscription_change"],
  "transitions": {
    "new_interaction": "launch_interaction_selector",
    "end_call": "node_wrap_up"
  }
}
```

Available additional interaction types filtered by client configuration.

### Call-Level Disposition

Derived automatically from interaction outcomes — not entered by agent:
- `all_sale` — every interaction resulted in a sale
- `mixed` — some sales, some no-sales
- `service_only` — no sales, service interactions only
- `no_sale` — no sales
- `incomplete` — call ended before all interactions completed

### Reporting Unlocked

- Interactions per call — average and distribution
- Cross-sell rate — sales interactions / calls with at least one sale
- Interaction type mix — what combinations appear together
- Per-interaction commission — accurate, no double-counting
- True handle time — one record, one value, impossible to inflate

---

## 23. Timestamp and Timezone Strategy

### The Rule

```
Store UTC
Transit UTC  
Display local
Document timezone explicitly on every surface
```

No exceptions. No special cases.

### Implementation

**Database:** All timestamps as `timestamptz`. PostgreSQL stores UTC internally.

**API:** All timestamps returned as ISO 8601 with explicit UTC indicator: `"2026-03-17T19:03:21Z"`

**Frontend:** Convert at render time using tenant/client/report configured timezone. Single utility function used everywhere.

**Exports:** Timezone is an explicit per-job configuration — not inherited from tenant.

**Reports:** Timezone is an explicit per-saved-report configuration — not dynamic from user context.

### Timezone Configuration Hierarchy

```
Platform Default (UTC)
    ↓ overridden by
Tenant Timezone (IANA name — e.g. 'America/Los_Angeles')
    ↓ overridden by
Client Report Timezone (e.g. 'America/New_York' for Cannella Media)
    ↓ overridden by
Saved Report Timezone (explicit, captured at save time)
    ↓ overridden by
Export Job Timezone (explicit per job)
```

### Always IANA Timezone Names

`America/New_York` — not `UTC-5` or `EST`. IANA names encode DST rules. `UTC-5` never adjusts for daylight saving. `America/New_York` always knows when to be UTC-4 and when to be UTC-5.

### Display Label

Every reporting surface carries a persistent timezone indicator:
```
All times displayed in Eastern Time (ET/UTC-4)
```

### Date Range Queries

Date selections converted to UTC before querying:
```csharp
// "Today" in tenant timezone → UTC range for query
var utcRange = ConvertDateToUtc(selectedDate, tenantTimezone);
// Tenant in LA selecting "March 17" queries 07:00Z to 06:59Z next day
```

### Export Timezone

Configured explicitly per export job. A tenant in Pacific time can deliver a file in Eastern time to a client who expects it. Common in DR media — Cannella Media and similar clients have specific timezone requirements in their file specifications.

---

## 24. PCI Compliance and Sensitive Data

### CVV / Security Code Lifecycle

PCI DSS prohibits storing the security code after authorization. ContactConnection implements time-bounded storage with guaranteed destruction:

```sql
call_records
├── sensitive_data              jsonb    -- AES-256 encrypted
├── sensitive_data_stored_at    timestamptz
├── sensitive_data_wiped_at     timestamptz
└── sensitive_wipe_reason       varchar  -- exported/api_processed
```

**Storage:** Encrypted immediately on capture. Flagged with stored_at timestamp.

**Export worker:** After confirmed export or API transmission, sensitive fields wiped in the same transaction as export confirmation. Cannot mark job complete without confirming wipe.

**Audit trail:** stored_at, wiped_at, wipe_reason — complete chain of custody for PCI audit.

### Tokenization

Payment card numbers stored as gateway tokens (Stripe/Braintree) — not encrypted card numbers. Raw card data never written to database. PCI scope dramatically reduced.

### Recording Masking

Automatic recording pause when agent interacts with payment fields — triggered by extension field focus detection. Timestamped audit trail of every mask event, duration masked, field, frame URL. Exportable compliance report per client per period.

### Encrypted Exports

Export files containing sensitive data (for clients requiring daily batched exports) encrypted at file level:
- WinZip AES encryption
- PGP encryption  
- Encryption key stored in credential store, never in job configuration

### Encrypted Credential Store

All client API keys encrypted at rest with platform master key (Azure Key Vault). Plaintext key lives only in memory during the API call that needs it. Never logged, never returned to frontend.

---

## 25. Infrastructure and Deployment

### Cloud Provider

**Azure** — recommended for .NET 8 stack. First-class .NET tooling, managed services for every component, excellent documentation.

### Production Architecture

```
Cloudflare (DNS, wildcard SSL, DDoS protection)
    ↓
Azure Front Door or Nginx reverse proxy
Subdomain tenant routing
    ↓
Azure App Service (auto-scaling)
ContactConnection API instances (stateless, N instances)
    ↓
├── Azure Database for PostgreSQL Flexible Server
│   Primary (writes) + Read replica (reporting)
│   Automated backups, point-in-time recovery
│
├── Azure Cache for Redis
│   Session state, SignalR backplane, flow execution cache
│
├── Azure SignalR Service
│   Agent real-time connections
│   Scales independently
│
├── Azure Service Bus
│   Export job queue, background task queue
│
├── Azure Blob Storage
│   Call recordings, export files
│   Tiered storage (hot → cool → archive)
│
└── Azure Container Instances
    ContactConnection Worker (background jobs)
    Scales to zero when not needed

FreeSWITCH (VPS or dedicated — SIP requirements)
    ESL connection to API
```

### Stateless API Requirement

API instances must be stateless. All shared state in Redis or PostgreSQL. Any instance handles any request from any tenant at any time. Load balancer distributes freely. New instance contributes capacity immediately on spin-up.

### Tiered Tenant Infrastructure

**Phase 1 — All tenants on shared infrastructure**
Logical isolation via PostgreSQL schemas. Single deployment.

**Phase 2 — Large/regulated tenants get dedicated database**
Shared application tier, dedicated PostgreSQL instance. Separate-schema design supports this migration without code changes.

**Phase 3 — Enterprise dedicated stacks**
Full infrastructure isolation. Premium contract pricing.

### Starting Cost Estimate

For first production deployment (beta call center):
- Azure App Service (B2/B3): ~$75-150/month
- Azure PostgreSQL Flexible Server: ~$50-100/month
- Azure Redis Cache: ~$15-30/month
- Azure SignalR Service: free tier initially
- Azure Blob Storage: ~$5-20/month (pay per GB)
- Azure Service Bus: ~$10/month
- **Total: ~$200-500/month**

Scales with usage. Bill grows with revenue — not ahead of it.

---

## 26. Local Development Environment

### Docker Desktop

The entire ContactConnection stack runs locally via Docker Desktop on Windows (WSL2 backend). `docker compose up` starts everything. `docker compose down` stops everything cleanly. Database persists via Docker volumes between sessions.

### Dev Machine Specs (Current)

- 32GB RAM — comfortable headroom for full stack + load testing
- 2TB NVMe M.2 — fast container startup, realistic PostgreSQL I/O performance
- Sufficient for 50-100 concurrent simulated agent sessions locally

### docker-compose.yml (Local Stack)

```yaml
services:
  api:
    build: ./src/ContactConnection.Api
    ports: ["5000:8080"]
    depends_on: [postgres, redis, freeswitch]

  worker:
    build: ./src/ContactConnection.Worker
    depends_on: [postgres, redis]

  postgres:
    image: postgres:16
    ports: ["5432:5432"]
    volumes: [postgres_data:/var/lib/postgresql/data]

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  freeswitch:
    image: signalwire/freeswitch:latest
    ports:
      - "5060:5060/udp"    # SIP
      - "8021:8021/tcp"    # ESL
      - "16384-16394:16384-16394/udp"  # RTP
    volumes: [./freeswitch/conf:/etc/freeswitch]

  proxy:
    image: nginx:alpine
    ports: ["80:80"]
    volumes: [./nginx/local.conf:/etc/nginx/conf.d/default.conf]

  pgadmin:
    image: dpage/pgadmin4
    ports: ["5050:80"]

  mailhog:
    image: mailhog/mailhog
    ports: ["8025:8025"]   # Web UI for captured email

volumes:
  postgres_data:
```

### Local Subdomain Routing

```
# Windows hosts file
127.0.0.1    hubion.local
127.0.0.1    tms.hubion.local
127.0.0.1    demo.hubion.local
```

### Development Workflow

Infrastructure (PostgreSQL, Redis, FreeSWITCH, Nginx) runs in Docker. Application code (.NET API, Worker) runs directly via `dotnet watch run` for hot reload during active development. Full containerized stack available when needed for integration testing.

### Local SIP Testing

Zoiper or MicroSIP softphone registers with `127.0.0.1:5060` for SIP call testing. SIPp Docker container for load testing — simulates inbound calls, queue scenarios, abandon events. No SIP trunk required, no cost during development.

### Load Testing

k6 + Grafana + Prometheus running in Docker for local load testing:
- 50-100 concurrent agent sessions
- 20-50 simultaneous SIP calls via SIPp
- Export worker under concurrent job load
- Multi-tenant isolation under stress

### CI/CD Pipeline

GitHub Actions builds Docker images on push to main, pushes to Azure Container Registry, Azure App Service pulls and deploys. Rolling deployment — zero downtime. Production receives identical containers to local test environment.

---

## 27. Commercial Model

### Pricing Philosophy

Every tenant gets every feature. No per-feature charges. No per-MB storage fees. No nickel-and-diming. Storage economics are the platform's operational responsibility — not the tenant's billing problem.

### Structure

```
Base tenant fee        Full platform access, every feature available
Per seat               Per active agent — scales with operation size
```

Nothing else.

### Feature Flags vs Licensing

Feature flags control what's enabled — not what's licensed. A tenant who doesn't need native telephony has it turned off. A tenant who needs it turns it on. The flag controls availability, not the bill. The bill is base fee plus seats.

### Storage

Tiered storage managed automatically:
- Hot: recent records, instant access
- Cool: older records, still accessible
- Archive: compliance retention, retrieval on demand

Client configures retention policy. Platform manages the mechanics. No per-GB invoice.

### Enterprise Dedicated Infrastructure

The one exception to flat pricing: tenants requiring dedicated infrastructure (contractual data isolation, regulatory requirements, very high volume) pay a premium tier that covers the operational overhead of dedicated servers. This is the exception, not the rule.

---

## 28. Competitive Position

### What No Other Platform Combines

| Capability | ContactConnection | NICE InContact | Five9 | Genesys |
|---|---|---|---|---|
| Call-flow-aware web automation | ✓ | ✗ | ✗ | ✗ |
| Agent annotation on external sites | ✓ | ✗ | ✗ | ✗ |
| Unified CDR + CRM record | ✓ | ✗ | ✗ | ✗ |
| Full decision tree call trace | ✓ | Partial | Partial | Partial |
| Honest full abandon family | ✓ | ✗ | ✗ | ✗ |
| Callback abandon as abandon | ✓ | ✗ | ✗ | ✗ |
| True parallel queuing | ✓ | ✗ | ✗ | ✗ |
| Multi-interaction per call | ✓ | ✗ | ✗ | ✗ |
| No-code flow ownership by ops | ✓ | Partial | Partial | Partial |
| All-inclusive pricing | ✓ | ✗ | ✗ | ✗ |
| No per-MB storage fees | ✓ | ✗ | ✗ | ✗ |

### Enterprise Platform Pricing

- NICE InContact: $100+ per agent per month
- Five9: $150+ per agent per month
- Genesys Cloud: $75-150+ per agent per month
- Plus per-feature add-ons, storage fees, professional services

### Infrastructure Cost Reduction Story

For call centers running Windows-based CRM clients:

- WinForms CRM → requires Windows workstations or VDIs
- ContactConnection web platform → runs in any browser

Results for a typical call center:
- Proxmox VDI stack → eliminated entirely
- Windows VDI licenses → eliminated
- Guacamole infrastructure → eliminated
- Windows workstation licenses → eliminated (Linux/Chromebook)
- Remote agent hardware provisioning → simplified or eliminated
- Power consumption → reduced (Chromebook: ~10W vs desktop: 65-150W)

The ROI conversation with a CFO: infrastructure savings often cover the ContactConnection subscription within the first quarter.

---

## 29. Planned Claude Code Sessions

Each session has specific inputs identified. Bring the listed materials to maximize session productivity.

### Session 1 — Foundation
**Goal:** Solution scaffold, security baseline, first working vertical slice  
**Inputs:** This ARCHITECTURE.md  
**Deliverables:**
- Solution structure created
- Tenant schema and provisioning
- Secrets management wired (Azure Key Vault local emulation)
- PostgreSQL connection with EF Core 8 and Npgsql
- First API endpoint returning call record data
- Docker compose stack running locally

### Session 2 — Script and Flow Analysis
**Goal:** JSON flow schema designed from real script data  
**Inputs:**
- Generated .vb files for representative scripts (one per archetype: linear sales, branching CS, CefSharp-heavy, complex tags, API integration)
- Corresponding .resources files
- Script designer source code
- Tag processor / DLL compilation logic
- Base class / shared code scripts inherit from  
**Process:** Analysis pass first (Claude summarizes patterns), then design translation pass (map patterns to JSON schema)  
**Deliverables:**
- Finalized JSON flow schema
- Complete node type taxonomy grounded in real script data
- Variable resolution engine design
- Migration parser design for existing scripts → JSON

### Session 3 — Flow Engine Implementation
**Goal:** Working server-side flow interpreter  
**Inputs:** This ARCHITECTURE.md, Session 2 deliverables  
**Deliverables:**
- FlowExecutionContext implementation
- Variable resolver service
- Node execution handlers for core node types
- SignalR push to agent UI
- Flow state persistence (Redis + PostgreSQL)
- Commitment event system

### Session 4 — Web Automation and Extension
**Goal:** Chrome extension with working automation  
**Inputs:**
- CefSharp automation code for complex multi-page branching flow
- Branching logic description and URL patterns
- Known timing sensitivities and fragile selectors
- Current error handling and recovery logic
- Syncro extension code (iframe + UI injection reference)  
**Deliverables:**
- Chrome extension manifest and project structure
- Background service worker
- Content scripts with framework detection
- Page readiness contract implementation
- waitForElement utility (exists + visible + enabled + delay)
- Annotation system
- Frame registry and cross-origin iframe handling
- Recording mask trigger

### Session 5 — Offer / Inventory / Cart Engine
**Goal:** Commerce engine designed from proven implementation  
**Inputs:**
- Existing offer/product/cart table structure from CRMPro
- Kit and variable kit implementation code
- Price break calculation logic (quantity and mix-and-match)
- Commission attribution when breaks apply
- Real examples of complex orders  
**Deliverables:**
- Product / offer / cart schema design
- Price resolution service
- Commission attribution model
- Inventory control integration

### Session 6 — API Builder
**Goal:** No-code adapter builder with generic runtime  
**Inputs:**
- Existing CRMPro API builder implementation
- Example complex API definition (dynamic URL, custom headers, processed response)
- Current tag syntax for API builder  
**Deliverables:**
- Adapter definition schema
- Generic adapter runtime
- API builder UI components
- Test execution implementation

### Session 7 — FreeSWITCH and Telephony
**Goal:** ESL integration and parallel queue engine  
**Inputs:** This ARCHITECTURE.md, FreeSWITCH ESL documentation  
**Deliverables:**
- ESL client service
- Call lifecycle event handling
- Screen pop bridge (telephony → CRM flow)
- Parallel queue manager with atomic claim
- Commission outcome recording
- Inbound telephony event API endpoints

### Session 8 — Reporting and Dashboards
**Goal:** Widget-based reporting with honest metrics  
**Inputs:** This ARCHITECTURE.md  
**Deliverables:**
- Dashboard widget system
- Abandon family taxonomy queries
- True contact rate calculation
- Callback lifecycle tracking
- Call trace view
- Client portal reporting surface
- Custom field aggregation with type system

---

## Document Maintenance

This document is the authoritative architectural reference for all ContactConnection development. It should be updated when:

- A significant architectural decision is made or changed
- A new major component is designed
- A technology choice is finalized or changed
- A new Claude Code session reveals something that updates the design

Every Claude Code session should begin with: *"Please read ARCHITECTURE.md before we start."*

---

*Call Center Solutions, LLC — Confidential*  
*ContactConnection Platform Architecture v1.0*  
*All platform IP owned by Call Center Solutions, LLC*
