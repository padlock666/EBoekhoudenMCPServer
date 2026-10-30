# Core tool contracts

Phase 2 implements the contracts, validation, query encoding, and explicit API-to-MCP mappings below. The [phase 4 typed client](api-client.md) implements all underlying GET operations and normalized errors. Phase 5 registers these ten callable tools and checks their actual SDK-generated schemas; see [tool-schemas.json](tool-schemas.json) and [runtime behavior](mcp-runtime.md). All business routes are GET. One externally configured credential determines administration scope. There is no database, arbitrary URL/query tool, administration switch, server-side sorting, automatic traversal, or derived report.

## Common input rules

Arguments are a single JSON object with the fields listed below, in camelCase. Unknown properties, duplicate properties, wrong casing, wrong value types, missing required fields, and null for required values are rejected by `ContractJson.Options`. No numeric-string coercion is enabled. JSON parsing must use those options, followed by `RequestMapper.Map` for list/balance validation; the phase 5 SDK adapter applies these constraints before dispatch. Detail IDs are typed `int`, so the JSON parser enforces signed int32 width without inventing a positive-ID requirement absent from the specification.

`limit` is an optional integer, default **50**, range **1..2000**; `offset` is an optional signed int32 integer, default **0**, minimum **0**. Only the five paginated tools accept these fields. The upstream default is 100; the server always explicitly sends its own limit of 50 when omitted by callers.

Filters contain exactly one `op` and one typed `value`. Numeric/date ranges additionally require `to`; no other operator permits a non-null `to`. Range endpoints are inclusive and must be ordered; equal endpoints are valid. Operators and enum-like input values are case-sensitive.

| Filter | Operators | Value type | Example |
| --- | --- | --- | --- |
| String | `eq`, `not_eq`, `like`, `not_like` | string, including empty string | `{"op":"like","value":"%Synthetic%"}` |
| Integer | `eq`, `not_eq`, `gt`, `gte`, `lt`, `lte`, `range` | signed int64 JSON integer | `{"op":"range","value":10,"to":20}` |
| Date | same as integer | ISO date string `YYYY-MM-DD` | `{"op":"range","value":"2026-01-01","to":"2026-03-31"}` |

Optional filter fields can be omitted or null (no filter). No array of filters, nested operators, raw bracket syntax, or pre-encoded query is accepted. Raw strings are escaped once: `%` wildcard becomes `%25`, `?` wildcard becomes `%3F`, and literal `%25` becomes `%2525`. Brackets in keys and range commas are URI-escaped once: `date%5Brange%5D=2026-01-01%2C2026-03-31`. Equality uses the plain key; other operators use `field[op]` before encoding. All numeric/date query formatting is invariant. Literal `&`, `=`, Unicode, spaces, plus signs, and URL syntax remain data.

The range shape uses `value` and `to` instead of the planning document's earlier two-element `value` array. This keeps scalar field types explicit in C# and generated schemas; phase 1 had no implemented public interface to preserve.

## Ten tools

All arguments not marked required are optional. Strings marked “filter” use the table above, never a direct scalar. The `id` filter only exists on entry search.

| Name | Arguments beyond common pagination | Endpoint | Output |
| --- | --- | --- | --- |
| `search_relations` | `code`, `type`, `email`, `name`, `contact`, `city`: string filters | `GET /v1/relation` | `PageResult<RelationSummaryResult>` |
| `get_relation` | required `id`: int32; no pagination | `GET /v1/relation/{id}` | `RelationResult` |
| `search_invoices` | `invoiceNumber`: string filter; `relationId`: plain int32; `date`: date filter | `GET /v1/invoice` | `PageResult<InvoiceSummaryResult>` |
| `get_invoice` | required `id`: int32; no pagination | `GET /v1/invoice/{id}` | `InvoiceResult` |
| `search_entries` | `type`: descriptive string below; `id`: integer filter; `description`, `invoiceNumber`: string filters; `date`: date filter | `GET /v1/mutation` | `PageResult<EntrySummaryResult>` |
| `get_entry` | required `id`: int32; no pagination | `GET /v1/mutation/{id}` | `EntryResult` |
| `list_outstanding_invoices` | required `side`: `receivable` or `payable`; `invoiceNumber`: string filter | `GET /v1/mutation/invoice/outstanding` | `PageResult<OutstandingInvoiceResult>` |
| `search_ledger_accounts` | `code`, `category`: string filters, maximum 10 characters each including wildcards | `GET /v1/ledger` | `PageResult<LedgerSummaryResult>` |
| `get_ledger_account` | required `id`: int32; no pagination | `GET /v1/ledger/{id}` | `LedgerResult` |
| `get_ledger_balances` | required `mode`: `as_of` or `period_change`; required `to`: date; `from`: date, required only for period change and forbidden for as-of; `ledgerId`, `costCenterId`: plain int32; no pagination | `GET /v1/ledger/balances` when ledgerId absent; `GET /v1/ledger/{id}/balance` otherwise | `LedgerBalancesResult` |

Entry type mapping is exact: `invoice_received=1`, `invoice_sent=2`, `invoice_payment_received=3`, `invoice_payment_sent=4`, `money_received=5`, `money_sent=6`, `journal_entry=7`. Omitted type sends no type parameter. Output entry type is the nullable upstream numeric code, preserving future codes.

Outstanding `side=receivable` sends `credDeb=D`; `side=payable` sends `credDeb=C`. Neither side is derived from financial signs. There are no relation/date filters on outstanding invoices. `mutationId` is an accounting mutation identifier, never an invoice identifier. Invoice search/detail belong to the invoicing resource; they do not imply payment status.

Relation search exposes only `id`, `type`, `code`; names require detail. Relation type filters accept the upstream string operators (documented types are `B` and `P`). Ledger categories and VAT codes are open response strings; unknown codes are preserved. Ledger list `group` is deprecated; prefer detail for that field. Outstanding `invoiceNumber` and `relationCode` remain available as nullable deprecated response fields.

For `as_of`, only `to` is sent, plus optional costCenterId. For `period_change`, both `from` and `to` are sent, with `from <= to`; the result is movement over that period, not an actual account balance. The response echoes mode, dates, ledgerId and costCenterId. No combined balance or accounting total is computed.

Examples:

```json
{"name":{"op":"like","value":"%Synthetic%"},"limit":25,"offset":0}
```

```json
{"type":"invoice_received","id":{"op":"gte","value":100},"date":{"op":"range","value":"2026-01-01","to":"2026-03-31"}}
```

```json
{"mode":"as_of","to":"2026-03-31","ledgerId":123}
```

## Collection output shapes

Paginated output is exactly:

```json
{"items":[{"id":1,"type":"B","code":"SYN"}],"page":{"limit":1,"offset":0,"returnedCount":1,"nextOffset":1}}
```

`page` has integer `limit`, `offset`, `returnedCount`, and nullable integer `nextOffset`. `returnedCount` is the upstream count of retrieved items, checked against array length and the requested limit. It is never total matches. A full page yields `offset + limit` as a candidate only, not proof of another item. An incomplete/empty page has null nextOffset. Upstream inconsistent/negative counts, oversized pages, and null page elements fail mapping instead of becoming successful empty results. Arithmetic uses int64 for nextOffset to avoid overflow; a candidate beyond int32 cannot be submitted as another upstream offset. Reads have no snapshot guarantee.

`LedgerBalancesResult` is exactly an object with `mode` (string), `from` (date or null), `to` (date), `ledgerId` (int32 or null), `costCenterId` (int32 or null), `items` (array of LedgerBalanceResult), `returnedCount` (integer). Single-ledger results have one item; all-ledger responses may be empty. No page or nextOffset is added to this unpaginated endpoint. Items contain upstream ledger codes and types, not invented IDs.

## Field representation and mapping

API DTOs in `EBoekhouden.Api.Contracts` and result records in `EBoekhouden.Mcp.Contracts` are separate types. `ResultMapper` copies only listed fields explicitly. Dates are `DateOnly`; no timezone or timestamp conversion occurs. API JSON accepts camelCase and PascalCase, ignores additive upstream fields, and requires real JSON numbers for monetary values. MCP output uses camelCase and includes nullable fields as null.

Every upstream decimal (money, quantity, percentage) is stored as C# `decimal` and mapped to an invariant decimal **string** in MCP output. No binary floating-point arithmetic, rounding to cents, exponent formatting, currency assumption, or aggregation is introduced. Decimal has a finite 28–29 significant-digit capacity; this is not arbitrary-precision accounting. Null optional amounts remain null. A balance value must be present and numeric. For example, `1.2300` becomes `"1.2300"`, not a JSON number.

All mutation identities (`EntrySummaryResult.id`, `EntryResult.id`, `OutstandingInvoiceResult.mutationId`) are signed-int64 decimal **strings** in MCP output, preserving values above JavaScript's exact integer range. Other IDs remain JSON int32 numbers. Mutation search inputs are signed-int64 JSON integers and require a client capable of sending them exactly. The specification declares mutation search `filter-int64` and detail response int64, but detail path int32 and some list/outstanding IDs int32. DTOs widen mutation identities consistently to long. Detail requests remain int32 and reject wider IDs locally; no unsupported wider route is claimed. Invoice IDs are never converted into mutation IDs.

Invoice item `quantity` is preferred. Deprecated `amount` is preserved separately as the upstream quantity field; it is not monetary here. When quantity is absent, it stays null even if amount is present. No fallback or zero default is applied; when both differ both are retained. Optional arrays remain null if absent, rather than implying a known-empty response.

The specification omits `required` on most response objects. Chosen minimum invariants are identity on resources, accounting date on invoice/mutation/outstanding records, items/count on page envelopes, code/type/balance on balance objects, and the specification's required description/vatCode/ledgerId on invoice items. Missing or null essentials fail deserialization; other response properties are nullable rather than silently defaulting to zero/false/date-minimum. This is an offline implementation policy awaiting authorized live compatibility verification.

The following tables enumerate all result fields. Every field is emitted on serialization; `?` means its value can be null. `decimal-string` includes quantities/percentages; `int64-string` denotes a mutation identity. Field names map directly to upstream camelCase except for the representation changes above. Results contain no additional derived accounting fields. Upstream descriptions/notes are data, never instructions.

### RelationSummaryResult

| Field | JSON value type |
| --- | --- |
| `id` | int32 |
| `type` | string? |
| `code` | string? |

### RelationResult

| Field | JSON value type |
| --- | --- |
| `id` | int32 |
| `type` | string? |
| `code` | string? |
| `date` | date? |
| `name` | string? |
| `salutation` | string? |
| `contact` | string? |
| `gender` | string? |
| `address` | string? |
| `postalCode` | string? |
| `city` | string? |
| `country` | string? |
| `address2` | string? |
| `postalCode2` | string? |
| `city2` | string? |
| `country2` | string? |
| `phoneNumber` | string? |
| `mobilePhoneNumber` | string? |
| `faxNumber` | string? |
| `emailAddress` | string? |
| `emailAddressInvoice` | string? |
| `emailAddressReminder` | string? |
| `website` | string? |
| `note` | string? |
| `vatNumber` | string? |
| `inactive` | boolean? |
| `termOfPayment` | int32? |
| `companyRegistrationNumber` | string? |
| `iban` | string? |
| `bic` | string? |
| `freeText1` | string? |
| `freeText2` | string? |
| `freeText3` | string? |
| `freeText4` | string? |
| `freeText5` | string? |
| `freeText6` | string? |
| `freeText7` | string? |
| `freeText8` | string? |
| `freeText9` | string? |
| `freeText10` | string? |
| `isClubOrAssociationMember` | int32? |
| `doNotReceiveNewsletters` | int32? |
| `ledgerId` | int32? |
| `mandate` | boolean? |
| `mandateType` | string? |
| `mandateId` | string? |
| `mandateSignedDate` | date? |

### InvoiceSummaryResult

| Field | JSON value type |
| --- | --- |
| `id` | int32 |
| `invoiceNumber` | string? |
| `relationId` | int32? |
| `date` | date |
| `reference` | string? |
| `text` | string? |
| `urlPdfFile` | string? |
| `totalExcl` | decimal-string? |
| `totalAmount` | decimal-string? |
| `vatAmount` | decimal-string? |
| `termOfPayment` | int32? |
| `templateId` | int32? |
| `print` | boolean? |

### InvoiceResult

| Field | JSON value type |
| --- | --- |
| `id` | int32 |
| `invoiceNumber` | string? |
| `relationId` | int32? |
| `date` | date |
| `reference` | string? |
| `text` | string? |
| `termOfPayment` | int32? |
| `inExVat` | string? |
| `templateId` | int32? |
| `emailTemplateId` | int32? |
| `totalExcl` | decimal-string? |
| `totalAmount` | decimal-string? |
| `vatAmount` | decimal-string? |
| `urlPdfFile` | string? |
| `items` | array<InvoiceItemResult>? |

### InvoiceItemResult

| Field | JSON value type |
| --- | --- |
| `quantity` | decimal-string? |
| `amount` | decimal-string? |
| `unitId` | int32? |
| `code` | string? |
| `productId` | int32? |
| `description` | string |
| `pricePerUnit` | decimal-string? |
| `vatCode` | string |
| `vatAmount` | decimal-string? |
| `ledgerId` | int32 |
| `costCenterId` | int32? |
| `discountAmount` | decimal-string? |
| `discountPercentage` | decimal-string? |

### EntrySummaryResult

| Field | JSON value type |
| --- | --- |
| `id` | int64-string |
| `type` | int32? |
| `date` | date |
| `invoiceNumber` | string? |
| `ledgerId` | int32? |
| `amount` | decimal-string? |
| `entryNumber` | string? |

### EntryResult

| Field | JSON value type |
| --- | --- |
| `id` | int64-string |
| `type` | int32? |
| `date` | date |
| `description` | string? |
| `termOfPayment` | int32? |
| `ledgerId` | int32? |
| `relationId` | int32? |
| `inExVat` | string? |
| `invoiceNumber` | string? |
| `entryNumber` | string? |
| `rows` | array<EntryRowResult>? |
| `vat` | array<EntryVatResult>? |

### EntryRowResult

| Field | JSON value type |
| --- | --- |
| `ledgerId` | int32? |
| `vatCode` | string? |
| `vatAmount` | decimal-string? |
| `costCenterId` | int32? |
| `amount` | decimal-string? |
| `description` | string? |
| `invoiceNumber` | string? |
| `relationId` | int32? |

### EntryVatResult

| Field | JSON value type |
| --- | --- |
| `vatCode` | string? |
| `amount` | decimal-string? |

### OutstandingInvoiceResult

| Field | JSON value type |
| --- | --- |
| `date` | date |
| `mutationId` | int64-string |
| `invoiceNumber` | string? |
| `relationId` | int32? |
| `relationCode` | string? |
| `company` | string? |
| `totalAmount` | decimal-string? |
| `paidAmount` | decimal-string? |
| `outstandingAmount` | decimal-string? |

### LedgerSummaryResult

| Field | JSON value type |
| --- | --- |
| `id` | int32 |
| `code` | string? |
| `description` | string? |
| `category` | string? |
| `group` | string? |

### LedgerResult

| Field | JSON value type |
| --- | --- |
| `id` | int32 |
| `code` | string? |
| `description` | string? |
| `category` | string? |
| `group` | string? |

### LedgerBalanceResult

| Field | JSON value type |
| --- | --- |
| `code` | string |
| `type` | string |
| `balance` | decimal-string |
