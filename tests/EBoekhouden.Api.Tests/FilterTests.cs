using EBoekhouden.Api.Filtering;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class FilterTests
{
    [Theory]
    [InlineData("eq", "name=A%26B")]
    [InlineData("not_eq", "name%5Bnot_eq%5D=A%26B")]
    [InlineData("like", "name%5Blike%5D=A%26B")]
    [InlineData("not_like", "name%5Bnot_like%5D=A%26B")]
    public void StringOperatorsHaveExactEncoding(string op, string expected)
    {
        Assert.Equal("?limit=50&offset=0&" + expected,
            QueryEncoder.Encode(new RelationQuery(new(), Name: new(op, "A&B"))));
    }

    [Fact]
    public void EscapesWildcardsUnicodeAndLiteralPercentExactlyOnce()
    {
        var encoded = QueryEncoder.Encode(new RelationQuery(new(), Name: new("like", "%é? +&=/#%25")));
        Assert.Equal("?limit=50&offset=0&name%5Blike%5D=%25%C3%A9%3F%20%2B%26%3D%2F%23%2525", encoded);
        Assert.Equal("%é? +&=/#%25", Uri.UnescapeDataString(encoded.Split('=')[3]));
    }

    [Theory]
    [InlineData("eq", "id=5", "date=2026-01-02")]
    [InlineData("not_eq", "id%5Bnot_eq%5D=5", "date%5Bnot_eq%5D=2026-01-02")]
    [InlineData("gt", "id%5Bgt%5D=5", "date%5Bgt%5D=2026-01-02")]
    [InlineData("gte", "id%5Bgte%5D=5", "date%5Bgte%5D=2026-01-02")]
    [InlineData("lt", "id%5Blt%5D=5", "date%5Blt%5D=2026-01-02")]
    [InlineData("lte", "id%5Blte%5D=5", "date%5Blte%5D=2026-01-02")]
    public void OrderedOperatorsSerializeInvariantly(string op, string number, string date)
    {
        Assert.Equal("?limit=50&offset=0&" + number + "&" + date,
            QueryEncoder.Encode(new EntryQuery(new(), Id: new(op, 5), Date: new(op, new(2026, 1, 2)))));
    }

    [Fact]
    public void RangesAreInclusiveAndSupportInt64AndSameEndpoint()
    {
        Assert.Equal("?limit=2000&offset=10&id%5Brange%5D=2147483648%2C9223372036854775807&date%5Brange%5D=2026-01-02%2C2026-01-02",
            QueryEncoder.Encode(new EntryQuery(new(2000, 10), Id: new("range", 2147483648, long.MaxValue),
                Date: new("range", new(2026, 1, 2), new(2026, 1, 2)))));
    }

    [Theory]
    [InlineData("range")]
    [InlineData("gt")]
    [InlineData("EQ")]
    [InlineData("")]
    [InlineData("eq][like")]
    public void UnsupportedStringOperatorsFail(string op) =>
        Assert.Throws<ArgumentException>(() => new StringFilter(op, "value"));

    [Theory]
    [InlineData("like")]
    [InlineData("not_like")]
    [InlineData("EQ")]
    [InlineData("")]
    public void UnsupportedOrderedOperatorsFail(string op)
    {
        Assert.Throws<ArgumentException>(() => new IntegerFilter(op, 1));
        Assert.Throws<ArgumentException>(() => new DateFilter(op, new(2026, 1, 1)));
    }

    [Fact]
    public void NullAndInvalidRangesFail()
    {
        Assert.Throws<ArgumentNullException>(() => new StringFilter("eq", null!));
        Assert.Throws<ArgumentException>(() => new IntegerFilter("range", 1));
        Assert.Throws<ArgumentException>(() => new IntegerFilter("eq", 1, 2));
        Assert.Throws<ArgumentException>(() => new IntegerFilter("range", 2, 1));
        Assert.Throws<ArgumentException>(() => new DateFilter("range", new(2026, 1, 1)));
        Assert.Throws<ArgumentException>(() => new DateFilter("eq", new(2026, 1, 1), new(2026, 1, 2)));
        Assert.Throws<ArgumentException>(() => new DateFilter("range", new(2026, 1, 2), new(2026, 1, 1)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2001, 0)]
    [InlineData(50, -1)]
    public void InvalidPaginationFails(int limit, int offset) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pagination(limit, offset));

    [Fact]
    public void EndpointsUseOnlyTheirDocumentedParameters()
    {
        Assert.Equal("?limit=50&offset=0&invoiceNumber%5Bnot_eq%5D=SYN&relationId=17&date=2026-02-03",
            QueryEncoder.Encode(new InvoiceQuery(new(), new("not_eq", "SYN"), 17, new("eq", new(2026, 2, 3)))));
        Assert.Equal("?limit=50&offset=0&type=7&description%5Blike%5D=%25synthetic%25&invoiceNumber=X",
            QueryEncoder.Encode(new EntryQuery(new(), 7, Description: new("like", "%synthetic%"), InvoiceNumber: new("eq", "X"))));
        Assert.Equal("?limit=50&offset=0&credDeb=D&invoiceNumber=X",
            QueryEncoder.Encode(new OutstandingQuery(new(), "D", new("eq", "X"))));
        Assert.Equal("?limit=50&offset=0&code%5Blike%5D=7%25&category=NEW",
            QueryEncoder.Encode(new LedgerQuery(new(), new("like", "7%"), new("eq", "NEW"))));
        Assert.Throws<ArgumentException>(() => QueryEncoder.Encode(new LedgerQuery(new(), new("eq", new string('x', 11)))));
        Assert.Throws<ArgumentException>(() => QueryEncoder.Encode(new OutstandingQuery(new(), "receivable")));
        Assert.Throws<ArgumentOutOfRangeException>(() => QueryEncoder.Encode(new EntryQuery(new(), 8)));
    }

    [Fact]
    public void BalanceQueryChoosesOnlySupportedDatesAndRoutes()
    {
        var asOf = new BalanceQuery("as_of", new(2026, 6, 30));
        Assert.Equal("?to=2026-06-30", QueryEncoder.Encode(asOf));
        Assert.Equal("/v1/ledger/balances", QueryEncoder.BalancePath(asOf));
        var period = new BalanceQuery("period_change", new(2026, 6, 30), new(2026, 1, 1), 123, 456);
        Assert.Equal("?costCenterId=456&from=2026-01-01&to=2026-06-30", QueryEncoder.Encode(period));
        Assert.Equal("/v1/ledger/123/balance", QueryEncoder.BalancePath(period));
        Assert.Throws<ArgumentException>(() => new BalanceQuery("other", new(2026, 1, 1)));
        Assert.Throws<ArgumentException>(() => new BalanceQuery("as_of", new(2026, 1, 1), new(2026, 1, 1)));
        Assert.Throws<ArgumentException>(() => new BalanceQuery("period_change", new(2026, 1, 1)));
        Assert.Throws<ArgumentException>(() => new BalanceQuery("period_change", new(2026, 1, 1), new(2026, 1, 2)));
    }
}
