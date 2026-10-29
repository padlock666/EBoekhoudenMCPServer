using System.Net;
using EBoekhouden.Api.Clients;
using EBoekhouden.Api.Filtering;
using Microsoft.Extensions.DependencyInjection;

namespace EBoekhouden.Api.Tests;

internal static class ClientTestSupport
{
    public static string Fixture(string name)
    {
        using var stream = typeof(ClientTestSupport).Assembly.GetManifestResourceStream(
            "EBoekhouden.Api.Tests.Fixtures." + name + ".synthetic.json")
            ?? throw new InvalidOperationException("Missing synthetic fixture: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IEBoekhoudenReadClient Client(this HttpHarness harness) => harness.Services.GetRequiredService<IEBoekhoudenReadClient>();

    public static Task Call(IEBoekhoudenReadClient client, string endpoint, CancellationToken cancellationToken) => endpoint switch
    {
        "relations" => client.GetRelationsAsync(new(new()), cancellationToken),
        "relation" => client.GetRelationAsync(17, cancellationToken),
        "invoices" => client.GetInvoicesAsync(new(new()), cancellationToken),
        "invoice" => client.GetInvoiceAsync(23, cancellationToken),
        "mutations" => client.GetMutationsAsync(new(new()), cancellationToken),
        "mutation" => client.GetMutationAsync(12, cancellationToken),
        "outstanding" => client.GetOutstandingInvoicesAsync(new(new(), "D"), cancellationToken),
        "ledgers" => client.GetLedgersAsync(new(new()), cancellationToken),
        "ledger" => client.GetLedgerAsync(7, cancellationToken),
        "balances" => client.GetLedgerBalancesAsync(new("as_of", new(2026, 3, 31)), cancellationToken),
        "balance" => client.GetLedgerBalanceAsync(new("as_of", new(2026, 3, 31), ledgerId: 7), cancellationToken),
        _ => throw new ArgumentException("Unknown test endpoint.", nameof(endpoint)),
    };

    public static TrackedJsonContent Json(string value) => new(value);
    public static HttpResponseMessage Response(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = Json(value) };

    internal sealed class TrackedJsonContent(string value) : StringContent(value, System.Text.Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
