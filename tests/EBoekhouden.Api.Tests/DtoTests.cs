using System.Text.Json;
using EBoekhouden.Api.Contracts;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class DtoTests
{
    [Theory]
    [InlineData("{\"Id\":42,\"Type\":\"B\",\"Code\":\"SYN\"}")]
    [InlineData("{\"id\":42,\"type\":\"B\",\"code\":\"SYN\",\"future\":true}")]
    public void UpstreamCasingAndAdditiveFieldsAreAccepted(string json)
    {
        var item = JsonSerializer.Deserialize<RelationSummaryDto>(json, ApiJson.Options)!;
        Assert.Equal(42, item.Id);
        Assert.Equal("SYN", item.Code);
        Assert.Equal("B", item.Type);
    }

    [Fact]
    public void DateOnlyAndDecimalsHaveNoFloatingPointOrTimezoneConversion()
    {
        var item = JsonSerializer.Deserialize<InvoiceDto>("""
            {"id":1,"date":"2024-02-29","totalAmount":12345678901234567890.12345678,
             "items":[{"description":"Synthetic","vatCode":"FUTURE_VAT","ledgerId":2,"quantity":0.0001,"amount":9}]}
            """, ApiJson.Options)!;
        Assert.Equal(new DateOnly(2024, 2, 29), item.Date);
        Assert.Equal(12345678901234567890.12345678m, item.TotalAmount);
        Assert.Null(item.TotalExcl);
        Assert.Equal(0.0001m, Assert.Single(item.Items!).Quantity);
        Assert.Equal("FUTURE_VAT", item.Items![0].VatCode);
        Assert.Contains("\"date\":\"2024-02-29\"", JsonSerializer.Serialize(item, ApiJson.Options));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":null}")]
    [InlineData("{\"id\":\"42\"}")]
    public void EssentialIdentityIsNotSilentlyDefaulted(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RelationDto>(json, ApiJson.Options));

    [Theory]
    [InlineData("{\"id\":1}")]
    [InlineData("{\"id\":1,\"date\":null}")]
    [InlineData("{\"id\":1,\"date\":\"2026-02-30\"}")]
    [InlineData("{\"id\":1,\"date\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{\"id\":1,\"date\":\"2026-01-01\",\"totalAmount\":\"10.20\"}")]
    [InlineData("{\"id\":1,\"date\":\"2026-01-01\",\"totalAmount\":1e100}")]
    public void MalformedDatesAndFinancialTypesFail(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InvoiceDto>(json, ApiJson.Options));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"description\":\"Synthetic\",\"ledgerId\":1}")]
    [InlineData("{\"description\":null,\"vatCode\":\"GEEN\",\"ledgerId\":1}")]
    public void InvoiceItemRequiredFieldsAreEnforced(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InvoiceItemDto>(json, ApiJson.Options));

    [Fact]
    public void UnknownCodesAndWideMutationIdsSurvive()
    {
        var ledger = JsonSerializer.Deserialize<LedgerDto>("""{"id":1,"category":"FUTURE"}""", ApiJson.Options)!;
        var entry = JsonSerializer.Deserialize<EntryDto>("""{"id":9223372036854775807,"date":"2026-01-01","type":99}""", ApiJson.Options)!;
        Assert.Equal("FUTURE", ledger.Category);
        Assert.Equal(long.MaxValue, entry.Id);
        Assert.Equal(99, entry.Type);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LedgerDto>("""{"id":2147483648}""", ApiJson.Options));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":null,\"count\":0}")]
    public void PageEnvelopeRequiresItemsAndRetrievedCount(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApiPage<RelationSummaryDto>>(json, ApiJson.Options));

    [Theory]
    [InlineData("{\"code\":\"1000\",\"type\":\"DEB\"}")]
    [InlineData("{\"code\":null,\"type\":\"DEB\",\"balance\":0}")]
    public void MissingBalanceCannotBecomeZero(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LedgerBalanceDto>(json, ApiJson.Options));
}
