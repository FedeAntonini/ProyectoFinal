namespace McpServer.Api.Turnera;

public class TurneraApiKeyHandler : DelegatingHandler
{
    private readonly IConfiguration _config;

    public TurneraApiKeyHandler(IConfiguration config)
    {
        _config = config;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var apiKey = _config["Turnera:AgentApiKey"]
            ?? throw new InvalidOperationException("Missing Turnera:AgentApiKey");

        request.Headers.Add("x-api-key", apiKey);
        return base.SendAsync(request, cancellationToken);
    }
}