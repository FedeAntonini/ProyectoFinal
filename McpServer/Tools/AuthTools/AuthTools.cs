using McpServer.Api.Auth;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpServer.Tools;

[McpServerToolType]
public class AuthTools
{
    private readonly IAuthService _authService;

    public AuthTools(IAuthService authService)
    {
        _authService = authService;
    }

    [McpServerTool, Description("Retrieves a valid access token from the backend API. Returns a cached token if one is still valid.")]
    public async Task<string> GetAccessToken()
    {
        return await _authService.GetAccessTokenAsync();
    }
}