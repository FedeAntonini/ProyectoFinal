using McpServer.Api.AgentRun.Dto;

namespace McpServer.Api.AgentRun;

public interface IAgentRunService
{
    Task<AgentRunResponse> CreateAsync(CreateAgentRunRequest request, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, string status, CancellationToken ct = default);
}