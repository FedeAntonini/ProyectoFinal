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
}