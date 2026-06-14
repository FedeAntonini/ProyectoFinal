using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using McpServer.MessageQueue;

namespace McpServer.Services;

public class LlmGateway
{
    private readonly ILlmService _llm;

    public LlmGateway(ILlmService llm)
    {
        _llm = llm;
    }

    public async Task<string> CompleteAsync(
        McpClient mcpClient,
        string systemPrompt,
        IEnumerable<ChatMessage> messages,
        int agentRunId,
        string agentType,
        CancellationToken ct = default)
    {
        var inputSnapshot = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Text}"));

        var step = await CreateAgentStepAsync(mcpClient, agentRunId, agentType, inputSnapshot, systemPrompt, ct);

        var response = await _llm.CompleteAsync(systemPrompt, messages, ct);

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
        string prompt,
        CancellationToken ct)
    {
        var result = await mcpClient.CallToolAsync(
            "create_agent_step",
            new Dictionary<string, object?>
            {
                ["agentRunId"] = agentRunId,
                ["agentType"] = agentType,
                ["inputData"] = inputData,
                ["prompt"] = prompt,
            },
            cancellationToken: ct);

        var text = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("create_agent_step returned no text content.");

        return JsonSerializer.Deserialize<AgentStepResult>(text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize AgentStepResult.");
    }

    private record AgentStepResult(int Id, int AgentRunId, string AgentType, string InputData, string OutputData, string Status, DateTime CreatedAt);
}