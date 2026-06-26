using McpServer.Api.Tickets.Dto;

namespace McpServer.Api.Tickets;

public class TicketService : ITicketService
{
    private readonly HttpClient _http;

    public TicketService(HttpClient http) 
    {
        _http = http;
    }

    public async Task<TicketResponse?> GetTicketAsync(int ticketId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/tickets/{ticketId}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TicketResponse>(ct);
    }

    public async Task<TicketResponse?> UpdateTicketAsync(int ticketId, UpdateTicketRequest request, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/tickets/{ticketId}", request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        // El endpoint devuelve 204 No Content, no hay body para parsear
        return await GetTicketAsync(ticketId, ct);
    }
    public async Task<TicketResponse> CreateFromAgentAsync(CreateAgentTicketRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/tickets/from-agent", request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TicketResponse>(ct)
            ?? throw new InvalidOperationException("La API no devolvió el ticket creado.");
    }
}