# CRMPro script → ContactConnection CRM flow

Tools used in Session 164 to convert the Life Seasons **NeuroQ - V1** CRMPro script into a draft CRM
flow (`docs/integrations/neuroq-v1-crm-script.md`). Kept for the other scripts (NeuroQ - SF TV,
My Best Heart, JF - Healthy Aging). Contain no CRMPro source or credentials — point them at a local
copy of the script files, and delete those copies when done.

| Script | Does |
|---|---|
| `resparse.py FILE.DESIGNER.RESOURCES --dump res` | Reads the binary .NET resources: every control's `ScriptBox` RTF, the offer catalog, web-service configs (structure only — the web-service blobs can hold credentials; don't copy them anywhere) |
| `designer.py FILE.DESIGNER.VB` | Control tree per tab (positions, labels, hidden/disabled) |
| `rtf2html.py` | RichEdit RTF → HTML (bold/italic/underline, colours, highlight); `<* … *>` VB tags returned as `[[CODEn]]` placeholders |
| `gen_v1.py` | The V1-specific generator: catalog seed, custom fields, the flow (verbatim script text, code tags → `{{variables}}` or per-case nodes); fails loudly on any unmapped code tag |
| `layout.py in.json out.json` | Layered auto-layout for any CRM/telephony flow: top-to-bottom ranks, crossing reduction, long edges routed through `_waypoints`, jump-backs up the left margin |
| `render.py flow.json out.png [scale]` | PNG preview of a layout + crossing count (Pillow) |

Finding a script's files: CRMPro DB `Scripts` (by `Client_ID` from `Clients`) → `ScriptFiles` is a
base64 BinaryFormatter `List(Of String)` of file names → `X:\Backup_Data\CRMPro_Scripts`. The DB dump
is at `C:\Users\Stephen\Documents\CRMPro_DB\CRMPro.sql` (pg_restore ≥ 18; restore only the tables
needed into a throwaway `postgres:18` container, then remove it with `docker rm -f -v`).

Once a generated flow is edited in the designer, the database copy is the source of truth — don't
re-run a generator over it.
