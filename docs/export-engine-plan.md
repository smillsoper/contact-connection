# Export Worker — plan + the Cannella layouts it must reproduce (S180, Sprint 2 item 2)

## Design (agreed in principle with Stephen, S180)

- **Export Definition** (tenant, versioned like API definitions): data (calls filtered by client / campaign / disposition /
  media agency; production only; one row per call | interaction | order line), layout, schedule, data window, delivery.
- **Layout modes**
  - **Columns**: header + one Liquid expression per column; the format setting does delimiters / quoting / escaping / fixed
    width / Excel (.xlsx).
  - **Document**: one Liquid template over the result set (`{% for call in calls %}`), with header/trailer lines, counts and
    totals. Export filters: `pad_left` / `pad_right` (fixed width), `csv`, `xml`. For rigid vendor formats (Cannella).
  - Letters / PDF: later, when a concrete need exists.
  - Same data model as the API Liquid bodies (`call_record`, `media`, `cart`, `order`, `custom_fields`, `agent`…); Preview
    renders against real recent calls.
- **Schedule vs data window** are separate settings, each with its own time zone; the UI states the next run in plain English.
  Windows: previous day (in a chosen tz), last N hours, previous week/month, since the last successful run.
- **Engine (Worker)**: a DB-backed run queue (one run per definition + window, claimed with `FOR UPDATE SKIP LOCKED`),
  max 5 concurrent, retries; streams pages to a file; run history keeps the file N days (re-deliver); **re-run a past window**
  with current data (agencies send changes late); Run now.
- **Delivery** (pluggable): SFTP (SSH.NET; password or key — we generate the pair), FTPS (FluentFTP), email, download; later
  S3 / Box / Drive / SharePoint. Encryption: PGP (PgpCore) to the recipient's public key, AES zip. Credentials in the tenant
  store. (WinSCP is Windows-only — not used; hosting is Linux containers.)
- **Lifecycle + test files (Stephen, S180)**: Draft → Testing → Approved → Live. Vendors (Cannella and others) approve a
  test file before go-live, sometimes before they release their delivery method. **Generate test file** at any stage:
  from a chosen date range of production calls, or from practice runs (training / sandbox) when there's no production data
  yet; download, or deliver to a target once one exists. Marked as test (optional `_TEST` filename suffix; `export.is_test`
  in templates); never counts as a run (no effect on "since last successful run"). Vendor approval recorded (who, when,
  note) in the definition history; the schedule only runs when Live.
- **Guards**: no card data in the model; production calls only; definitions versioned; runs and downloads audited.

## Build order

1. **DONE S180 (ca72eb7):** definitions + lifecycle, Columns + Document modes, CSV / fixed width / Excel, Preview, test
   files, Run now, run history + download, the queue.
2. **DONE S180:** schedules (daily / weekdays / monthly, own time zone) + windows (previous day / week / month, last N
   hours, since last run — in the export's time zone), scheduler with catch-up (max 7) and a unique (definition,
   scheduled_for) guard; delivery targets SFTP (pinned host key, password or key) / FTPS (valid or pinned certificate) /
   email, each optionally PGP- or AES-zip-encrypted; a delivery queue with growing-backoff retries and Retry; test files
   sent only on request, real files only once approved; retention (90 days, the approved test file kept forever);
   activity audit (downloads, sends, failures, expiry); Cannella LF + SF starter templates (true Eastern; O/R flag from
   `export.is_rerun`; Cannella SKUs from offer flags `line.flags[...]`).
3. **DONE S180:** vendor keys — generate an SSH key pair (RSA-4096; PKCS#1 private key in the credential store, OpenSSH
   public key + SHA256 fingerprint shown; an SFTP target signs in with it — verified against a key-only SFTP server) or
   a PGP key pair (private key + random passphrase in the credential store; public key for vendors to encrypt to us).
   Revoke deletes the private key (refused while a delivery target uses it). Inbound file ingestion itself is later.
4. Later: plain FTP and cloud connectors (S3 / Box / Drive / SharePoint) when a vendor needs one.

**Local dev note:** the Worker sends deliveries, so it needs the tenant credential store (Key Vault settings) and the
email key in its own User Secrets; production reads both from Key Vault.

## CRMPro reference: Life Seasons' Cannella exports (from the CRMPro dump, layout only — no credentials read)

Both active, daily at **9:30 PM Pacific**, `UpToMins = 30` (calls up to 9:00 PM Pacific = midnight Eastern), so each file is
effectively **the previous Eastern day**. Delivered by an on-complete script using SFTP/FTPS (credentials: Clint supplies;
never taken from the dump). Separate files, separate destinations.

### Shared filter

- Media company = **Cannella** (TFN media assignment), LF vs SF chosen by the call's `SF` flag (true = SF).
- **Junk calls excluded** (call type starts with "Junk").
- CRMPro shifts times by **+3 h** (Pacific server → Eastern). We'll format in `America/New_York` instead (handles DST cleanly).

### LF — `DAT_TEMS_LIFESEASONS_0_<yyyyMMddHHmmss>.csv` (CSV, quoted strings, no header line)

Sorted by call date. Per call, several rows sharing these columns (`[x]` = varies per row):

| # | Column | Value |
|---|---|---|
| 1 | Telemarketer code | `"TEMS"` |
| 2 | Call-center code | `"TEMS"` |
| 3 | Client code | `"13156"` |
| 4 | Transaction ID | `"<order number>"` |
| 5 | Transaction date | `MM/dd/yyyy` (Eastern) |
| 6 | Transaction time | `HH:mm:ss` (Eastern) |
| 7 | Record type | `"CALL"` / `"ORDER"` / `"UPSELL"` / `"REVENUE"` |
| 8 | Record detail | `""`, or the Cannella SKU on UPSELL rows |
| 9 | Value | `1`; UPSELL = quantity; REVENUE = cart total − sales tax (`#0.00`) |
| 10 | Call status | `"Valid"` on the CALL row, else `""` |
| 11 | Toll-free number | `"<DNIS>"` or `"UNASSIGNED"` |
| 12 | Station code | `"<station>"` or `"UNASSIGNED"` |
| 13 | Product code | `"<PRODUCTCODE media field>"` |
| 14 | Area code | first 3 of ANI |
| 15 | Script version | `""` |
| 16 | Billing ZIP | `"<zip>"` ("NOZIP" → blank) |
| 17 | Call type | CALL row only: `"Order"` / `"Customer Service"` / `"Inquiry"` (else `""`) |
| 18 | Caller ANI | ANI |
| 19–23 | Billing address 1 / 2 / city / state / ZIP+4 | quoted |

Rows per call: **CALL** always; for orders: **ORDER**, then **UPSELL** per cart line with a Cannella SKU (from the offer
flags "Cannella Order SKU" / "Cannella Upsell SKU", else a SKU → Cannella-code map), then **REVENUE**.

**CRMPro bug, not reproduced:** the LF script adds +3 h to the call date and then +3 h again when writing date and time
(= Pacific + 6 h). **Decision (Stephen, S180): LF goes out in true Eastern time, like SF.**

### SF — `NERQ_TMS_<MMddyy>.txt` (fixed width, CORE)

One master record per call, repeated per response code:

`TMSS` + `O` (O = original, R = re-run) + 8 blanks (telemarketing product) + 4 blanks (agency) + `TV` + `LIFE` + 4 blanks
(product) + 4 blanks (campaign) + **access code** (4, padded) + **station** (12) + **date** `yyyyMMdd` + **time** `HHmm`
(Eastern) + **response code** (4) + **counter** (6) + **dialed TFN** (10) + **ZIP** (5; caller ZIP else billing) +
**area code** (3; billing phone else ANI) + 6 blanks (DNIS code) + 15 blanks (custom client code).

Response codes per call: **`VCAL`** `000001` (every call), **`CALL`** `000001` (non-junk), **`ORD `** `000001` (inbound
orders), **`GREV`** cart total as `000000` (inbound orders).
