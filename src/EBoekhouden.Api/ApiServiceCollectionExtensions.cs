using System.Net;
using EBoekhouden.Api.Authentication;
using EBoekhouden.Api.Clients;
using EBoekhouden.Api.Configuration;
using EBoekhouden.Api.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EBoekhouden.Api;

public static class ApiServiceCollectionExtensions
{
    public const string SessionClientName = "EBoekhouden.Session";
    public const string ReadClientName = "EBoekhouden.Read";

    public static IServiceCollection AddEBoekhoudenApi(this IServiceCollection services, IConfiguration configuration) =>
        Add(services, configuration, new Uri("https://api.e-boekhouden.nl/"));

    // Test-only registration is a deliberate code choice, never a configuration/environment switch.
    public static IServiceCollection AddEBoekhoudenApiForTesting(this IServiceCollection services,
        IConfiguration configuration, Uri loopbackOrigin)
    {
        ArgumentNullException.ThrowIfNull(loopbackOrigin);
        if (!loopbackOrigin.IsAbsoluteUri || loopbackOrigin.Scheme is not ("http" or "https") ||
            !IPAddress.TryParse(loopbackOrigin.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address) ||
            loopbackOrigin.AbsolutePath != "/" || loopbackOrigin.Query.Length != 0 ||
            loopbackOrigin.Fragment.Length != 0 || loopbackOrigin.UserInfo.Length != 0)
            throw new ArgumentException("Test origin must be a numeric loopback HTTP(S) origin with root path.", nameof(loopbackOrigin));
        return Add(services, configuration, loopbackOrigin);
    }

    private static IServiceCollection Add(IServiceCollection services, IConfiguration configuration, Uri origin)
    {
        if (services.Any(s => s.ServiceType == typeof(SessionTokenProvider)))
            throw new InvalidOperationException("Only one e-Boekhouden credential registration is supported.");
        services.AddOptions<EBoekhoudenOptions>().Configure(settings =>
        {
            var section = configuration.GetSection(EBoekhoudenOptions.SectionName);
            CredentialConfiguration.Validate(section);
            section.Bind(settings);
        }).ValidateOnStart();
        services.AddSingleton<IValidateOptions<EBoekhoudenOptions>, EBoekhoudenOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordSource>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<EBoekhoudenOptions>>().Value;
            return new KeyringPasswordSource(settings.Keyring);
        });
        services.AddSingleton<SessionTokenProvider>();
        services.AddSingleton(sp => new ReadTransport(sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<SessionTokenProvider>(), sp.GetRequiredService<IOptions<EBoekhoudenOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IEBoekhoudenReadClient, EBoekhoudenReadClient>();
        AddClient(SessionClientName, true);
        AddClient(ReadClientName, false);
        return services;

        void AddClient(string name, bool session)
        {
            services.AddHttpClient(name, client =>
                {
                    client.BaseAddress = origin;
                    client.Timeout = Timeout.InfiniteTimeSpan; // Single explicit timeout owner, including body buffering.
                    client.MaxResponseContentBufferSize = session ? 16 * 1024 : 16 * 1024 * 1024;
                })
                .RemoveAllLoggers()
                .ConfigurePrimaryHttpMessageHandler(sp => CreatePrimaryHandler(sp.GetRequiredService<IOptions<EBoekhoudenOptions>>().Value))
                .AddHttpMessageHandler(() => new RequestGuardHandler(origin, session));
        }
    }

    internal static SocketsHttpHandler CreatePrimaryHandler(EBoekhoudenOptions options) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        MaxConnectionsPerServer = options.MaxConcurrentReads + 1,
        ConnectTimeout = options.AttemptTimeout,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
}
