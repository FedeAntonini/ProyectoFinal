using McpServer.MessageQueue;
using McpServer.Services;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.AI;

namespace McpServer.Agentes;

public class AgenteEntrada
{
    private readonly ILogger<AgenteEntrada> _logger;
    private readonly LlmGateway _gateway;
    private readonly IConfiguration _config;

    private static readonly string SystemPrompt = 
    new AgentPromptLoader().Load("entrada.md");

    private readonly OutboundQueueService _outboundQueue;

    public AgenteEntrada(
        ILogger<AgenteEntrada> logger,
        LlmGateway gateway,
        IConfiguration config,
        OutboundQueueService outboundQueue)
    {
        _logger = logger;
        _gateway = gateway;
        _config = config;
        _outboundQueue = outboundQueue;
    }

    public async Task<EntradaResult> ProcessAsync(InboundMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Mensaje recibido — TicketId: {TicketId}, CustomerId: {CustomerId}",
            message.TicketId,
            message.CustomerId);

        await using var mcpClient = await CreateMcpClientAsync(ct);

        var runId = await CreateAgentRunAsync(mcpClient, int.Parse(message.TicketId), ct);

        _logger.LogInformation(
            "AgentRun creado — RunId: {RunId}, TicketId: {TicketId}",
            runId,
            message.TicketId);

        var messages = BuildMessages(message);

        var response = await _gateway.CompleteAsync(
            mcpClient,
            SystemPrompt,
            messages,
            runId,
            nameof(AgenteEntrada),
            ct);

        _logger.LogInformation("Respuesta de LLM recibida: {Response}", response);

        await UpdateAgentRunStatusAsync(mcpClient, runId, "completed", ct);

        return EntradaResult.Accepted(message);
    }

    private static IEnumerable<ChatMessage> BuildMessages(InboundMessage message) =>
    [
        new(ChatRole.User, $"Ticket {message.TicketId}: {message.Payload}")
    ];

    private async Task<McpClient> CreateMcpClientAsync(CancellationToken ct)
    {
        var mcpUrl = _config["McpServer:BaseUrl"] ?? throw new InvalidOperationException("Missing McpServer:BaseUrl");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{mcpUrl}/mcp"),
            Name = "McpServer"
        });

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private async Task<int> CreateAgentRunAsync(McpClient client, int ticketId, CancellationToken ct)
    {
        var result = await client.CallToolAsync(
            "create_agent_run",
            new Dictionary<string, object?> { ["ticketId"] = ticketId },
            cancellationToken: ct);

        var text = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("create_agent_run returned no text content.");

        var run = JsonSerializer.Deserialize<AgentRunResult>(text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize agent run.");

        return run.Id;
    }

    private async Task UpdateAgentRunStatusAsync(McpClient client, int runId, string status, CancellationToken ct)
    {
        await client.CallToolAsync(
            "update_agent_run_status",
            new Dictionary<string, object?> { ["runId"] = runId, ["status"] = status },
            cancellationToken: ct);
    }

    private record AgentRunResult(int Id, int TicketId, string Status, DateTime StartedAt, DateTime? EndedAt);
}