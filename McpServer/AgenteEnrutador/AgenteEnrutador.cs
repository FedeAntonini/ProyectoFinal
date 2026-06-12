using System.Text.Json;
using McpServer.Services;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using McpServer.MessageQueue;

namespace McpServer.Agentes;

public class AgenteEnrutador
{
    private readonly LlmGateway _llmGateway;
    private readonly IConfiguration _config;
    private readonly ILogger<AgenteEnrutador> _logger;

    private static readonly string SystemPrompt = 
        new AgentPromptLoader().Load("enrutador.md");

    public AgenteEnrutador(
        LlmGateway llmGateway,
        IConfiguration config,
        ILogger<AgenteEnrutador> logger)
    {
        _llmGateway = llmGateway;
        _config = config;
        _logger = logger;
    }

    public async Task<EnrutadorResult> ProcesarAsync(int ticketId, int agentRunId, CancellationToken ct = default)
    {
        _logger.LogInformation("AgenteEnrutador procesando ticket {TicketId}", ticketId);

        await using var mcpClient = await CreateMcpClientAsync(ct);

        // Obtener el ticket via tool
        var ticketResult = await mcpClient.CallToolAsync(
            "get_ticket",
            new Dictionary<string, object?> { ["ticketId"] = ticketId },
            cancellationToken: ct);

        var ticket = ticketResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("get_ticket returned no content.");

        _logger.LogInformation("Ticket obtenido: {Ticket}", ticket);

        // Llamar al LLM para que razone y decida
        var llmResponse = await _llmGateway.CompleteAsync(
            mcpClient,
            SystemPrompt,
            [new(ChatRole.User, $"Datos del ticket:\n{ticket}")],
            agentRunId,
            "enrutador",
            ct);

        _logger.LogInformation("LLM response: {Response}", llmResponse);

        // Parsear la decisión
        var decision = JsonSerializer.Deserialize<EnrutadorDecision>(llmResponse,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize EnrutadorDecision.");

        _logger.LogInformation("Enrutador decision for ticket {TicketId}: {Agente}", ticketId, decision.Agente);

        return new EnrutadorResult(ticketId, decision.Agente, decision.Motivo);
    }

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

    public async Task ProcessAsync(InboundMessage message, CancellationToken ct = default)
{
    if (!int.TryParse(message.TicketId, out var ticketId))
    {
        _logger.LogWarning("TicketId inválido: {TicketId}", message.TicketId);
        return;
    }

    // Usar el agentRunId del mensaje o crear uno nuevo
    var agentRunId = 1; // TODO: extraer del mensaje cuando esté disponible
    await ProcesarAsync(ticketId, agentRunId, ct);
}

    private record EnrutadorDecision(string Agente, string Motivo);
}

public record EnrutadorResult(int TicketId, string Agente, string Motivo);