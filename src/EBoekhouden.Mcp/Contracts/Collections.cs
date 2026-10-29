namespace EBoekhouden.Mcp.Contracts;

public sealed record PageInfo(int Limit, int Offset, int ReturnedCount, long? NextOffset);
public sealed record PageResult<T>(IReadOnlyList<T> Items, PageInfo Page);

// Balances have no upstream pagination; the same result shape handles one or all ledgers.
public sealed record LedgerBalancesResult(string Mode, DateOnly? From, DateOnly To,
    int? LedgerId, int? CostCenterId, IReadOnlyList<LedgerBalanceResult> Items, int ReturnedCount);
