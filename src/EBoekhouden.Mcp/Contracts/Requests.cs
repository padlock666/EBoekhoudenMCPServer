namespace EBoekhouden.Mcp.Contracts;

// Each field has one filter object and one operator, never an arbitrary query fragment.
public sealed record StringFilterRequest
{
    public required string Op { get; init; }
    public required string Value { get; init; }
}

public sealed record IntegerFilterRequest
{
    public required string Op { get; init; }
    public required long Value { get; init; }
    public long? To { get; init; }
}

public sealed record DateFilterRequest
{
    public required string Op { get; init; }
    public required DateOnly Value { get; init; }
    public DateOnly? To { get; init; }
}

public abstract record PagedRequest
{
    public int Limit { get; init; } = 50;
    public int Offset { get; init; }
}

public sealed record SearchRelationsRequest : PagedRequest
{
    public StringFilterRequest? Code { get; init; }
    public StringFilterRequest? Type { get; init; }
    public StringFilterRequest? Email { get; init; }
    public StringFilterRequest? Name { get; init; }
    public StringFilterRequest? Contact { get; init; }
    public StringFilterRequest? City { get; init; }
}

public sealed record GetRelationRequest
{
    public required int Id { get; init; }
}

public sealed record SearchInvoicesRequest : PagedRequest
{
    public StringFilterRequest? InvoiceNumber { get; init; }
    public int? RelationId { get; init; }
    public DateFilterRequest? Date { get; init; }
}

public sealed record GetInvoiceRequest
{
    public required int Id { get; init; }
}

public sealed record SearchEntriesRequest : PagedRequest
{
    public string? Type { get; init; }
    public IntegerFilterRequest? Id { get; init; }
    public StringFilterRequest? Description { get; init; }
    public StringFilterRequest? InvoiceNumber { get; init; }
    public DateFilterRequest? Date { get; init; }
}

public sealed record GetEntryRequest
{
    // Path is int32 in the specification, even though responses/search IDs can be int64.
    public required int Id { get; init; }
}

public sealed record ListOutstandingInvoicesRequest : PagedRequest
{
    public required string Side { get; init; }
    public StringFilterRequest? InvoiceNumber { get; init; }
}

public sealed record SearchLedgerAccountsRequest : PagedRequest
{
    public StringFilterRequest? Code { get; init; }
    public StringFilterRequest? Category { get; init; }
}

public sealed record GetLedgerAccountRequest
{
    public required int Id { get; init; }
}

public sealed record GetLedgerBalancesRequest
{
    public required string Mode { get; init; }
    public required DateOnly To { get; init; }
    public DateOnly? From { get; init; }
    public int? LedgerId { get; init; }
    public int? CostCenterId { get; init; }
}
