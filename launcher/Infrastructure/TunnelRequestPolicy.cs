namespace REvolutionClient.Launcher.Infrastructure;

internal static class TunnelRequestPolicy
{
    private const string NgrokBypassHeader = "ngrok-skip-browser-warning";

    public static void Apply(HttpRequestMessage request, Uri endpoint)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps || endpoint.HostNameType != UriHostNameType.Dns)
            return;

        var host = endpoint.IdnHost;
        if (!host.EndsWith(".ngrok-free.app", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".ngrok-free.dev", StringComparison.OrdinalIgnoreCase))
            return;

        request.Headers.TryAddWithoutValidation(NgrokBypassHeader, "revolution-dev-client");
    }
}
