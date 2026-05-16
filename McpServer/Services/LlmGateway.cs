using System.Text.Json;
using McpServer.Api.AgentStep;
using McpServer.Api.AgentStep.Dto;
using McpServer.MessageQueue;
using Microsoft.Extensions.DependencyInjection;

namespace McpServer.Services;

public class LlmGateway
{
    private readonly ILlmService _llm;
    private readonly IAgentStepService _agentStepService;
    private readonly string _targetAgent;

    public LlmGateway(
        ILlmService llm,
        IAgentStepService agentStepService,
        IConfiguration config)
    {
        _llm = llm;
        _agentStepService = agentStepService;
        _targetAgent = config["Groq:Modelo"]!;
    }

    public async Task<string> CompleteAsync(
        InboundMessage inbound,
        int agentRunId,
        string agentType,
        string prompt,
        CancellationToken ct = default)
    {
        var step = await _agentStepService.CreateAsync(new CreateAgentStepRequest(
            AgentRunId: agentRunId,
            AgentType: agentType,
            InputData: prompt
        ), ct);

        var response = await _llm.CompleteAsync(prompt, ct);

        await _agentStepService.UpdateAsync(step.Id, "completed", response, ct);

        return response;
    }
}