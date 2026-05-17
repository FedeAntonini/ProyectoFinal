using McpServer.MessageQueue;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace McpServer.Services;

public class LlmGateway
{
    private readonly ILlmService _llm;
    private readonly string _modelo;

    public LlmGateway(ILlmService llm, IConfiguration config)
    {
        _llm = llm;
        _modelo = config["Groq:Modelo"]!;
    }

    public async Task<string> CompleteAsync(
        McpClient mcpClient,
        InboundMessage inbound,
        int agentRunId,
        string agentType,
        string prompt,
        CancellationToken ct = default)
    {
        var step = await CreateAgentStepAsync(mcpClient, agentRunId, agentType, prompt, ct);

        var response = await _llm.CompleteAsync(prompt, ct);

        await mcpClient.CallToolAsync(
            "update_agent_step",
            new Dictionary<string, object?>
            {
                ["stepId"] = step.Id,
                ["status"] = "completed",
                ["outputData"] = response
            },
            cancellationToken: ct);

        return response;
    }

    private async Task<AgentStepResult> CreateAgentStepAsync(
        McpClient mcpClient,
        int agentRunId,
        string agentType,
        string inputData,
        CancellationToken ct)
    {
        var result = await mcpClient.CallToolAsync(
            "create_agent_step",
            new Dictionary<string, object?>
            {
                ["agentRunId"] = agentRunId,
                ["agentType"] = agentType,
                ["inputData"] = inputData
            },
            cancellationToken: ct);

        var text = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("create_agent_step returned no text content.");

        return JsonSerializer.Deserialize<AgentStepResult>(text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize agent step.");
    }

    private record AgentStepResult(int Id, int AgentRunId, string AgentType, string InputData, string OutputData, string Status, DateTime CreatedAt);
}