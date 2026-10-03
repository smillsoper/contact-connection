# Production Hosting Plan (draft, S174, 2026-10-03)

Goal: run the Life Seasons pilot in the cloud, off the home connection. The target load is ~246k inbound minutes a month and **104 peak
concurrent calls**, with up to 76 agents a day.

## Choice: Azure, two VMs + managed PostgreSQL

Why Azure: the platform already uses Key Vault, Entra ID and Managed Identity (`AzureCredentialFactory`),
so secrets and portal sign-in work without changes. Region: **West US 2**, close to Oregon agents and to SignalWire's west edge.

Why VMs and not App Service/containers: FreeSWITCH needs a **static public IP, host networking and a wide UDP
range**, which only a VM gives cleanly. The rest of the stack already runs under Docker Compose, so production
reuses the same compose setup with prod settings (no dev rebuild loop, no pgAdmin/MailHog).

| # | Resource | Runs | Size | ~$/mo (pay-as-you-go) |
|---|---|---|---|---|
| 1 | **Telephony VM** | FreeSWITCH, coturn (TURN) | D4as_v5: 4 vCPU, 16 GB, 128 GB disk | $126 + $20 disk |
| 2 | **App VM** | API, Worker, web (built static files), Redis, cloudflared | D4as_v5: 4 vCPU, 16 GB, 128 GB disk | $126 + $20 disk |
| 3 | **Azure Database for PostgreSQL** (Flexible) | all tenant schemas | General Purpose D2ds: 2 vCore, 128 GB, 7-day backups included | $145 |
| 4 | Static public IP (Standard) | Telephony VM | | $4 |
| 5 | Blob Storage (cool tier) | recordings (compressed) | ~60 GB/mo added, ~700 GB after a year | $5 → $15 |
| 6 | Egress bandwidth | call audio to SignalWire and agents | ~370 GB/mo (first 100 GB free) | $25 |
| 7 | VM backup + monitoring (Log Analytics, alerts) | | | $30 |
| 8 | Key Vault, container registry (or free GitHub registry) | | | $5 |
| | **Total** | | | **≈ $510/mo** |

A second-opinion estimate (S174) came to **$515–545/mo**. It used Intel D4s/D4ds_v5 VMs (~$140 each, vs ~$126 for the AMD
D4as_v5) and a memory-optimized E2ds_v5 database (~$175–195). The AMD VMs are the cheaper equivalent. Upgrade the database
to E-series only if monitoring shows memory pressure. **Budget $510–545; make the final quote in the Azure Pricing
Calculator (West US 2) before signing.**

- A **1-year reservation** on both VMs and the database cuts about 35%, to roughly **$370/mo**. Do it once the contract is signed.
- The worksheet's $600 hosting line is covered, with room left.

## Sizing reasoning

- **CPU:** each call has a caller leg (G.711 from SignalWire) and an agent leg (Opus over WebRTC), so FreeSWITCH
  transcodes every call and writes a stereo recording. At 104 calls that is about 1.5–2 cores of work. A 4-vCPU VM leaves 2× headroom.
  Confirm with the load test below.
- **Bandwidth:** ~200 kbps each way per call (two legs) → **~21 Mbps each way at peak**. Trivial for an Azure NIC.
- **RTP ports:** each call uses a port pair per leg, about 420 ports at peak. Use FreeSWITCH's default range
  **16384–32768/UDP**, far more than needed. The home range (16384–16393) stays dev-only.
- **App VM:** the API and Worker are light. Redis holds live flow state, so it runs here with persistence (AOF) on.

## Network / firewall (Azure NSG)

| Open to the internet | Port | Why |
|---|---|---|
| Telephony VM | 5061/TCP (TLS SIP), **restricted to SignalWire's IP ranges** | trunk |
| Telephony VM | 16384–32768/UDP | RTP media (callers + agents) |
| Telephony VM | 3478/UDP+TCP + coturn relay range | TURN for remote agents |
| Telephony VM | 7443/TCP | WSS softphone signaling (or route through the tunnel as today) |
| App VM | **nothing**: web/API go out through the Cloudflare tunnel | |

**Never public:** 5432 (Postgres, private endpoint only), 6379 (Redis), **8021 (ESL), reachable only from the App VM over
the private VNet**, pgAdmin, MailHog. Admin access goes through Azure Bastion or a VPN, not open SSH.

## Code/config changes before go-live

1. **Recordings → compressed + Blob:** after each call, transcode the stereo WAV to MP3 with ffmpeg (already in the stack), upload it to
   Blob, then delete the local file only after the upload is confirmed. Playback streams from Blob.
2. FreeSWITCH: set the RTP range above and the external IP to the static IP (`ext-rtp-ip` / `ext-sip-ip`). Copy the TLS certs to the VM (not
   in git).
3. Production `appsettings` + `KeyVault:VaultUri`; move the secrets in CLAUDE.md's Key Vault table into the vault.
4. Compose split: `docker-compose.telephony.yml` (VM 1) and `docker-compose.app.yml` (VM 2); drop Postgres, pgAdmin and MailHog in prod.
5. SignalWire: point the SIP endpoint and numbers at the static IP; keep the tunnel hostnames for web/API.

## Go-live checks

- **Load test:** SIPp from a third, temporary VM, ramping to **120 concurrent calls** with recording on. Watch
  CPU, audio quality (MOS/jitter) and the dashboard. Delete the VM after (~$2).
- A real-agent test from outside the office (TURN path) and from the office network.
- A backup restore test: restore the database to a scratch server once.
- Alerts: VM CPU/disk, FreeSWITCH down, SignalWire balance (checklist item), Postgres storage.

## Known risk (pilot)

There is one telephony VM, so a VM failure drops live calls until it restarts (usually minutes). That is acceptable for the pilot. The
upgrade path is a warm-standby FreeSWITCH VM plus zone-redundant Postgres, about +$300/mo, priced into later tenants.
Write a short rebuild runbook so a fresh VM can be stood up from compose + Key Vault in under an hour.

## Setup-fee items from this plan

- Load-test VM: ~$2.
- First month of hosting before revenue: ~$510.
- Domain/TLS: already owned.
- Time to build the compose split and the recording-to-Blob change.
