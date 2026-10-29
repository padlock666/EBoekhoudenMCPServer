using System.Net;
using EBoekhouden.Api.Http;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class ReadCircuitTests
{
    [Fact]
    public async Task ConsecutiveTransientFailuresOpenCircuitAndSuccessfulProbeClosesIt()
    {
        var recover = false;
        using var harness = new HttpHarness(read: (_, _) => Task.FromResult(HttpHarness.Response(recover ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)),
            configure: options => { options.MaxReadAttempts = 1; options.CircuitFailureThreshold = 2; });
        using var first = await harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        using var second = await harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<ApiTransportException>(() => harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken));
        Assert.Equal(TransportFailure.CircuitOpen, failure.Failure);
        Assert.Equal(2, harness.Reads.Calls);
        harness.Time.Advance(TimeSpan.FromSeconds(30));
        recover = true;
        using var probe = await harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        using var after = await harness.Transport.SendAsync(ReadRequest.Relation(1), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(4, harness.Reads.Calls);
    }

    [Fact]
    public void HalfOpenCircuitAllowsOneProbeAndIgnoresOldGenerationCompletions()
    {
        using var harness = new HttpHarness();
        var circuit = new ReadCircuit(harness.Time, 1, TimeSpan.FromSeconds(30));
        using var old = circuit.Enter();
        using var trip = circuit.Enter();
        trip.Failure();
        old.Success();
        Assert.Throws<ApiTransportException>(() => circuit.Enter());
        harness.Time.Advance(TimeSpan.FromSeconds(30));
        using var probe = circuit.Enter();
        Assert.Throws<ApiTransportException>(() => circuit.Enter());
        probe.Failure();
        Assert.Throws<ApiTransportException>(() => circuit.Enter());
        harness.Time.Advance(TimeSpan.FromSeconds(30));
        using (var canceledProbe = circuit.Enter()) { }
        using var recovery = circuit.Enter();
        recovery.Success();
        using var closed = circuit.Enter();
        closed.Success();
    }
}
