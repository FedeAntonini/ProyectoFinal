using System.Text.Json;
using McpServer.Services;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpServer.Agentes;

public class AgenteEnrutador
{
    private readonly LlmGateway _llmGateway;
    private readonly IConfiguration _config;
    private readonly ILogger<AgenteEnrutador> _logger;

    private const string SystemPrompt = """
        Sos el Agente Enrutador de un sistema de soporte nivel 1 para un estudio de pilates.
        
        Cuando recibas los datos de un ticket:
        1. Analizá el problema y el sistema afectado
        2. Decidí cuál de estos agentes es el más adecuado para resolverlo:
           - AgenteAccionReserva: problemas con reservas de turnos (no puede reservar, turno no aparece, quiere cancelar o cambiar horario)
           - AgenteAccionAcceso: problemas de login o acceso a la plataforma
           - AgenteAccionPago: problemas con cobros, pagos o facturación de clases
           - AgenteAccionNotificacion: no recibió confirmación o notificación de un turno
           - Escalacion: si el problema no encaja en ninguno de los anteriores
        3. Respondé ÚNICAMENTE con un JSON en este formato:
           {"agente": "NombreDelAgente", "motivo": "explicación breve de por qué elegiste ese agente"}
        
        No uses bloques de código ni backticks. Solo el JSON, sin texto adicional.
        """;

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

    private record EnrutadorDecision(string Agente, string Motivo);
}

public record EnrutadorResult(int TicketId, string Agente, string Motivo);