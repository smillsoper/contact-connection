# Life Seasons — Legacy CRMPro Integration Reference

Recovered in Session 162 (2026-09-26) from the TMS call center's CRMPro production backup, to
serve as the contract reference for rebuilding Life Seasons' **Order API** submission and
**Avalara AvaTax** sales-tax calculation in ContactConnection.

> **Credentials are deliberately excluded.** The backup contained live keys (Order API function
> key, Avalara account/license key, per-campaign Authorize.Net login/transaction keys). They are
> presumed revoked and must not be used. Real values come from Life Seasons when testing is
> scheduled.

Only the **Life Seasons Order API** path below was in use at shutdown. OrderLogix ("OLX") and
Konnective code still present in the scripts is from an earlier era and is dead code.

---

## Sources

| Source | What it holds |
|---|---|
| `X:\Backup_Data\CRMPro_DB\CRMPro.sql` | pg_dump **custom-format** archive (not plain SQL). Source PG 15.2, dumped by pg_dump 18.3 → requires `pg_restore` ≥ 18. `CRMProTraining.sql` alongside it is the training DB. |
| `CC_Processors` table | Base for **all** CRMPro API integrations (misnamed — not just card processors). `WebService` = base64 text of a .NET BinaryFormatter `clsWebService` object: URL, HTTP method, headers, and embedded VB.NET for building the request body and post-processing the response. `ProcessorFieldNames` = the per-client setting names. |
| `ClientProcessors` table | Per-client integration assignments + setting values (`ProcessorFields`, same serialization). |
| `GlobalClasses` table | Shared VB.NET class library (`Code` blob) — namespaces `LifeSeasons` and `AvalaraTaxClass` define the wire DTOs. |
| `X:\backup_data\crmpro_scripts\SCRIPT_PROD_NEURO Q TV SCRIPT NEW.vb` | NeuroQ campaign's main agent script — shows when/how the integrations were invoked. |

Integrations mapped to client **Life Seasons, LLC**: `Life Seasons - Order`, `Avalara Tax`,
`USPS`, `Dial800 New`, `Advantone - Caller Info`, `DialTower - Caller Info`.

---

## 1. Life Seasons Order API

| | |
|---|---|
| Production | `POST https://vendor.lifeseasons.com/api/v1/addorder` |
| Staging | `POST https://vendorfuncapi-staging.azurewebsites.net/api/v1/addorder` |
| Content-Type | `application/json` |
| Auth | Header `x-functions-key: <API Key>` (Azure Functions key) |
| Per-client settings | `API Key`, `Vendor Number` |

### Request body

Serialized with `System.Text.Json`, `WhenWritingNull` ignored. **Property-name casing is mixed and
must be reproduced exactly** — most fields are snake_case, but `products[]` items, `payment_info`
fields, `CustomMeta` and `additional_fees_or_taxes[]` items are PascalCase.

```jsonc
{
  "vendor_number": "…",              // per-client setting
  "customer_number": "LIFSEA-10000123", // = order_number (both from the order-number sequence)
  "order_number": "LIFSEA-10000123",
  "order_date_utc": "2024-03-01T18:22:10Z", // call date, UTC, yyyy-MM-ddTHH:mm:ssZ
  "order_hold_date_utc": null,       // defined in the DTO, never populated
  "dnis": "8001234567",              // call DNIS, overridable by an "Override DNIS" field
  "voice_recording_id": "…",         // telephony ContactID
  "customer_info": { "email", "first_name", "last_name", "phone" }, // phone = billing phone
  "billing_info":  { "email", "first_name", "last_name", "company",
                     "address_1", "address_2", "city", "state", "post_code", "country_code" },
  "shipping_info": { "phone", "first_name", "last_name", "company",
                     "address_1", "address_2", "city", "state", "post_code", "country_code" },
  "products": [
    { "ProductCode": "283-1-CTY-P10-SN", "Quantity": 1, "Price": 49.95, "Discount": 0, "Tax": 3.12 }
  ],
  "coupon_code": "",
  "shipping_method": "REG",          // REG default, RUSH for rush
  "use_shipping_cost": true,
  "shipping_cost": 0.00,
  "subtotal": 49.95,                 // Σ(Price × Quantity + Discount)
  "taxes": 3.12,                     // cart sales tax (2dp) MINUS the Retail Delivery Fee amount
  "additional_fees_or_taxes": [      // DTO initializes to [], so it serializes as [] when empty
    { "Description": "Shipping Tax",        "Amount": 0.00 },
    { "Description": "Retail Delivery Fee", "Amount": 0.28 }
  ],
  "total": 53.07,                    // cart total (2dp)
  "payment_info": {
    "PaymentMethod": "CC",
    "PaymentType": "auth",           // "auth" = auth-only, "sale" = auth+capture
    "PaymentTransactionId": "…",     // Authorize.Net transaction id
    "PaymentAmount": 53.07
  },
  "CustomMeta": {                    // omitted entirely when none of its inputs are present
    "mailin_keycode": "…",
    "ls_consented_to_marketing_texts": true,
    "ls_consented_to_transactional_texts": true,
    "ls_datetime_consented_to_marketing_texts": "…Z",
    "ls_datetime_consented_to_transactional_texts": "…Z", // same timestamp as marketing
    "ls_referrer_firstname": "…",
    "ls_referrer_lastname": "…",
    "ls_referrer_phone": "…"
  }
}
```

Field notes:

- `country_code`: code sends `"US"` / `"CA"` (from the address's IsCanada flag). The DTO comment
  says "USA or CAN" and a commented-out variant used `"001"`/`"034"` — `US`/`CA` is what shipped.
- Email only on `billing_info`; phone only on `shipping_info` (per DTO comments).
- `address_1`/`address_2` are prefix-formatted (`FormatAddress1/2` combining street prefix + line).
- Cart items flagged `ReportingOnly` are excluded from `products`.
- A cart item with SKU `RETAIL DELIVERY FEE` (case-insensitive) is **not** sent as a product — its
  `Prod_Tax` value is sent as a `Retail Delivery Fee` entry in `additional_fees_or_taxes` and
  subtracted from `taxes`.
- Per-product `Tax` = that line's Avalara `taxCalculated` (stored on the cart item as flag
  `Prod_Tax`). Per-product `Discount` came from an `OLX Discount` flag (OrderLogix-era naming).
- `Shipping Tax` fee entry is added whenever a `Shipping Tax` decimal field exists on the call
  (it's always set, possibly to 0, during tax calculation).

### Response

```jsonc
{
  "success": true,
  "message": "…",
  "receipt": { "messageId", "insertionTime", "expirationTime", "popReceipt", "timeNextVisible" },
  "hasValue": true,
  "errors": ["…"]
}
```

`receipt` is an Azure Storage Queue receipt — the API enqueues the order rather than processing it
synchronously. Success = `success == true`. On failure (or non-JSON response), `message` (or the
raw body) was saved to the call field `Life Seasons Order API Failure Message` and shown to the
agent. Raw request/response were saved as `Order API Request` / `Life Seasons Order API Response`.

### Order number

`LIFSEA-` + 8-digit per-client incrementing counter (production counter started at `10000000`).
Generated once per call at auth time and reused as both `order_number` and `customer_number`
(and as the Authorize.Net invoice/PO reference).

### Submission rules (NeuroQ script `SubmitLifeSeasonsOrder`)

1. Idempotent per call — skipped if the `Order Submitted` flag is already true.
2. Training mode: no post, simulated success. Simulator mode: posts (to staging).
3. On success sets `Order Submitted = true`.

---

## 2. Avalara AvaTax

| | |
|---|---|
| Endpoint | `POST https://rest.avatax.com/api/v2/transactions/create` |
| Auth | `Authorization: Basic base64(<Avalara Account>:<Avalara API Key>)` |
| Extra header | `X-Avalara-Client: CRMPro;8.12;;<machine name>` (client identifier — use our own) |
| Per-client settings | `Avalara Account`, `Avalara API Key` |

### Request (`Avalara_Tax_Request`, DataContract JSON, defaults omitted)

```jsonc
{
  "lines": [
    { "number": "1", "quantity": 1, "amount": 59.95,   // amount = quantity × unit price
      "taxCode": "PF050714", "itemCode": "283-2-CTY-P2-ORIG", "description": "Neuro-Q Autoship" },
    { "number": "2", "quantity": 1, "amount": 0.00,
      "taxCode": "FR020200", "itemCode": "REG", "description": "Shipping" }, // itemCode = ship method code
    { "number": "3", "quantity": 1, "amount": -5.00,
      "itemCode": "DISCOUNT", "description": "Discount" }                    // only if cart discount > 0
  ],
  "type": "SalesOrder",          // quote only
  "companyCode": "…",            // optional; NeuroQ did not set it (account default)
  "date": "2024-03-01",          // yyyy-MM-dd
  "customerCode": "TMS",
  "addresses": {
    "shipFrom": { "line1", "line2", "city", "region", "country", "postalCode" },
    "shipTo":   { "line1", "line2", "city", "region", "country", "postalCode" }
  },
  "commit": false,
  "currencyCode": "USD",
  "description": "TMS Phone Order"
}
```

- Item `taxCode` can be overridden per offer via an `OverrideTaxCode` offer flag; otherwise the
  campaign's product tax code applies.
- `ReportingOnly` cart items are excluded.

### NeuroQ campaign values (non-secret)

| Setting | Value |
|---|---|
| Product tax code | `PF050714` |
| Shipping tax code | `FR020200` |
| Ship-from | 565 N Kays Dr, Kaysville, UT 84037, US |
| Company code | not set |

### Response (fields consumed)

```jsonc
{
  "totalTax": 3.12,
  "summary": [ { "rate": 0.0475 }, … ],   // Σ rate = overall tax rate
  "lines": [
    { "itemCode": "…", "taxCalculated": 2.85,
      "details": [ { "jurisName", "tax", "rate", "taxCalculated" } ] }
  ],
  "error": { "code", "message", "details": [ { "code", "message", "description", "faultCode", "helpLink" } ] }
}
```

### How the result was applied

Run on the offer/cart step, whenever the shipping ZIP is known:

1. Reset every cart line's sales tax and the call's `Shipping Tax` to 0.
2. If ship-to state is **CO**, add the `Retail Delivery Fee` offer to the cart (remove it otherwise)
   — Avalara then returns the Colorado RDF as that line's tax.
3. Call Avalara. If `totalTax > 0`, for each response line:
   - matching cart item (by SKU = `itemCode`): `SalesTax = taxCalculated`, flags `Prod_Tax`
     (2dp) and `Tax Rate` (Σ detail rates, 6dp);
   - line whose `itemCode` = ship method code: call fields `Shipping Tax` and `Shipping Tax Rate`.
4. Cart `SalesTax = totalTax`; call field `Tax Rate` = Σ summary rates.
5. On `error`, the agent was shown the message and the call continued with zero tax.

---

## 3. Authorize.Net auth-only (embedded in the script, not `CC_Processors`)

NeuroQ did **not** use the `Authorize.Net` entry in `CC_Processors`. The script carried its own
embedded `WebServiceControl` (`wsAuthNet`, serialized in
`SCRIPT_PROD_NEURO Q TV SCRIPT NEW.designer.resources` as `wsAuthNet.WebService`).

- URL: `apitest.authorize.net` unless call field `AuthNetMode = "LIVE"`, then `api.authorize.net`
  (`/xml/v1/request.api`). Training/simulator → TEST.
- Credentials were set into call fields `APILogin`/`TransactionKey` (encrypted) by `DoAuthNet`
  just before the call and removed from the call data right after. Credentials were per-campaign
  and switched by call date (merchant account changed 2024-01-10).
- Request body was a string template (`createTransactionRequest`, `authOnlyTransaction`) filled
  with:
  - `amount` = cart total; `payment.creditCard` (`expirationDate` as `yyyy-MM`);
  - `lineItems` (non-`ReportingOnly` items: `itemId`/`name` = SKU truncated to 31,
    `description` ≤ 255, `quantity`, `unitPrice` = first installment amount);
  - `tax` (`amount` = cart sales tax, name/description = `"<ST> Sales Tax"`), `duty` zeros,
    `shipping` (amount, name = ship method code ≤ 31, description ≤ 255);
  - **`refId` and `poNumber` = call field `RefOrderNum`**;
  - `billTo`/`shipTo` full name/company/address (line 1 + line 2 joined)/city/state/zip/country;
  - `userFields` from any call field named `AuthNetUserField_*` (value `"name|value"`).
- A masked copy of the request (PAN → last 4, exp → `****-**`, CVV → `***`) was saved to the call
  as `AuthNet_RequestLog`; the raw response as `AuthNetResponse`.
- Approved = `transactionResponse.responseCode` `1` (approved) **or `4` (held for review)**.
  Max 3 auth attempts per call. Saved `Auth_TransID`, `Auth_AuthCode`, `Auth_AVSResultCode`,
  `Auth_CVVResultCode`, `Auth_CAVVResultCode`, `Auth_RefTransID`, `Auth_ResponseCode`, `Auth_RefID`.

### Latent legacy bug — order number likely never reached Authorize.Net

`DoAuthNet` generates the `LIFSEA-########` number and stores it as call field **`AuthOrderNum`**,
but the embedded control reads **`RefOrderNum`** — a field the NeuroQ script never sets. Unless
the CRMPro runtime populated `RefOrderNum` itself, `refId`/`poNumber` went out empty. The Order API
used `AuthOrderNum` correctly.

### Duplicate-transaction handling (for our client)

Authorize.Net rejects a transaction as a duplicate (response reason code 11) when an identical
one arrives within the `duplicateWindow` (default 120 s). Per Authorize.Net's documentation the
comparison uses the card/amount plus fields such as **`order.invoiceNumber`**, customer id and
bill-to/ship-to details. **`refId` and `poNumber` are not part of it**, so the legacy approach
would not have prevented duplicate responses even with the field-name bug fixed. To tie auths to
the order and separate distinct orders, send the order number as `order.invoiceNumber` (max 20
chars — `LIFSEA-10000000` is 15), and optionally also as `refId`/`poNumber`. Authorize.Net's JSON
API is XML-schema-backed, so **element order inside `transactionRequest` matters**: `order` goes
after `payment` and before `lineItems`/`tax`/`shipping`/`poNumber`/`customer`/`billTo`. Verify the
duplicate behavior against the sandbox before relying on it.

---

## 4. Mapping to ContactConnection

| Legacy | ContactConnection |
|---|---|
| Authorize.Net auth-only, `Auth_TransID` | `authorize_payment` node → `{{flow.<out>.gatewayTransactionId}}` (Session 161) |
| Avalara Tax integration | new `"avalara"` `ITaxProvider` (`CartDocument.TaxProvider = "avalara"`), returning per-line tax + `JurisdictionTax` details |
| `Prod_Tax` / `Shipping Tax` | `CartItem.SalesTax` (per line) + `CartDocument.ShippingTax` |
| Retail Delivery Fee cart item + `OverrideTaxCode` flag | **No placeholder cart item.** Campaign Avalara setting `feeLines: [{state:"CO", taxCode:"OF400000", description:"Colorado Retail Delivery Fee", code:"CO_RDF"}]` — the provider adds the line itself when ship-to is CO; its "tax" comes back as a `CartFee` in `CartDocument.Fees` (not `SalesTax`), included in `CartTotal`. Order API: send each fee in `additional_fees_or_taxes`; `taxes` = `SalesTax` (fees already excluded). Flat-rate campaigns get the same via a per-state fee. |
| offer flag `OverrideTaxCode` | `Product.TaxCode`, overridable by `Offer.TaxCode`; snapshotted as `CartItem.TaxCode` |
| `set_variable` / "ship to billing?" | `{{call_record.shipping_address}}` = `{{flow.billing_address}}` saves to the call record |
| Life Seasons - Order integration | Order API submission step (API Definition or dedicated node), idempotent per call record |
| `LIFSEA-########` counter | per-client order-number sequence |
| per-client settings | tenant credential store, campaign → client → tenant cascade |
| Training/simulator modes | flow preview / sandbox configuration |
