namespace McpServer.Api.Auth;

public interface IAuthService
{
    Task<string> GetAccessTokenAsync();
}