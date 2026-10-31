using System.Net;
using System.Text.Json;
using EBoekhouden.Api.Configuration;
using EBoekhouden.Api.Contracts;
using EBoekhouden.Api.Errors;
using EBoekhouden.Api.Filtering;
using EBoekhouden.Api.Http;
using Microsoft.Extensions.Options;

namespace EBoekhouden.Api.Clients;

public sealed class EBoekhoudenReadClient(ReadTransport transport, IOptions<EBoekhoudenOptions> options,
    TimeProvider time) : IEBoekhoudenReadClient
{
    private readonly EBoekhoudenOptions _settings = options.Value;

    public Task<ApiPage<RelationSummaryDto>> GetRelationsAsync(RelationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Page);
        return GetAsync<ApiPage<RelationSummaryDto>>(ReadRequest.Relations(query), r => ValidatePage(r, query.Page.Limit), cancellationToken);
    }
    public Task<RelationDto> GetRelationAsync(int id, CancellationToken cancellationToken) =>
        GetAsync<RelationDto>(ReadRequest.Relation(id), null, cancellationToken);
    public Task<ApiPage<InvoiceSummaryDto>> GetInvoicesAsync(InvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Page);
        return GetAsync<ApiPage<InvoiceSummaryDto>>(ReadRequest.Invoices(query), r => ValidatePage(r, query.Page.Limit), cancellationToken);
    }
    public Task<InvoiceDto> GetInvoiceAsync(int id, CancellationToken cancellationToken) =>
        GetAsync<InvoiceDto>(ReadRequest.Invoice(id), r => ValidateElements(r.Items), cancellationToken);
    public Task<ApiPage<EntrySummaryDto>> GetMutationsAsync(EntryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Page);
        return GetAsync<ApiPage<EntrySummaryDto>>(ReadRequest.Entries(query), r => ValidatePage(r, query.Page.Limit), cancellationToken);
    }
    public Task<EntryDto> GetMutationAsync(int id, CancellationToken cancellationToken) =>
        GetAsync<EntryDto>(ReadRequest.Entry(id), r => { ValidateElements(r.Rows); ValidateElements(r.Vat); }, cancellationToken);
    public Task<ApiPage<OutstandingInvoiceDto>> GetOutstandingInvoicesAsync(OutstandingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Page);
        return GetAsync<ApiPage<OutstandingInvoiceDto>>(ReadRequest.Outstanding(query), r => ValidatePage(r, query.Page.Limit), cancellationToken);
    }
    public Task<ApiPage<LedgerSummaryDto>> GetLedgersAsync(LedgerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Page);
        return GetAsync<ApiPage<LedgerSummaryDto>>(ReadRequest.Ledgers(query), r => ValidatePage(r, query.Page.Limit), cancellationToken);
    }
    public Task<LedgerDto> GetLedgerAsync(int id, CancellationToken cancellationToken) =>
        GetAsync<LedgerDto>(ReadRequest.Ledger(id), null, cancellationToken);

    public Task<ApiPage<LedgerBalanceDto>> GetLedgerBalancesAsync(BalanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.LedgerId.HasValue) throw new ArgumentException("All-ledger balances require an absent ledgerId.", nameof(query));
        return GetAsync<ApiPage<LedgerBalanceDto>>(ReadRequest.Balances(query), r => ValidatePage(r, null), cancellationToken);
    }

    public Task<LedgerBalanceDto> GetLedgerBalanceAsync(BalanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!query.LedgerId.HasValue) throw new ArgumentException("Single-ledger balance requires ledgerId.", nameof(query));
        return GetAsync<LedgerBalanceDto>(ReadRequest.Balances(query), null, cancellationToken);
    }

    private async Task<T> GetAsync<T>(ReadRequest request, Action<T>? validate, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Pass one outer deadline into the transport as caller cancellation, extending the bound through parsing.
        using var deadline = new CancellationTokenSource(_settings.OperationTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var response = await transport.SendAsync(request, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
            {
                // The keyring credential has been released; discard potentially reflected metadata.
                linked.Token.ThrowIfCancellationRequested();
                throw ApiException.Http(response.StatusCode);
            }
            if (response.StatusCode != HttpStatusCode.OK) throw ApiException.InvalidResponse(response.StatusCode);
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
                var result = await JsonSerializer.DeserializeAsync<T>(stream, ApiJson.Options, linked.Token).ConfigureAwait(false);
                if (result is null) throw ApiException.InvalidResponse(response.StatusCode);
                validate?.Invoke(result);
                linked.Token.ThrowIfCancellationRequested();
                return result;
            }
            catch (JsonException)
            {
                linked.Token.ThrowIfCancellationRequested();
                throw ApiException.InvalidResponse(response.StatusCode);
            }
            catch (InvalidDataException)
            {
                linked.Token.ThrowIfCancellationRequested();
                throw ApiException.InvalidResponse(response.StatusCode);
            }
        }
        catch (ApiTransportException error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw ApiException.Transport(error.Failure, error.StatusCode, error.Code, error.TraceId);
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw ApiException.Transport(deadline.IsCancellationRequested ? TransportFailure.Timeout : TransportFailure.Network);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw ApiException.Transport(TransportFailure.Timeout); }
        catch (OperationCanceledException)
        { throw new OperationCanceledException("e-Boekhouden API read canceled.", cancellationToken); }
    }

    private static void ValidatePage<T>(ApiPage<T> page, int? limit)
    {
        if (page.Items is null || page.Count < 0 || page.Count != page.Items.Count || page.Count > limit)
            throw new InvalidDataException("Invalid upstream page envelope.");
        ValidateElements(page.Items);
    }

    private static void ValidateElements<T>(IReadOnlyList<T>? items)
    {
        if (items is not null && items.Any(item => item is null))
            throw new InvalidDataException("Invalid upstream collection element.");
    }
}
