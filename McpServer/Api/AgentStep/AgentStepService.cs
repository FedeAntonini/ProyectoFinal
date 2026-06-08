using McpServer.Api.AgentStep.Dto;

namespace McpServer.Api.AgentStep;

public class AgentStepService : IAgentStepService
{
    private readonly HttpClient _http;

    public AgentStepService(HttpClient http) => _http = http;

    public async Task<AgentStepResponse> CreateAsync(CreateAgentStepRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/agent-steps", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentStepResponse>(ct);
    }

    public async Task UpdateAsync(int id, string status, string outputData, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/agent-steps/{id}", new { Status = status, OutputData = outputData }, ct);
        response.EnsureSuccessStatusCode();
    }
}