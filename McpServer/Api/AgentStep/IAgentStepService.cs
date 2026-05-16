using McpServer.Api.AgentStep.Dto;

namespace McpServer.Api.AgentStep;
public interface IAgentStepService
{
    Task<AgentStepResponse> CreateAsync(CreateAgentStepRequest request, CancellationToken ct = default);
    Task UpdateAsync(int id, string status, string outputData, CancellationToken ct = default);
}