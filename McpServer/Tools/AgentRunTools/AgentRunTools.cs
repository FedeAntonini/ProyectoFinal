using McpServer.Api.AgentRun;
using McpServer.Api.AgentRun.Dto;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpServer.Tools;

[McpServerToolType]
public class AgentRunTools
{
    private readonly IAgentRunService _agentRunService;

    public AgentRunTools(IAgentRunService agentRunService)
    {
        _agentRunService = agentRunService;
    }

    [McpServerTool, Description("Creates a new agent run for a given ticket. Returns the run ID and metadata.")]
    public async Task<AgentRunResponse> CreateAgentRun(
        [Description("The ticket ID to create the run for")]
        int ticketId,
        CancellationToken ct = default)
    {
        return await _agentRunService.CreateAsync(new CreateAgentRunRequest(ticketId), ct);
    }

    [McpServerTool, Description("Updates the status of an existing agent run. Valid statuses: completed, failed, running.")]
    public async Task UpdateAgentRunStatus(
        [Description("The agent run ID to update")]
        int runId,
        [Description("The new status: completed, failed, running")]
        string status,
        CancellationToken ct = default)
    {
        await _agentRunService.UpdateStatusAsync(runId, status, ct);
    }
}