using EBoekhouden.Api.Clients;
using EBoekhouden.Mcp.Contracts;
using EBoekhouden.Mcp.Mapping;
using EBoekhouden.Mcp.Validation;

namespace EBoekhouden.Mcp.Tools;

// Only these ten methods are explicitly registered by AccountingToolCatalog.
public sealed class AccountingTools(IEBoekhoudenReadClient client)
{
    public async Task<PageResult<RelationSummaryResult>> SearchRelationsAsync(SearchRelationsRequest request, CancellationToken cancellationToken)
    {
        var query = RequestMapper.Map(request);
        return CollectionMapper.Map(await client.GetRelationsAsync(query, cancellationToken).ConfigureAwait(false), query.Page, ResultMapper.Map);
    }

    public async Task<RelationResult> GetRelationAsync(GetRelationRequest request, CancellationToken cancellationToken) =>
        ResultMapper.Map(await client.GetRelationAsync(request.Id, cancellationToken).ConfigureAwait(false));

    public async Task<PageResult<InvoiceSummaryResult>> SearchInvoicesAsync(SearchInvoicesRequest request, CancellationToken cancellationToken)
    {
        var query = RequestMapper.Map(request);
        return CollectionMapper.Map(await client.GetInvoicesAsync(query, cancellationToken).ConfigureAwait(false), query.Page, ResultMapper.Map);
    }

    public async Task<InvoiceResult> GetInvoiceAsync(GetInvoiceRequest request, CancellationToken cancellationToken) =>
        ResultMapper.Map(await client.GetInvoiceAsync(request.Id, cancellationToken).ConfigureAwait(false));

    public async Task<PageResult<EntrySummaryResult>> SearchEntriesAsync(SearchEntriesRequest request, CancellationToken cancellationToken)
    {
        var query = RequestMapper.Map(request);
        return CollectionMapper.Map(await client.GetMutationsAsync(query, cancellationToken).ConfigureAwait(false), query.Page, ResultMapper.Map);
    }

    public async Task<EntryResult> GetEntryAsync(GetEntryRequest request, CancellationToken cancellationToken) =>
        ResultMapper.Map(await client.GetMutationAsync(request.Id, cancellationToken).ConfigureAwait(false));

    public async Task<PageResult<OutstandingInvoiceResult>> ListOutstandingInvoicesAsync(ListOutstandingInvoicesRequest request, CancellationToken cancellationToken)
    {
        var query = RequestMapper.Map(request);
        return CollectionMapper.Map(await client.GetOutstandingInvoicesAsync(query, cancellationToken).ConfigureAwait(false), query.Page, ResultMapper.Map);
    }

    public async Task<PageResult<LedgerSummaryResult>> SearchLedgerAccountsAsync(SearchLedgerAccountsRequest request, CancellationToken cancellationToken)
    {
        var query = RequestMapper.Map(request);
        return CollectionMapper.Map(await client.GetLedgersAsync(query, cancellationToken).ConfigureAwait(false), query.Page, ResultMapper.Map);
    }

    public async Task<LedgerResult> GetLedgerAccountAsync(GetLedgerAccountRequest request, CancellationToken cancellationToken) =>
        ResultMapper.Map(await client.GetLedgerAsync(request.Id, cancellationToken).ConfigureAwait(false));

    public async Task<LedgerBalancesResult> GetLedgerBalancesAsync(GetLedgerBalancesRequest request, CancellationToken cancellationToken)
    {
        var query = RequestMapper.Map(request);
        return query.LedgerId.HasValue
            ? CollectionMapper.Map(await client.GetLedgerBalanceAsync(query, cancellationToken).ConfigureAwait(false), query)
            : CollectionMapper.Map(await client.GetLedgerBalancesAsync(query, cancellationToken).ConfigureAwait(false), query);
    }
}
