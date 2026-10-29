namespace EBoekhouden.Api.Filtering;

public sealed record StringFilter
{
    public string Op { get; }
    public string Value { get; }

    public StringFilter(string op, string value)
    {
        if (op is not ("eq" or "not_eq" or "like" or "not_like"))
            throw new ArgumentException("Unsupported string filter operator.", nameof(op));
        ArgumentNullException.ThrowIfNull(value);
        Op = op;
        Value = value;
    }
}

public sealed record IntegerFilter
{
    public string Op { get; }
    public long Value { get; }
    public long? To { get; }

    public IntegerFilter(string op, long value, long? to = null)
    {
        FilterValidation.Ordered(op, value, to);
        Op = op;
        Value = value;
        To = to;
    }
}

public sealed record DateFilter
{
    public string Op { get; }
    public DateOnly Value { get; }
    public DateOnly? To { get; }

    public DateFilter(string op, DateOnly value, DateOnly? to = null)
    {
        FilterValidation.Ordered(op, value, to);
        Op = op;
        Value = value;
        To = to;
    }
}

internal static class FilterValidation
{
    public static void Ordered<T>(string op, T value, T? to) where T : struct, IComparable<T>
    {
        if (op is not ("eq" or "not_eq" or "gt" or "gte" or "lt" or "lte" or "range"))
            throw new ArgumentException("Unsupported ordered filter operator.", nameof(op));
        if ((op == "range") != to.HasValue)
            throw new ArgumentException("Only range requires a second endpoint.", nameof(to));
        if (to.HasValue && value.CompareTo(to.Value) > 0)
            throw new ArgumentException("Range endpoints must be ordered.", nameof(to));
    }
}

public sealed record Pagination
{
    public int Limit { get; }
    public int Offset { get; }

    public Pagination(int limit = 50, int offset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 2000);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        Limit = limit;
        Offset = offset;
    }
}
