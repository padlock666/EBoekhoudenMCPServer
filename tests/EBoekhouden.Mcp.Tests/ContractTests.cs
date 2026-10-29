using System.Globalization;
using System.Text.Json;
using EBoekhouden.Api.Contracts;
using EBoekhouden.Api.Filtering;
using EBoekhouden.Mcp.Contracts;
using EBoekhouden.Mcp.Mapping;
using EBoekhouden.Mcp.Validation;
using Xunit;

namespace EBoekhouden.Mcp.Tests;

public sealed class ContractTests
{
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, ContractJson.Options)!;

    [Fact]
    public void RequestsDefaultPaginationAndMapRawFilters()
    {
        var query = RequestMapper.Map(Read<SearchRelationsRequest>("""{"name":{"op":"like","value":"%Synthetic%"}}"""));
        Assert.Equal(50, query.Page.Limit);
        Assert.Equal(0, query.Page.Offset);
        Assert.Equal("?limit=50&offset=0&name%5Blike%5D=%25Synthetic%25", QueryEncoder.Encode(query));
        var invoices = RequestMapper.Map(Read<SearchInvoicesRequest>("""
            {"relationId":4,"date":{"op":"range","value":"2026-01-01","to":"2026-03-31"}}
            """));
        Assert.Equal("?limit=50&offset=0&relationId=4&date%5Brange%5D=2026-01-01%2C2026-03-31", QueryEncoder.Encode(invoices));
    }

    [Theory]
    [InlineData("{\"sort\":\"name\"}")]
    [InlineData("{\"Name\":null}")]
    [InlineData("{\"name\":{\"op\":\"eq\"}}")]
    [InlineData("{\"name\":{\"op\":\"eq\",\"value\":null}}")]
    [InlineData("{\"name\":{\"op\":\"eq\",\"op\":\"like\",\"value\":\"a\"}}")]
    [InlineData("{\"name\":{\"op\":\"eq\",\"value\":\"a\",\"to\":\"z\"}}")]
    [InlineData("{\"name\":[{\"op\":\"eq\",\"value\":\"a\"}]}")]
    public void AmbiguousUnsupportedAndMalformedInputsFail(string json) =>
        Assert.Throws<JsonException>(() => Read<SearchRelationsRequest>(json));

    [Theory]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":2001}")]
    [InlineData("{\"offset\":-1}")]
    [InlineData("{\"name\":{\"op\":\"gt\",\"value\":\"a\"}}")]
    public void ValidationRejectsRequestsBeforeAnyTransport(string json) =>
        Assert.ThrowsAny<ArgumentException>(() => RequestMapper.Map(Read<SearchRelationsRequest>(json)));

    [Theory]
    [InlineData("invoice_received", 1)]
    [InlineData("invoice_sent", 2)]
    [InlineData("invoice_payment_received", 3)]
    [InlineData("invoice_payment_sent", 4)]
    [InlineData("money_received", 5)]
    [InlineData("money_sent", 6)]
    [InlineData("journal_entry", 7)]
    public void EntryTypesMapToDocumentedNumbers(string type, int expected) =>
        Assert.Equal(expected, RequestMapper.Map(new SearchEntriesRequest { Type = type }).Type);

    [Theory]
    [InlineData("receivable", "D")]
    [InlineData("payable", "C")]
    public void OutstandingSideMapsToCredDeb(string side, string expected) =>
        Assert.Equal(expected, RequestMapper.Map(new ListOutstandingInvoicesRequest { Side = side }).CredDeb);

    [Fact]
    public void PlainParametersCannotAcceptFiltersAndIdentityWidthsAreExplicit()
    {
        Assert.Throws<JsonException>(() => Read<SearchInvoicesRequest>("""{"relationId":{"op":"eq","value":1}}"""));
        Assert.Throws<JsonException>(() => Read<SearchEntriesRequest>("""{"type":{"op":"eq","value":1}}"""));
        Assert.Throws<JsonException>(() => Read<GetEntryRequest>("""{"id":2147483648}"""));
        Assert.Throws<JsonException>(() => Read<GetInvoiceRequest>("""{}"""));
        Assert.Throws<JsonException>(() => Read<GetRelationRequest>("""{}"""));
        Assert.Throws<JsonException>(() => Read<GetLedgerAccountRequest>("""{}"""));
        Assert.Throws<JsonException>(() => Read<GetEntryRequest>("""{}"""));
        Assert.Equal(int.MinValue, Read<GetEntryRequest>("""{"id":-2147483648}""").Id);
        var search = RequestMapper.Map(Read<SearchEntriesRequest>("""{"id":{"op":"eq","value":9223372036854775807}}"""));
        Assert.Equal(long.MaxValue, search.Id!.Value);
        Assert.Throws<ArgumentException>(() => RequestMapper.Map(new SearchEntriesRequest { Type = "other" }));
        Assert.Throws<ArgumentException>(() => RequestMapper.Map(new ListOutstandingInvoicesRequest { Side = "D" }));
        Assert.Throws<ArgumentException>(() => RequestMapper.Map(new SearchLedgerAccountsRequest
        { Category = new() { Op = "eq", Value = "12345678901" } }));
    }

    [Theory]
    [InlineData("{\"date\":{\"op\":\"eq\"}}")]
    [InlineData("{\"date\":{\"op\":\"eq\",\"value\":\"2026-13-01\"}}")]
    [InlineData("{\"id\":{\"op\":\"eq\"}}")]
    public void TypedFiltersRequireTypedValues(string json) =>
        Assert.Throws<JsonException>(() => Read<SearchEntriesRequest>(json));

    [Fact]
    public void BalanceMappingRetainsInterpretationAndDates()
    {
        var request = Read<GetLedgerBalancesRequest>("""
            {"mode":"period_change","from":"2026-01-01","to":"2026-03-31","ledgerId":123,"costCenterId":45}
            """);
        var query = RequestMapper.Map(request);
        var result = CollectionMapper.Map(new LedgerBalanceDto { Code = "SYN", Type = "DEB", Balance = -0.0001m }, query);
        Assert.Equal("period_change", result.Mode);
        Assert.Equal(new DateOnly(2026, 1, 1), result.From);
        Assert.Equal(new DateOnly(2026, 3, 31), result.To);
        Assert.Equal(123, result.LedgerId);
        Assert.Equal(45, result.CostCenterId);
        Assert.Equal("-0.0001", Assert.Single(result.Items).Balance);
        Assert.Equal(1, result.ReturnedCount);
        Assert.Equal("?costCenterId=45&from=2026-01-01&to=2026-03-31", QueryEncoder.Encode(query));

        var all = RequestMapper.Map(Read<GetLedgerBalancesRequest>("""{"mode":"as_of","to":"2026-03-31"}"""));
        var empty = CollectionMapper.Map(new ApiPage<LedgerBalanceDto> { Items = [], Count = 0 }, all);
        Assert.Null(empty.From);
        Assert.Empty(empty.Items);
        Assert.Equal("?to=2026-03-31", QueryEncoder.Encode(all));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"mode\":\"as_of\"}")]
    [InlineData("{\"mode\":\"as_of\",\"to\":\"2026-01-01\",\"limit\":50}")]
    public void BalanceRequiresModeAndToAndDisallowsPagination(string json) =>
        Assert.Throws<JsonException>(() => Read<GetLedgerBalancesRequest>(json));

    [Fact]
    public void MonetaryAndQuantityStringsPreservePrecisionAcrossCultures()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            var dto = JsonSerializer.Deserialize<InvoiceDto>("""
                {"id":5,"date":"2026-01-31","totalAmount":12345678901234567890.12345678,
                 "items":[{"description":"Synthetic","vatCode":"FUTURE","ledgerId":9,
                           "quantity":0.0001,"amount":3,"pricePerUnit":1.2300,"discountPercentage":0.5}]}
                """, ApiJson.Options)!;
            var result = ResultMapper.Map(dto);
            var output = JsonSerializer.SerializeToElement(result, ContractJson.Options);
            Assert.Equal("12345678901234567890.12345678", output.GetProperty("totalAmount").GetString());
            Assert.Equal(JsonValueKind.Null, output.GetProperty("totalExcl").ValueKind);
            Assert.Equal("2026-01-31", output.GetProperty("date").GetString());
            var item = Assert.Single(result.Items!);
            Assert.Equal("0.0001", item.Quantity);
            Assert.Equal("3", item.Amount);
            Assert.Equal("1.2300", item.PricePerUnit);
            Assert.Equal("0.5", item.DiscountPercentage);
            Assert.Equal("FUTURE", item.VatCode);
            // Deprecated quantity is retained separately, never substituted or added.
            Assert.Null(ResultMapper.Map(new InvoiceItemDto { Description = "Synthetic", VatCode = "X", LedgerId = 1, Amount = 2m }).Quantity);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void InvoiceAndMutationIdentitiesAndUnknownCodesStayDistinct()
    {
        var entry = ResultMapper.Map(new EntryDto { Id = long.MaxValue, Date = new(2026, 1, 1), Type = 99,
            Rows = [new() { VatCode = "UNKNOWN", Amount = 1.23m }], Vat = [new() { VatCode = "UNKNOWN", Amount = 0.23m }] });
        Assert.Equal("9223372036854775807", entry.Id);
        Assert.Equal(99, entry.Type);
        Assert.Equal("1.23", Assert.Single(entry.Rows!).Amount);
        Assert.Equal("0.23", Assert.Single(entry.Vat!).Amount);
        var outstanding = ResultMapper.Map(new OutstandingInvoiceDto { MutationId = 42, Date = new(2026, 1, 1), OutstandingAmount = 10m });
        var json = JsonSerializer.SerializeToElement(outstanding, ContractJson.Options);
        Assert.Equal("42", json.GetProperty("mutationId").GetString());
        Assert.False(json.TryGetProperty("invoiceId", out _));
        Assert.Equal("FUTURE", ResultMapper.Map(new LedgerDto { Id = 1, Category = "FUTURE" }).Category);
        var summary = JsonSerializer.SerializeToElement(ResultMapper.Map(new RelationSummaryDto { Id = 1 }), ContractJson.Options);
        Assert.Equal(3, summary.EnumerateObject().Count());
        Assert.False(summary.TryGetProperty("name", out _));
    }

    [Fact]
    public void MissingNestedCollectionsRemainUnknownAndNullElementsFailMapping()
    {
        var invoice = new InvoiceDto { Id = 1, Date = new(2026, 1, 1) };
        Assert.Null(ResultMapper.Map(invoice).Items);
        Assert.Empty(ResultMapper.Map(invoice with { Items = [] }).Items!);
        Assert.Throws<InvalidDataException>(() => ResultMapper.Map(invoice with { Items = [null!] }));
        var entry = new EntryDto { Id = 2, Date = new(2026, 1, 1) };
        Assert.Null(ResultMapper.Map(entry).Rows);
        Assert.Throws<InvalidDataException>(() => ResultMapper.Map(entry with { Rows = [null!] }));
        Assert.Throws<InvalidDataException>(() => ResultMapper.Map(entry with { Vat = [null!] }));
    }
}
