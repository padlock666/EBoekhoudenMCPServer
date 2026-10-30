# Typed read-only API client

Phase 4 implements `IEBoekhoudenReadClient` and `EBoekhoudenReadClient` in the API library. Both production and explicit test registrations register the interface as a singleton. Resolving it does not acquire a session. Methods return API DTOs, not MCP results, and require a cancellation token.

## Operations

All return types below are wrapped in Task.

| Method | Input | GET route | Return type |
| --- | --- | --- | --- |
| GetRelationsAsync | RelationQuery | `/v1/relation` | ApiPage<RelationSummaryDto> |
| GetRelationAsync | int32 id | `/v1/relation/{id}` | RelationDto |
| GetInvoicesAsync | InvoiceQuery | `/v1/invoice` | ApiPage<InvoiceSummaryDto> |
| GetInvoiceAsync | int32 id | `/v1/invoice/{id}` | InvoiceDto |
| GetMutationsAsync | EntryQuery | `/v1/mutation` | ApiPage<EntrySummaryDto> |
| GetMutationAsync | int32 id | `/v1/mutation/{id}` | EntryDto |
| GetOutstandingInvoicesAsync | OutstandingQuery | `/v1/mutation/invoice/outstanding` | ApiPage<OutstandingInvoiceDto> |
| GetLedgersAsync | LedgerQuery | `/v1/ledger` | ApiPage<LedgerSummaryDto> |
| GetLedgerAsync | int32 id | `/v1/ledger/{id}` | LedgerDto |
| GetLedgerBalancesAsync | BalanceQuery, ledgerId absent | `/v1/ledger/balances` | ApiPage<LedgerBalanceDto> |
| GetLedgerBalanceAsync | BalanceQuery, ledgerId required | `/v1/ledger/{id}/balance` | LedgerBalanceDto |

Methods reuse ReadRequest, QueryEncoder and ReadTransport. There is no arbitrary URL/path/method input, new route, financial read cache, sorting or automatic traversal. Five paginated methods send explicit limit/offset; balances are unpaginated. Single/all balance scope mismatches and null list query/page objects fail before session acquisition.

`as_of` sends to only; `period_change` sends from/to. Values are returned without reinterpretation or aggregation. The separate MCP mapper echoes mode/dates/scope. See [tool contracts](tool-contracts.md) for filters and MCP shapes.

## Response handling and nullability

Only the specified HTTP 200 success is accepted. A 404 raises an error, never null or an empty collection; unexpected success statuses such as 204 are invalid responses. HTTP 200 with `items: [], count: 0` is valid. Empty bodies, JSON null, wrong root shapes, malformed JSON and missing essentials fail without a new HTTP retry category.

All relevant GET properties were reviewed against the DTOs. Most schemas omit required markers. The phase 2 minimum-invariant policy remains:

| Response area | Required minimum | Optional behavior |
| --- | --- | --- |
| Relation/ledger summary/detail | id | Other fields nullable; relation search only id/type/code |
| Invoice/mutation summary/detail | id, date | Amounts, flags, references and nested arrays nullable |
| Outstanding item | mutationId, date | Amounts and relation/deprecated fields nullable |
| Invoice item | description, vatCode, ledgerId (specified required fields) | quantity/amount/price/VAT/discounts and other IDs nullable |
| Mutation row/VAT | No new required fields | Optional values remain null |
| Balance | code, type, balance | No invented ledgerId or aggregate |
| List envelope | items, count | Retrieved count, never total matches |

Counts must be nonnegative, match array length, and not exceed requested limits on paginated routes. All-balances has no pagination limit. Null collection elements fail, including nested invoice items and mutation rows/VAT. Missing/null optional arrays remain null; explicit empty arrays stay empty. No amount, flag, date or name is fabricated.

API JSON accepts camelCase/PascalCase and additive fields but rejects duplicate properties, including known fields repeated with different casing. Deserialization uses UTF-8 JSON and ApiJson.Options with DateOnly and exact decimal conversion. Numbers requiring rounding or underflow fail as InvalidResponse. Monetary strings and alternate date formats are rejected. The only numeric-string exception is mutation type, because its schema declares strings and its examples use numbers; both int32 representations map to the same numeric MCP code. Explicit zero remains zero; missing money remains null.

Mutation identities remain long despite inconsistent int32 declarations; detail inputs remain int32. Returned IDs are retained without narrowing or rewriting. Invoice IDs and mutation IDs stay distinct. Invoice detail implies no payment status. Outstanding results refer to mutationId. Unknown VAT/category/type codes survive. Quantity and deprecated amount are independent, without fallback. PDF URLs are data and never fetched.

Final responses and parsing streams are disposed on success/error/cancellation. An outer typed-call OperationTimeout passes cancellation into the existing transport and extends the deadline through parsing, validation and error inspection; it adds no retry policy. Caller cancellation remains OperationCanceledException with the caller token; internal deadlines become normalized Timeout failures. Argument validation remains local before dispatch.

## Normalized errors

ApiException exposes Kind (Http, InvalidResponse or Transport), nullable StatusCode, Code, TraceId and TransportFailure. Message uses fixed local status categories. No upstream title/message/errors/propertyName, raw body/URI, credential or low-level inner exception is retained. Metadata stays separate from Message/ToString. No logging or console output is added.

HTTP status is authoritative even when body status disagrees. Empty, non-JSON, malformed and wrong-shaped error bodies still yield the HTTP failure. The documented errors string-array and example dictionary shapes are tolerated by discarding their free text. Root code/traceId are read case-insensitively; ambiguous duplicate names and non-string values are omitted.

The transport receives headers first and buffers diagnostic bodies up to 64 KiB (16 KiB for sessions) under the existing deadline. Oversized bodies or HTTP/I/O body-read failures are disposed and discarded while preserving status. They do not cause permanent HTTP failures to be retried as network failures. Cancellation still propagates. Diagnostic inspection is capped at the same limits. Code is 1..64 ASCII characters, starting A–Z, with uppercase letters/digits/underscore only. Trace ID is 1..128 ASCII characters, starting alphanumeric, with letters/digits/period/underscore/colon/hyphen. These are local safety bounds, not upstream guarantees. Unknown codes with safe syntax survive; other formats are omitted without masking status.

Metadata equal to or containing the configured credential or failed request's Bearer token is omitted. Decoded non-pagination query values have outer wildcards removed for echo checks: exact matches are omitted, and values of at least four characters also use case-insensitive substring checks. Short query values use equality to avoid discarding unrelated diagnostic IDs containing common digits. All free text in other error properties is discarded.

Session rejection uses the same sanitizer and preserves available status/code/trace as Transport / SessionRejected, without adding POST retries. Invalid session bodies retain HTTP status when available. Existing authentication replay, transient retry, redirect and circuit behavior is unchanged. The MCP adapter translates ApiException explicitly into tool errors, never serializing exceptions or returning success-shaped empty data.

## Evidence and limits

Embedded fixtures in `tests/EBoekhouden.Api.Tests/Fixtures` are labeled synthetic. Tests retain the real factory/authentication/transport/guards and replace only primary handlers. They verify exact methods, origins, routes, encoded queries, headers, absence of request bodies, typed values and no extra detail/PDF requests. They also cover both error shapes, session failures, malformed/missing/empty/optional/additive data, required fields, precision, casing, identifier widths, disposal, cancellation and redaction.

No packages or MCP SDK APIs changed. Tests do not establish live compatibility. See [API compatibility](development/API_COMPATIBILITY.md) for the unverified Bearer wording, minimum response invariants, identifier-width conflict and diagnostic formats. Tool/host wiring and published subprocess integration are implemented and tested; see the [independent review](development/REVIEW.md).
