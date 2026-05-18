using McpServer.Api.Auth.Dto;   
namespace McpServer.Api.Auth;

public class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public AuthService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        var email = _config["Api:Username"] ?? throw new InvalidOperationException("Missing Api:Username");
        var password = _config["Api:Password"] ?? throw new InvalidOperationException("Missing Api:Password");

        var payload = new { email, password };

        var response = await _http.PostAsJsonAsync("/auth/sign-in", payload);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InvalidOperationException("Empty token response");

        return token.AccessToken;
    }
}