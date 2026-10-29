# API compatibility and verification notes

Source: repository file `e-boekhouden-api-v1.yaml`. Initial review: 2026-10-29. No live API requests have been made as part of planning.

## Known constraints and ambiguities

| Topic | Evidence or constraint | Implementation direction | Verification |
| --- | --- | --- | --- |
| Authorization header | Quick-start prose uses the token directly; securitySchemes gives `Bearer SESSION_TOKEN` | Initially follow securitySchemes; isolate header construction and document assumption | Live verification pending |
| JSON casing | Schemas use camelCase; examples use PascalCase | Use deliberate case-insensitive upstream deserialization and stable MCP output casing | Case-insensitive API / strict camelCase MCP tests pass; live pending |
| Session creation | POST /v1/session requires accessToken and source; response has token and expiresIn | Internal-only authentication exception; cache by expiry | Offline tests planned |
| Source identifier | Maximum length 10 with a documented pattern | Validate configuration against spec | Offline tests planned |
| Relation search | Returns id, type, code only | Do not fabricate names; get_relation retrieves details | Spec-reviewed |
| Invoice versus mutation | Invoicing and accounting are separate resources | Keep IDs and contracts distinct | Spec-reviewed |
| Outstanding balances | Endpoint requires credDeb C or D and returns mutationId | Map payable/receivable explicitly; no invented relation/date filters | Spec-reviewed |
| Pagination | Count is number retrieved, not total; limit maximum 2000, upstream default 100 | Send explicit MCP default 50; return count and candidate next offset; validate count against items, never report total matches | Offline count, bounds and overflow tests pass |
| Filter operators | One operator per field; range inclusive; plain parameters do not accept filters | Typed filters use value/to for range; encoder escapes brackets, commas and raw values once | Offline operator, invalid range and encoding tests pass |
| Ledger balances | Supplying from produces period movement rather than actual balance; routes are unpaginated | Explicit as_of (to only) versus period_change (from/to); echo requested scope and dates, no computed aggregate | Offline query/result mode tests pass |
| Identifier widths | `/v1/mutation` id filter is int64, list id is int32; detail path int32 but response id int64; outstanding mutationId int32 | Widen mutation DTO identities to long and MCP output identities to decimal strings. Keep detail input int32 per path; int64 search input remains supported. Other IDs stay int32; no invoice/mutation conversion | Offline width/mapping tests pass; wider detail paths remain unsupported/unverified |
| VAT/category codes | Introductory VAT tables and schema enum differ in coverage; response codes can evolve | Preserve VAT and category codes as strings; do not reject unknown response codes | Offline unknown-code tests pass; live pending |
| Quantity | Invoice item amount is deprecated in favor of quantity, with quantity schema default 0 | Preserve quantity and deprecated amount independently as nullable decimals, mapped to MCP strings. No fallback or default-zero substitution when quantity absent | Offline tests cover absent and differing values; live pending |
| Error shapes | Error schemas/examples may differ, including errors collection shape | Normalize defensively, retaining useful metadata without raw-body exposure | Fixture coverage pending |
| Administration scope | Discovery routes exist, but no explicit switching route in this spec | Bind to configured credential; do not invent administrationId support | Spec-reviewed |
| Reports | No dedicated aging, P&L, or VAT-summary routes in this spec | Defer derived reporting until rules/completeness are defined | Out of initial scope |

## Phase 2 response policy findings

- Most response schemas do not declare required properties. The implementation deliberately requires resource identities, invoice/mutation/outstanding accounting dates, page items/count, ledger balance code/type/balance, and the invoice item fields explicitly required by the specification. Other fields remain nullable. This is an offline minimum-invariant policy; authorized live verification may require a documented revision, not silent default values.
- Decimal JSON fields are modeled as nullable decimal when optional. MCP maps decimals to invariant strings, with no rounding or invented currency. Quoted upstream monetary strings are rejected until actual evidence justifies a compatibility converter. Out-of-range decimal values fail deserialization; precision is bounded by .NET decimal.
- API DTOs accept additive fields and either casing. MCP requests use strict camelCase with duplicate/unknown fields rejected. Generated SDK schemas and argument binding still require phase 5 verification.
- Optional nested collections distinguish missing/null from empty; null collection elements fail result mapping. This prevents incomplete data from looking like a known-empty financial response.

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
