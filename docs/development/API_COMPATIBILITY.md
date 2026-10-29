# API compatibility and verification notes

Source: repository file `e-boekhouden-api-v1.yaml`. Initial review: 2026-10-29. No live API requests have been made as part of planning.

## Known constraints and ambiguities

| Topic | Evidence or constraint | Implementation direction | Verification |
| --- | --- | --- | --- |
| Authorization header | Quick-start prose uses the token directly; securitySchemes gives `Bearer SESSION_TOKEN` | Initially follow securitySchemes; isolate header construction and document assumption | Live verification pending |
| JSON casing | Schemas use camelCase; examples use PascalCase | Use deliberate case-insensitive upstream deserialization and stable MCP output casing | Synthetic fixture tests planned; live pending |
| Session creation | POST /v1/session requires accessToken and source; response has token and expiresIn | Internal-only authentication exception; cache by expiry | Offline tests planned |
| Source identifier | Maximum length 10 with a documented pattern | Validate configuration against spec | Offline tests planned |
| Relation search | Returns id, type, code only | Do not fabricate names; get_relation retrieves details | Spec-reviewed |
| Invoice versus mutation | Invoicing and accounting are separate resources | Keep IDs and contracts distinct | Spec-reviewed |
| Outstanding balances | Endpoint requires credDeb C or D and returns mutationId | Map payable/receivable explicitly; no invented relation/date filters | Spec-reviewed |
| Pagination | Count is number retrieved, not total; limit maximum 2000 | Return page count and candidate next offset | Spec-reviewed |
| Filter operators | One operator per field; range inclusive; plain parameters do not accept filters | Central typed filter encoder | Offline tests planned |
| Ledger balances | Supplying from produces period movement rather than actual balance | Explicit as_of versus period_change mode | Spec-reviewed |
| Identifier widths | Mutation declarations include inconsistent int32/int64 widths | Review every relevant path/query/response and document representation | Decision pending in phase 2 |
| VAT codes | Introductory country tables and schema enum are not identical in coverage | Preserve response codes; do not validate reads against an incomplete enum | Review pending in phase 2 |
| Quantity | Invoice item amount is deprecated in favor of quantity | Prefer quantity; define any compatibility fallback explicitly | Review pending in phase 2 |
| Error shapes | Error schemas/examples may differ, including errors collection shape | Normalize defensively, retaining useful metadata without raw-body exposure | Fixture coverage pending |
| Administration scope | Discovery routes exist, but no explicit switching route in this spec | Bind to configured credential; do not invent administrationId support | Spec-reviewed |
| Reports | No dedicated aging, P&L, or VAT-summary routes in this spec | Defer derived reporting until rules/completeness are defined | Out of initial scope |

## Live smoke test, when authorized

Verify header format, actual list/detail casing and nullability, session expiry behavior, and representative error shapes with minimal read-only calls. Record sanitized observations, endpoint, date, and test result. Never commit tokens, identifiable financial records, or raw production responses.

Offline fixtures prove implementation behavior against fixtures, not actual service compatibility. Do not mark a live assumption verified merely because a synthetic test passes.

## New finding template

```text
Topic:
Specification location / endpoint:
Observed inconsistency:
Chosen handling:
Evidence (spec / synthetic test / authorized live test):
Remaining uncertainty:
Follow-up:
```
