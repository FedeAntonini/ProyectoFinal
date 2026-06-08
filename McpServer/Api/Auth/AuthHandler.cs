using System.Net.Http.Headers;

namespace McpServer.Api.Auth;

public class AuthHandler : DelegatingHandler
{
    private readonly IAuthService _authService;

    public AuthHandler(IAuthService authService)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _authService.GetAccessTokenAsync();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}