using System.Globalization;
using EBoekhouden.Api.Filtering;

namespace EBoekhouden.Api.Http;

// Closed request construction: no public URL, path, method, or arbitrary query input.
public sealed class ReadRequest
{
    internal string PathAndQuery { get; }
    private ReadRequest(string path, string query = "") => PathAndQuery = path + query;

    public static ReadRequest Relations(RelationQuery query) => new("/v1/relation", QueryEncoder.Encode(query));
    public static ReadRequest Relation(int id) => new(Detail("relation", id));
    public static ReadRequest Invoices(InvoiceQuery query) => new("/v1/invoice", QueryEncoder.Encode(query));
    public static ReadRequest Invoice(int id) => new(Detail("invoice", id));
    public static ReadRequest Entries(EntryQuery query) => new("/v1/mutation", QueryEncoder.Encode(query));
    public static ReadRequest Entry(int id) => new(Detail("mutation", id));
    public static ReadRequest Outstanding(OutstandingQuery query) => new("/v1/mutation/invoice/outstanding", QueryEncoder.Encode(query));
    public static ReadRequest Ledgers(LedgerQuery query) => new("/v1/ledger", QueryEncoder.Encode(query));
    public static ReadRequest Ledger(int id) => new(Detail("ledger", id));
    public static ReadRequest Balances(BalanceQuery query) => new(QueryEncoder.BalancePath(query), QueryEncoder.Encode(query));

    private static string Detail(string resource, int id) => "/v1/" + resource + "/" + id.ToString(CultureInfo.InvariantCulture);
    public override string ToString() => nameof(ReadRequest);
}
