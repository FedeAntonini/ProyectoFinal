using McpServer.Api.AgentStep;
using McpServer.Api.AgentStep.Dto;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpServer.Tools;

[McpServerToolType]
public class AgentStepTools
{
    private readonly IAgentStepService _agentStepService;

    public AgentStepTools(IAgentStepService agentStepService)
    {
        _agentStepService = agentStepService;
    }

    [McpServerTool, Description("Creates a new agent step for a given agent run.")]
    public async Task<AgentStepResponse> CreateAgentStep(
        [Description("The agent run ID this step belongs to")]
        int agentRunId,
        [Description("The type/name of the agent executing this step")]
        string agentType,
        [Description("The input data for this step, typically the prompt or message being processed")]
        string inputData,
        CancellationToken ct = default)
    {
        return await _agentStepService.CreateAsync(
            new CreateAgentStepRequest(agentRunId, agentType, inputData), ct);
    }

    [McpServerTool, Description("Updates an existing agent step with its result status and output data.")]
    public async Task UpdateAgentStep(
        [Description("The agent step ID to update")]
        int stepId,
        [Description("The result status: completed, failed")]
        string status,
        [Description("The output data or result produced by this step")]
        string outputData,
        CancellationToken ct = default)
    {
        await _agentStepService.UpdateAsync(stepId, status, outputData, ct);
    }
}