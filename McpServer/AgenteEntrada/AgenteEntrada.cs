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

    private const string SystemPrompt = """
    Sos el Agente Enrutador de soporte de e-commerce nivel 1.

    Cuando recibas un ID de ticket:
    1. Llamá a la tool obtener_ticket para obtener los datos
    2. Si el ticket no existe, respondé: TICKET_NO_ENCONTRADO: No se encontró un ticket con ese ID.
    3. Si el ticket existe, analizá el problema y el sistema afectado y decidí cuál de estos agentes es el más adecuado para resolverlo:
       - AgenteAccionAcceso: problemas de login, autenticación o acceso de usuarios
       - AgenteAccionPedido: problemas con el estado o seguimiento de pedidos
       - AgenteAccionPago: problemas con pagos, cobros o transacciones
       - AgenteAccionPrecio: problemas con precios incorrectos en el catálogo
       - AgenteAccionStock: problemas con inventario o sincronización de stock
       - Escalacion: si el problema no encaja en ninguno de los anteriores
    4. Respondé ÚNICAMENTE con: DELEGAR_A: [nombre del agente elegido]

    No incluyas tags XML ni HTML. No agregues texto adicional.
    """;

    public AgenteEntrada(
        ILogger<AgenteEntrada> logger,
        LlmGateway gateway,
        IConfiguration config)
    {
        _logger = logger;
        _gateway = gateway;
        _config = config;
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