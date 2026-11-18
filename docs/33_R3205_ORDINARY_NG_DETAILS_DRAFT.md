# R32-05 Ordinary NG details draft

## Baseline and scope

PR #32 merged as `5f37eff9e93f5857d5770e2604cbd75651761af2` with the
same tree as reviewed HEAD `7197b8b5fae62540d41816e47934e077f37e6348`.
[The merged-main workflow](https://github.com/peterl-ai/IPQC-system/actions/runs/44392344777)
passed with 1,161 backend tests and 743 opt-in skips, 743 real PostgreSQL
17.12 tests, 166 Web tests, and zero backend Release warnings or errors.
There was no open or overlapping PR. The original checkout's 22 protected
paths and SHA-256 hashes matched before implementation.

[Approved Rev3.2 §10](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md)
defines optional NG Type, ordinary NG Reason and Remarks. [The approved
supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
governs identity and authorization. [The identity and acceptance
reference](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
retains its distinction between approved rules and proposed contracts.

The existing NG PIC observation already finds real accepted current sample
judgments in the initialized inspection attempt. The ordinary metadata
service reuses this observation and accepts only its `OrdinaryNG` result.
The other states include Removed, UnableToMeasureNG, NotCurrentlyNG and
unavailable scope. Legacy AbnormalType, AbnormalCause and Remarks remain
in the legacy workflow. The dedicated UnableToMeasureNG reason and evidence
requirements remain in their existing contract.

The scoped changes cover the three shared-draft routes, one typed receipt
extension, new OrdinaryNgDetails contracts, model, canonicalizer and service,
one additive migration plus snapshot, and test/CI integration. The prior
PIC migration upgrade test is pinned to its own target. The earlier migration
fingerprints exclude the new table when comparing the older schemas, while
the new upgrade tests cover the new table and preserved prior data.

## API, identity and recovery

The prefix is `/api/line-patrol/tasks/{taskId}`. All routes require current
IPQA personal authority and task scope. The POST also requires
`LinePatrol.ContributeDraft` and the existing strict JSON and transport limits.

| Route | Contract |
| --- | --- |
| POST `/ordinary-ng-details-batches` | One typed SetOrdinaryNgDetails or CorrectOrdinaryNgDetails operation. Required expected task/line/workflow/environment/DraftVersion and the existing contributor/WorkContext and local/base revision fields. |
| GET `/shared-draft/ordinary-ng-details` | Bounded current item page and independent descending DraftVersion history cursor. Optional expected task line and expected DraftVersion. |
| GET `/ordinary-ng-details-operations/{operationId}/receipt` | Original typed receipt in the actual task scope. |
| Existing GET `/operation-batches/{batchId}/receipt` | New receipt version 8 contains one `ordinaryNgDetailsOperations` entry. The unversioned sample receipt and versions 2–7 retain their existing wire shapes. |

The three optional fields accept null or a string of up to 2,000 UTF-16 code
units. Missing fields and explicit JSON null have the same value. Empty and
whitespace-only strings are accepted as supplied and remain distinct from
null. Unicode is preserved without trimming or normalization. Control
characters and malformed Unicode are rejected. An explicit all-null set
may be recorded, and a correction can clear all values to null. The workflow
has no requirement to send a set operation or supply any value.

Each set requires no existing operation for that item/attempt. Each correction
names the exact latest predecessor ID and content hash. The new service
freezes the full ordinary NG basis as stable references to the current, accepted,
nonremoved sample value operations whose judgments are NG. It does not require
photo bytes or lower sample requirements. The current item output independently
reports the latest metadata for the actual current attempt. History shows
original receipt, current scope, latest status and whether the observed NG
references still match. Applicability requires a current attempt, latest
operation and matching ordinary NG basis. An old receipt stays recoverable
after sample correction, removal or source security revocation. Current pages accept
20 items at most, and history accepts up to 100 rows, with a default of 20
for each.

Canonical V1 binds original scope, semantic fields and hashed predecessor.
Transport proof and actual uploader are excluded from request identity. The
transaction stores original contributor A and actual accepting user and
session B separately. Replay checks known identity before current draft
eligibility or fresh source validation. A known batch returns its original
common and typed receipts. The same operation under a different exact-scope
batch ID returns the original typed receipt without adding a new batch alias.
A receipt lookup returning 404 is point-in-time evidence.

## Additive schema and transaction

`20261118210133_R3205OrdinaryNgDetailsDraft` adds one immutable ledger table.
The scoped latest result comes from its indexed item/attempt DraftVersion
history. Restrictive keys bind the attempt and the exact scoped predecessor.
SQLite and PostgreSQL guards enforce matching scope, the real ordinary NG
basis, the common receipt, first actor fields and the all-domain operation
identity. EF and provider guards reject update/delete. The migration does
not backfill data and protects applied history against downgrade.

A single existing task version CAS, common batch receipt, domain ledger,
all-domain identity and audit commit in the existing authority transaction.
PostgreSQL retains the existing account/line/Task/context lock order.
SQLite uses its existing immediate writer reservation. PostgreSQL also blocks
TRUNCATE on the new immutable ledger.

## Local verification and developer review

- Focused verification passed 25 SQLite/provider-model cases and 23 real
  PostgreSQL 17.12 cases, with zero skips and failures in both commands.
- On each provider, nine admitted-writer races cover details against details,
  sample, Removed, PIC selection and header. Cross-account PostgreSQL tests
  observed the Task lock and verified the server-reported blocking path.
  SQLite observed native BEGIN IMMEDIATE while the winner held its writer.
  Each race verifies one commit and no losing receipt, audit or registry row.
- Populated upgrade tests compare old rows and HTTP receipt bytes before and
  after upgrading from `20261117191540_R3205NgPicDraftSelections`. Native
  tamper and failure injection verify rejection and rollback. The earlier
  unversioned sample receipt is tested without adding a version field.
- EF scaffold output initially omitted the existing provider-neutral mapping
  in the snapshot. The migration designer and snapshot use the existing
  provider conventions, and both model checks pass. Receipt tests compare
  the same HTTP encoding before and after recovery. The race harness was
  corrected for configured contender sets and verifies admission for each
  actual actor/family.
- Local Release restore/build passed with 0 warnings and 0 errors.
  The full Windows backend gate passed 1,184 with 768 skips and zero failures.
  The skips were 766 PostgreSQL opt-in cases and two Linux-only photo storage
  cases. Web install, lint, typecheck and production build passed, with 166 tests
  passed across 23 files. The existing Vite large-chunk warning remains.

The feature adds no submit requirement. Shared execution, submission and
formal inspection acceptance flags are all false. Formal Submit, PQE,
reinspection producers, offline F2, F3, Web or Android execution, PIC and
photo policy changes, and production activity are outside this scope. The
historical SQLite HTTP 500 trace `0HNQ4T7TVDUH5` remains unexplained.
