using EBoekhouden.Api.Contracts;
using EBoekhouden.Api.Filtering;
using EBoekhouden.Mcp.Mapping;
using Xunit;

namespace EBoekhouden.Mcp.Tests;

public sealed class PaginationTests
{
    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, 12L)]
    public void OnlyFullPagesHaveCandidateNextOffset(int count, long? expected)
    {
        var page = new ApiPage<RelationSummaryDto> { Count = count,
            Items = Enumerable.Range(1, count).Select(id => new RelationSummaryDto { Id = id }).ToArray() };
        var result = CollectionMapper.Map(page, new Pagination(2, 10), ResultMapper.Map);
        Assert.Equal(expected, result.Page.NextOffset);
        Assert.Equal(count, result.Page.ReturnedCount);
        Assert.Equal(count, result.Items.Count);
        Assert.Equal(10, result.Page.Offset);
        Assert.Equal(2, result.Page.Limit);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void InconsistentCountIsAnErrorInsteadOfSuccess(int count) =>
        Assert.Throws<InvalidDataException>(() => CollectionMapper.Map(
            new ApiPage<RelationSummaryDto> { Count = count, Items = [new() { Id = 1 }] },
            new Pagination(), ResultMapper.Map));

    [Fact]
    public void OversizedAndNullPagesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => CollectionMapper.Map(
            new ApiPage<RelationSummaryDto> { Count = 2, Items = [new() { Id = 1 }, new() { Id = 2 }] },
            new Pagination(1), ResultMapper.Map));
        Assert.Throws<InvalidDataException>(() => CollectionMapper.Map(
            new ApiPage<RelationSummaryDto> { Count = 1, Items = [null!] }, new Pagination(), ResultMapper.Map));
    }

    [Fact]
    public void CandidateOffsetDoesNotOverflowOrClaimToBeAnAcceptedNextRequest()
    {
        var page = new ApiPage<RelationSummaryDto> { Count = 1, Items = [new() { Id = 1 }] };
        var result = CollectionMapper.Map(page, new Pagination(1, int.MaxValue), ResultMapper.Map);
        Assert.Equal(2147483648L, result.Page.NextOffset);
        // Offset inputs remain int32 as specified by upstream; caller cannot traverse beyond that.
    }
}
