using McpServer.Api.Turnera.Dto;

namespace McpServer.Api.Turnera;

public class TurneraService : ITurneraService
{
    private readonly HttpClient _http;

    public TurneraService(HttpClient http)
    {
        _http = http;
    }

    public async Task<ResetAccesoResponse> ResetearAccesoAsync(string email, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/agent/reset-acceso", new { email }, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ResetAccesoResponse>(ct)
            ?? throw new InvalidOperationException("La turnera no devolvio una respuesta valida.");
    }

    public async Task<PagosResponse> ConsultarPagosAsync(string email, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/agent/pagos?email={Uri.EscapeDataString(email)}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PagosResponse>(ct)
            ?? throw new InvalidOperationException("La turnera no devolvio una respuesta valida.");
    }

    public async Task<TurnosResponse> ConsultarTurnosAsync(string email, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/agent/turnos?email={Uri.EscapeDataString(email)}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TurnosResponse>(ct)
            ?? throw new InvalidOperationException("La turnera no devolvio una respuesta valida.");
    }
}