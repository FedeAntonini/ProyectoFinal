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

    public async Task<EnrutadorResult> ProcesarAsync(int ticketId, int agentRunId, string correlationId, string customerId, int conversationId, CancellationToken ct = default)
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

    // Setear el AffectedSystem en el ticket según la decisión
    var sistemaAfectado = MapearAgenteASistema(decision.Agente);
    await mcpClient.CallToolAsync(
        "actualizar_sistema_afectado",
        new Dictionary<string, object?>
        {
            ["ticketId"] = ticketId,
            ["sistemaAfectado"] = sistemaAfectado
        },
        cancellationToken: ct);

    _logger.LogInformation("AffectedSystem '{Sistema}' seteado para ticket {TicketId}", sistemaAfectado, ticketId);

    var resultado = new EnrutadorResult(ticketId, decision.Agente, decision.Motivo);

    // Encolar TicketParaEjecutar para que AgenteAccion tome la decision
    await mcpClient.CallToolAsync(
        "send_outbound_message",
        new Dictionary<string, object?>
        {
            ["ticketId"] = ticketId.ToString(),
            ["correlationId"] = correlationId,
            ["customerId"] = customerId,
            ["targetAgent"] = "enrutador",
            ["action"] = "agente_accion",
            ["payload"] = JsonSerializer.Serialize(new
                {
                    resultado.TicketId,
                    resultado.Agente,
                    resultado.Motivo,
                    ConversationId = conversationId
                })
        },
        cancellationToken: ct);

    _logger.LogInformation("agente_accion encolado para ticket {TicketId}", ticketId);

    return resultado;
    }

    private static string MapearAgenteASistema(string agente) => agente switch
    {
        "AgenteAccionAcceso" => "acceso",
        "AgenteAccionPago" => "pago",
        "AgenteAccionTurnos" => "turnos",
        "AgenteAccionDisponibilidad" => "disponibilidad",
        _ => "escalacion"
    };

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

        // Extraer ConversationId del payload
        int conversationId = 0;
        if (!string.IsNullOrWhiteSpace(message.Payload))
        {
            try
            {
                var payloadDoc = JsonSerializer.Deserialize<JsonElement>(message.Payload);
                if (payloadDoc.TryGetProperty("conversationId", out var convIdEl))
                    conversationId = convIdEl.GetInt32();
            }
            catch { /* si no viene, continuamos con 0 */ }
        }

        await using var mcpClient = await CreateMcpClientAsync(ct);
        var agentRunId = await CreateAgentRunAsync(mcpClient, ticketId, ct);

        await ProcesarAsync(ticketId, agentRunId, message.CorrelationId, message.CustomerId, conversationId, ct);
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

    private record AgentRunResult(int Id, int TicketId, string Status, DateTime StartedAt, DateTime? EndedAt);

        private record EnrutadorDecision(string Agente, string Motivo);
    }

    public record EnrutadorResult(int TicketId, string Agente, string Motivo);