using McpServer.Api.AgentRun.Dto;

namespace McpServer.Api.AgentRun;
public class AgentRunService : IAgentRunService
{
    private readonly HttpClient _http;

    public AgentRunService(HttpClient http) => _http = http;

    public async Task<AgentRunResponse> CreateAsync(CreateAgentRunRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/agent-runs", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentRunResponse>(ct);
    }
    public async Task UpdateStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/agent-runs/{id}/status", new { Status = status }, ct);
        response.EnsureSuccessStatusCode();
    }
}