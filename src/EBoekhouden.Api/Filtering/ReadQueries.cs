namespace EBoekhouden.Api.Filtering;

public sealed record RelationQuery(Pagination Page, StringFilter? Code = null, StringFilter? Type = null,
    StringFilter? Email = null, StringFilter? Name = null, StringFilter? Contact = null, StringFilter? City = null);
public sealed record InvoiceQuery(Pagination Page, StringFilter? InvoiceNumber = null,
    int? RelationId = null, DateFilter? Date = null);
public sealed record EntryQuery(Pagination Page, int? Type = null, IntegerFilter? Id = null,
    StringFilter? Description = null, StringFilter? InvoiceNumber = null, DateFilter? Date = null);
public sealed record OutstandingQuery(Pagination Page, string CredDeb, StringFilter? InvoiceNumber = null);
public sealed record LedgerQuery(Pagination Page, StringFilter? Code = null, StringFilter? Category = null);

// No pagination on the balance routes. Mode semantics are validated by this constructor.
public sealed record BalanceQuery
{
    public string Mode { get; }
    public DateOnly To { get; }
    public DateOnly? From { get; }
    public int? LedgerId { get; }
    public int? CostCenterId { get; }

    public BalanceQuery(string mode, DateOnly to, DateOnly? from = null, int? ledgerId = null, int? costCenterId = null)
    {
        if (mode is not ("as_of" or "period_change"))
            throw new ArgumentException("Unsupported balance mode.", nameof(mode));
        if ((mode == "period_change") != from.HasValue || from > to)
            throw new ArgumentException("Invalid balance dates.", nameof(from));
        Mode = mode;
        To = to;
        From = from;
        LedgerId = ledgerId;
        CostCenterId = costCenterId;
    }
}
