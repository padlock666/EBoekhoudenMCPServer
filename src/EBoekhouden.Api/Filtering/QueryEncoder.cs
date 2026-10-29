using System.Globalization;

namespace EBoekhouden.Api.Filtering;

// Overloads restrict fields to the supplied specification. No caller-supplied keys or URLs.
public static class QueryEncoder
{
    public static string Encode(RelationQuery query) => Build(query.Page,
        S("code", query.Code), S("type", query.Type), S("email", query.Email),
        S("name", query.Name), S("contact", query.Contact), S("city", query.City));

    public static string Encode(InvoiceQuery query) => Build(query.Page,
        S("invoiceNumber", query.InvoiceNumber), Plain("relationId", query.RelationId), D("date", query.Date));

    public static string Encode(EntryQuery query)
    {
        if (query.Type is < 1 or > 7)
            throw new ArgumentOutOfRangeException(nameof(query), "Unsupported entry type.");
        return Build(query.Page, Plain("type", query.Type), I("id", query.Id),
            S("description", query.Description), S("invoiceNumber", query.InvoiceNumber), D("date", query.Date));
    }

    public static string Encode(OutstandingQuery query)
    {
        if (query.CredDeb is not ("C" or "D"))
            throw new ArgumentException("Expected C or D.", nameof(query));
        return Build(query.Page, ("credDeb", query.CredDeb), S("invoiceNumber", query.InvoiceNumber));
    }

    public static string Encode(LedgerQuery query)
    {
        if (query.Code?.Value.Length > 10 || query.Category?.Value.Length > 10)
            throw new ArgumentException("Ledger filters have a maximum length of 10.", nameof(query));
        return Build(query.Page, S("code", query.Code), S("category", query.Category));
    }

    public static string Encode(BalanceQuery query) => Build(null,
        Plain("costCenterId", query.CostCenterId),
        query.From is { } from ? ("from", Date(from)) : null, ("to", Date(query.To)));

    public static string BalancePath(BalanceQuery query) => query.LedgerId is { } id
        ? "/v1/ledger/" + id.ToString(CultureInfo.InvariantCulture) + "/balance"
        : "/v1/ledger/balances";

    private static (string, string)? S(string key, StringFilter? filter) => filter is null
        ? null : (Key(key, filter.Op), filter.Value);
    private static (string, string)? I(string key, IntegerFilter? filter) => filter is null
        ? null : (Key(key, filter.Op), filter.Value.ToString(CultureInfo.InvariantCulture) +
            (filter.To is { } to ? "," + to.ToString(CultureInfo.InvariantCulture) : ""));
    private static (string, string)? D(string key, DateFilter? filter) => filter is null
        ? null : (Key(key, filter.Op), Date(filter.Value) + (filter.To is { } to ? "," + Date(to) : ""));
    private static (string, string)? Plain(string key, int? value) => value is { } v
        ? (key, v.ToString(CultureInfo.InvariantCulture)) : null;
    private static string Key(string key, string op) => op == "eq" ? key : $"{key}[{op}]";
    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Build(Pagination? page, params (string Key, string Value)?[] fields)
    {
        var pairs = new List<(string Key, string Value)>();
        if (page is not null)
        {
            pairs.Add(("limit", page.Limit.ToString(CultureInfo.InvariantCulture)));
            pairs.Add(("offset", page.Offset.ToString(CultureInfo.InvariantCulture)));
        }
        pairs.AddRange(fields.Where(f => f.HasValue).Select(f => f!.Value));
        return "?" + string.Join("&", pairs.Select(p =>
            Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    }
}
