using EBoekhouden.Api.Filtering;
using EBoekhouden.Mcp.Contracts;

namespace EBoekhouden.Mcp.Validation;

public static class RequestMapper
{
    public static RelationQuery Map(SearchRelationsRequest request) => new(Page(request),
        S(request.Code), S(request.Type), S(request.Email), S(request.Name), S(request.Contact), S(request.City));
    public static InvoiceQuery Map(SearchInvoicesRequest request) => new(Page(request),
        S(request.InvoiceNumber), request.RelationId, D(request.Date));
    public static EntryQuery Map(SearchEntriesRequest request) => new(Page(request), EntryType(request.Type),
        request.Id is { } id ? new IntegerFilter(id.Op, id.Value, id.To) : null,
        S(request.Description), S(request.InvoiceNumber), D(request.Date));
    public static OutstandingQuery Map(ListOutstandingInvoicesRequest request) => new(Page(request),
        request.Side switch
        {
            "receivable" => "D",
            "payable" => "C",
            _ => throw new ArgumentException("Unsupported outstanding invoice side.", nameof(request)),
        }, S(request.InvoiceNumber));
    public static LedgerQuery Map(SearchLedgerAccountsRequest request)
    {
        if (request.Code?.Value is { Length: > 10 } || request.Category?.Value is { Length: > 10 })
            throw new ArgumentException("Ledger filters have a maximum length of 10.", nameof(request));
        return new(Page(request), S(request.Code), S(request.Category));
    }
    public static BalanceQuery Map(GetLedgerBalancesRequest request) => new(
        request.Mode, request.To, request.From, request.LedgerId, request.CostCenterId);

    private static Pagination Page(PagedRequest request) => new(request.Limit, request.Offset);
    private static StringFilter? S(StringFilterRequest? filter) => filter is null ? null : new(filter.Op, filter.Value);
    private static DateFilter? D(DateFilterRequest? filter) => filter is null ? null : new(filter.Op, filter.Value, filter.To);

    private static int? EntryType(string? type) => type switch
    {
        null => null,
        "invoice_received" => 1,
        "invoice_sent" => 2,
        "invoice_payment_received" => 3,
        "invoice_payment_sent" => 4,
        "money_received" => 5,
        "money_sent" => 6,
        "journal_entry" => 7,
        _ => throw new ArgumentException("Unsupported entry type.", nameof(type)),
    };
}
