using EBoekhouden.Api.Contracts;
using EBoekhouden.Api.Filtering;
using EBoekhouden.Mcp.Contracts;

namespace EBoekhouden.Mcp.Mapping;

public static class CollectionMapper
{
    public static PageResult<TResult> Map<TSource, TResult>(ApiPage<TSource> source,
        Pagination pagination, Func<TSource, TResult> map)
    {
        ValidatePage(source);
        if (source.Count > pagination.Limit)
            throw new InvalidDataException("Upstream page exceeds requested limit.");
        var items = source.Items.Select(map).ToArray();
        return new(items, new(pagination.Limit, pagination.Offset, source.Count,
            source.Count == pagination.Limit ? (long)pagination.Offset + pagination.Limit : null));
    }

    public static LedgerBalancesResult Map(ApiPage<LedgerBalanceDto> source, BalanceQuery query)
    {
        if (query.LedgerId.HasValue)
            throw new ArgumentException("Expected all-ledger query for a list response.", nameof(query));
        ValidatePage(source);
        return new(query.Mode, query.From, query.To, query.LedgerId, query.CostCenterId,
            source.Items.Select(ResultMapper.Map).ToArray(), source.Count);
    }

    public static LedgerBalancesResult Map(LedgerBalanceDto source, BalanceQuery query)
    {
        if (!query.LedgerId.HasValue)
            throw new ArgumentException("Expected single-ledger query for a detail response.", nameof(query));
        return new(query.Mode, query.From, query.To, query.LedgerId, query.CostCenterId,
            [ResultMapper.Map(source)], 1);
    }

    private static void ValidatePage<T>(ApiPage<T> source)
    {
        if (source.Items is null || source.Count < 0 || source.Count != source.Items.Count ||
            source.Items.Any(item => item is null))
            throw new InvalidDataException("Upstream items and retrieved count are inconsistent.");
    }
}
