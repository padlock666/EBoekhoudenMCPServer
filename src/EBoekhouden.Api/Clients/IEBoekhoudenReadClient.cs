using EBoekhouden.Api.Contracts;
using EBoekhouden.Api.Filtering;

namespace EBoekhouden.Api.Clients;

public interface IEBoekhoudenReadClient
{
    Task<ApiPage<RelationSummaryDto>> GetRelationsAsync(RelationQuery query, CancellationToken cancellationToken);
    Task<RelationDto> GetRelationAsync(int id, CancellationToken cancellationToken);
    Task<ApiPage<InvoiceSummaryDto>> GetInvoicesAsync(InvoiceQuery query, CancellationToken cancellationToken);
    Task<InvoiceDto> GetInvoiceAsync(int id, CancellationToken cancellationToken);
    Task<ApiPage<EntrySummaryDto>> GetMutationsAsync(EntryQuery query, CancellationToken cancellationToken);
    Task<EntryDto> GetMutationAsync(int id, CancellationToken cancellationToken);
    Task<ApiPage<OutstandingInvoiceDto>> GetOutstandingInvoicesAsync(OutstandingQuery query, CancellationToken cancellationToken);
    Task<ApiPage<LedgerSummaryDto>> GetLedgersAsync(LedgerQuery query, CancellationToken cancellationToken);
    Task<LedgerDto> GetLedgerAsync(int id, CancellationToken cancellationToken);
    Task<ApiPage<LedgerBalanceDto>> GetLedgerBalancesAsync(BalanceQuery query, CancellationToken cancellationToken);
    Task<LedgerBalanceDto> GetLedgerBalanceAsync(BalanceQuery query, CancellationToken cancellationToken);
}
