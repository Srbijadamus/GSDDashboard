# PROJECT_DOCUMENTATION_INDEX

**Prepared 2026-10-11.** This index lists every document a reader needs, tells you which repository it
lives in, and states which documents are **superseded** so that nobody relies on an outdated file.

**Read in this order if you are new:** [BOARD_OVERVIEW.md](./BOARD_OVERVIEW.md) →
[SYSTEM_ARCHITECTURE.md](./SYSTEM_ARCHITECTURE.md) → [GLOSSARY.md](./GLOSSARY.md) → the module
documents that interest you → [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md).

---

## 1. Documents written for this task (this document set)

All of these are in `C:\GSDDashboard_secure\documentation`. The ShiftKiosk subject matter is documented
**here** rather than in the ShiftKiosk repository, because that repository's `documentation\` folder
holds the owner's Serbian-language rollout ledger, which stays where it is. Each document names its
subject clearly.

| Document | Repository | One-sentence description |
|---|---|---|
| [BOARD_OVERVIEW.md](./BOARD_OVERVIEW.md) | Pulse | Two-to-three-page, non-technical explanation for the board: the problem, the solution, the verified numbers, the honest security position and the actions required. |
| [VIDEO_BRIEF.md](./VIDEO_BRIEF.md) | Pulse | A suggested chapter-by-chapter storyline for the board video, with the exact screens to show and the figure and source behind every claim. |
| [SYSTEM_ARCHITECTURE.md](./SYSTEM_ARCHITECTURE.md) | Both | How the two products fit together: layers, ports, request paths, the four architecture diagrams, and the known architectural risks. |
| [PULSE_FEATURES.md](./PULSE_FEATURES.md) | Pulse | Every page and function of WorkForce Pulse, one by one, with its route, its endpoints, its read/write nature, and the pages that do **not** exist under the names people use. |
| [ROSTER_AND_SCHEDULING.md](./ROSTER_AND_SCHEDULING.md) | Pulse | Where shifts come from: the 16 shift categories and who writes each, the Roster Generator, conflict and overlap rules, the legal rest rules, agent grouping, and the manual-versus-automatic protection chain. |
| [SHIFTKIOSK.md](./SHIFTKIOSK.md) | ShiftKiosk | The attendance product end to end: the two clients, the shift flow including pause and auto-logout, the offline queues, the device-token and broker model, the eight supervisor views, the nine security flags, and the stored data. |
| [INTEGRATION_AND_DATA_FLOWS.md](./INTEGRATION_AND_DATA_FLOWS.md) | Both | The three connections between the products, the authenticated kiosk proxy with its full error mapping, the notification flows, and every data flow in a table. |
| [SECURITY_MODEL.md](./SECURITY_MODEL.md) | Both | Both authentication worlds, the device-token and legacy-key model, the security tables, rate limiting, what is closed, what is open, and the limits we do not claim away. |
| [DATA_MODEL.md](./DATA_MODEL.md) | Both | Every table in both databases with row counts as at 2026-10-11, the key relationships, the archive tables, and the corrections to earlier schema documentation. |
| [OPERATIONS.md](./OPERATIONS.md) | Both | Build, deploy, the sentinel, the flag profiles, packaging a kiosk, installation, backup, the four kinds of rollback, monitoring and the known operational faults. |
| [CHANGE_HISTORY.md](./CHANGE_HISTORY.md) | Both | How the project got here, grouped by theme rather than by commit, from the first commit to the newest feature, with the working-tree caveat. |
| [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) | Both | Every open security item, data and correctness issue, functional gap and documentation contradiction found, each with a severity, plus the recommended order of work. |
| [GLOSSARY.md](./GLOSSARY.md) | Both | Every acronym, code word and domain term in both systems, with the two meanings of "RTM" and the location of "agent group" called out. |

---

## 2. Existing documents in this repository — and their status

The `documentation` folder already contained a substantial corpus. Rather than duplicate it, this set
**supersedes** the parts that are stale and **links to** the parts that are still correct. The status
column is the honest one.

| Existing document | Status | Comment |
|---|---|---|
| [PROJECT_BLUEPRINT.md](./PROJECT_BLUEPRINT.md) | **Partly superseded** | Still the best narrative of purpose, users, problems solved and the "never do this" engineering rules. Its sections 8 and 10 contain the QR-terminal claim, the 57-agent figure and the stale proxy-integration text — all corrected in this set. |
| [BLUEPRINT_DATA_MODEL.md](./BLUEPRINT_DATA_MODEL.md) | **Superseded for table inventory** | Documents 21 tables; the live database has 52 (26 application/framework). Superseded by [DATA_MODEL.md](./DATA_MODEL.md). Its per-column detail remains useful where it agrees with the live schema. |
| [BLUEPRINT_LOGIC.md](./BLUEPRINT_LOGIC.md) | **Partially current** | Deep business-logic detail. Predates the manual-marker work and the presence resolver; its API reference uses several older paths. Cross-check any path against [PULSE_FEATURES.md](./PULSE_FEATURES.md). |
| [BLUEPRINT_SCREENS.md](./BLUEPRINT_SCREENS.md) | **Stale** | Written when there were fewer routes: it has no Rollout, Remote Attendance or HR Export entry, and it describes the attendance page as calling the kiosk server directly, which is no longer true. Superseded by [PULSE_FEATURES.md](./PULSE_FEATURES.md). |
| [BLUEPRINT_DESIGN.md](./BLUEPRINT_DESIGN.md) | **Current** | The design-token system. Still the authority for colours, typography, spacing and z-index. Not covered by this set because it is not a board concern. |
| [overview.md](./overview.md) | **Stale in places** | Feature overview. Its employee and location figures are superseded; its backend-service table is a useful index. |
| [FUNCTIONS.md](./FUNCTIONS.md) | **Partially current** | Service catalogue with dependency-injection lifetimes and a known-issues table. Its own known-issues list is still the best single list of code-level oddities; the legal-rule and coverage details have moved on. |
| [technical.md](./technical.md) | **Partially current** | Long technical reference. Its deployment section is superseded by [OPERATIONS.md](./OPERATIONS.md); its "three coverage sources" section predates the unified presence resolver. |
| [auth_rbac.md](./auth_rbac.md) | **Contains a serious error** | Its architecture is the best available description of the role model, **but** it documents a supervisor enforcement switch that no code reads, and its account table contradicts its own update section in the same file. Read [SECURITY_MODEL.md](./SECURITY_MODEL.md) §2 and §4 instead, and treat this file's supervisor sections as historical. |
| [README.md](./documentation/README.md) | **Partly stale** | Deployment and troubleshooting basics. Its stack-versions line and its "verify" text are superseded. |
| [LIVE_DEPLOY_RUNBOOK.md](./LIVE_DEPLOY_RUNBOOK.md) | **Authoritative for deployment** | The live-state and staged-deployment record for the server and Pulse. Summarised, not replaced, by [OPERATIONS.md](./OPERATIONS.md). Its "current state" section conflicts with the kiosk hardening note on whether the proxy cutover is live. |
| [KIOSK_HARDENING_DEPLOY_NOTE.md](./KIOSK_HARDENING_DEPLOY_NOTE.md) | **Authoritative for kiosk live state, with one error** | The canonical kiosk state record. Its flag table is **contradicted by the live configuration** on device binding, and its Pulse "not deployed" line conflicts with the runbook. See [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) items S17 and C23. |
| [BULK_RTM.md](./BULK_RTM.md) | **Current for its subject** | The Bulk RTM rule. Describes uncommitted working-tree behaviour. |
| [ROLLOUT.md](./ROLLOUT.md) | **Current for its subject** | The Rollout feature. Note the feature is live but has never been used (0 rows). |
| [SECURITY_FINDINGS.md](./SECURITY_FINDINGS.md) | **Stale** | Dated audit listing issues that have since been addressed in mechanism, though some remain in effect because the authorisation switch is off. Superseded by [SECURITY_MODEL.md](./SECURITY_MODEL.md). |
| [KIOSK_AUTH_IDENTITY_ASIS.md](./KIOSK_AUTH_IDENTITY_ASIS.md) | **Historical snapshot** | Self-declared as pre-dan1. Valuable as the "how it was" baseline. |
| [KIOSK_ENDPOINT_ACCESS_MATRIX.md](./KIOSK_ENDPOINT_ACCESS_MATRIX.md) | **Historical snapshot** | Self-declared as pre-dan1. The endpoint-by-endpoint access picture at that time. |
| [KIOSK_DATA_EXPOSURE_INVENTORY.md](./KIOSK_DATA_EXPOSURE_INVENTORY.md) | **Historical snapshot** | Self-declared as pre-dan1. What data each endpoint exposed. |
| [KIOSK_AUDIT_LOG_SEMANTICS.md](./KIOSK_AUDIT_LOG_SEMANTICS.md) | **Historical snapshot** | Self-declared as pre-dan1. How the actor fields worked before the change. |
| [KIOSK_DEPLOYMENT.md](./KIOSK_DEPLOYMENT.md) | **Historical snapshot** | The legacy-client deployment picture. Still the best source on why the migration is expensive. |
| [KIOSK_GAPS_AND_CONTRADICTIONS.md](./KIOSK_GAPS_AND_CONTRADICTIONS.md) | **Historical audit** | Its finding that the kiosk was mis-documented as unbuilt hardware remains the clearest statement of that contradiction. |
| [KIOSK_SECURITY_SOLUTION.md](./KIOSK_SECURITY_SOLUTION.md) | **Historical proposal** | Self-declared as "nothing was implemented". Useful for the reasoning behind the design. |
| [SECURITY_MIGRATION_PROPOSAL.md](./SECURITY_MIGRATION_PROPOSAL.md) | **Historical proposal** | Self-declared as proposal only. |
| [VERIFICATION_REPORT.md](./VERIFICATION_REPORT.md) | **Stale** | A dated verification pass whose counts no longer hold (for example, its WIC-location and unique-index claims are contradicted by the live schema). |
| [conflicts_2026-09-03.md](./conflicts_2026-09-03.md) | **Historical data report** | A dated conflict report. Contains individual names — not board-facing. |
| [handoff_final.md](./handoff_final.md) / [handoff_prompt1-4.md](./handoff_prompt1.md) | **Historical** | Session hand-offs. Useful for context, not for current facts. |
| [KIOSK_NG_INSTALL.md](./KIOSK_NG_INSTALL.md) | **Superseded** | The one-page NG install note; the full procedure is in the kiosk repository's rollout ledger. |

> **Do not hand the existing corpus to a board or an external reader as-is.** Multiple files in it
> contain individual names, machine names, network addresses and device identifiers. Those files were
> deliberately left unchanged by this task; the board-facing documents are
> [BOARD_OVERVIEW.md](./BOARD_OVERVIEW.md) and [VIDEO_BRIEF.md](./VIDEO_BRIEF.md).

---

## 3. Documents in the ShiftKiosk repository

These stay where they are. They are referenced, not duplicated.

| Path | Language | Role |
|---|---|---|
| `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md` | Serbian | **The authoritative client-rollout and packaging procedure**, including the recorded packaging incident and its guard, the installation and removal commands, the backup rules and the auto-logout root-cause analysis. |
| `C:\ShiftKiosk_secure\documentation\history\KIOSK_NG_ROLLOUT_history_2026-10-05.md` | Serbian | The retired pilot history for the rollout above. |
| `C:\ShiftKiosk_secure\README.md` | English | System overview, pages, status types, configuration keys, troubleshooting and the language policy for the kiosk product. |
| `C:\ShiftKiosk_secure\PROJECT_DOCUMENTATION.md` | Serbian | Large code-derived reference. **Stale:** it states a 9-hour auto-logout (the code and the live data say 11), pre-dates the entire security layer and the NG client, and its line references no longer match. Use [SHIFTKIOSK.md](./SHIFTKIOSK.md) instead and consult this only for historical detail. |
| `C:\ShiftKiosk_secure\CODE_OVERVIEW.md` | English | Function-level overview of the server and the legacy client. Pre-dates the NG client and the security layer. |
| `C:\ShiftKiosk_secure\CHANGELOG.md` | English | Session-by-session change log for the early kiosk work. |
| `C:\ShiftKiosk_secure\DEPLOY_AGENT_LAPTOPS.md` | English | The legacy client's laptop-deployment procedure and hard-won rules. Its checksum table refers to the legacy client. |

**Cross-links.** The kiosk-side subject matter written by this task links into
[SHIFTKIOSK.md](./SHIFTKIOSK.md), [SECURITY_MODEL.md](./SECURITY_MODEL.md) and
[OPERATIONS.md](./OPERATIONS.md) here, and those documents link back to the kiosk repository's rollout
ledger as the authority for client procedures.

---

## 4. Repository-root documents worth knowing

| Path | Role |
|---|---|
| `CLAUDE.md` (both repositories) | The permanent engineering rules: the definition of done, the sentinel, the Rollout rule, the Bulk RTM rule, and the EN/DE-only language rule. **Read first by anyone changing code.** |
| `DEPLOYMENT_AND_VERIFICATION.md` | The nine verification rules, including the sentinel contract and the "the tunnel is what the user sees" rule. |
| `DEPLOY.md` | Fuller deployment reference. |
| `CONTEXT.md`, `REDESIGN_FULL_SPEC.md`, `REDESIGN_CURRENT_STATE.md` | Design and context notes from earlier phases. Historical. |
| Numerous `PS1_*.ps1`, `*_2026*.sql` and ad-hoc scripts at repository root | Historical operational scripts from the data-repair era. Not part of the running application; some still contain individual names and should not be shared. |

---

## 5. Document maintenance rules adopted by this set

1. **A figure without a date and a source is not a fact.** Every number in these documents carries the
   2026-10-11 verification date and the table it came from.
2. **"NIJE POTVRĐENO" (not confirmed) is a first-class result.** Where the code could not settle a
   question, the document says so and names what is missing rather than choosing a plausible answer.
3. **Where documentation and code disagree, both are reported.** The disagreement is recorded in
   [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) rather than silently edited away.
4. **Superseded does not mean deleted.** Older documents remain, with their status stated here, because
   they are the record of how a decision was reached.
5. **No personal data.** No agent names, user names, machine names, network addresses or device
   identifiers appear in this document set, including in examples.
6. **No secret values.** Configuration is documented by key name only.
