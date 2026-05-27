using System.Text.Json;
using System.Text.RegularExpressions;
using McpServer.Api.Messages.Dto;
using McpServer.MessageQueue;
using McpServer.Services;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpServer.Agentes;

public class AgenteConversacion
{
    private readonly AgenteEntrada _agenteEntrada;
    private readonly LlmGateway _llmGateway;
    private readonly IConfiguration _config;
    private readonly ILogger<AgenteConversacion> _logger;

    // Detecta "INC0001", "INC0042", etc. (con o sin espacios alrededor)
    private static readonly Regex TicketIdRegex =
        new(@"\bINC\d{4}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string SystemPrompt = """
        Sos un asistente de soporte de la turnera de pilates. Tu trabajo es analizar el ticket,
        el historial de la conversación y el último mensaje del usuario, para luego decidir una
        de tres acciones:

        - ask_more: Necesitás más información del usuario para poder continuar.
        - escalate:  El problema requiere intervención manual o no puede resolverse automáticamente.
        - continue:  Tenés suficiente información para asesorar al usuario.

        Respondé ÚNICAMENTE con un objeto JSON en este formato (sin bloques de código ni backticks):
        {
            "decision": "ask_more" | "escalate" | "continue",
            "message": "<mensaje a enviar al usuario>"
        }
        """;

    public AgenteConversacion(
        AgenteEntrada agenteEntrada,
        LlmGateway llmGateway,
        IConfiguration config,
        ILogger<AgenteConversacion> logger)
    {
        _agenteEntrada = agenteEntrada;
        _llmGateway    = llmGateway;
        _config        = config;
        _logger        = logger;
    }

    public async Task ProcessAsync(InboundMessage inboundMessage, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<IncomingMessagePayload>(
            inboundMessage.Payload ?? string.Empty)
            ?? throw new InvalidOperationException("No se pudo deserializar el payload del mensaje.");

        _logger.LogInformation(
            "AgenteConversacion — mensaje {MessageId}, conversación {ConversationId}",
            payload.MessageId, payload.ConversationId);

        var ticketMatch = TicketIdRegex.Match(payload.Body.Trim());

        if (ticketMatch.Success)
            await ProcesarTicketAsync(inboundMessage, ticketMatch.Value.ToUpper(), payload, ct);
        else
            await ProcesarConversacionAsync(inboundMessage, payload, ct);
    }

    // ── Flujo 1: el usuario envió un ID de ticket → resolver ─────────────────

    private async Task ProcesarTicketAsync(
        InboundMessage inboundMessage,
        string ticketId,
        IncomingMessagePayload payload,
        CancellationToken ct)
    {
        _logger.LogInformation("Ticket ID detectado en Telegram: {TicketId}", ticketId);

        await using var mcpClient = await CreateMcpClientAsync(ct);

        // 1. Notificar al usuario que arrancamos
        await NotificarAsync(mcpClient, inboundMessage,
            $"⏳ Recibí tu ticket *{ticketId}*. Estoy analizando y procesando tu caso...", ct);

        try
        {
            // 2. Construir el mensaje de entrada para AgenteEntrada
            var mensajeTicket = new InboundMessage(
                TicketId:      ticketId,
                CorrelationId: inboundMessage.CorrelationId,
                CustomerId:    inboundMessage.CustomerId,
                Action:        InboundAction.NewTicket,
                Payload:       null
            );

            // 3. Delegar al AgenteEntrada (orquestador del pipeline)
            //    → AgenteEntrada llama a AgenteEnrutador (KB) → AgenteAccion (subagente específico)
            await _agenteEntrada.ProcessAsync(mensajeTicket, ct);

            // 4. Notificar resolución exitosa
            await NotificarAsync(mcpClient, inboundMessage,
                $"✅ Tu ticket *{ticketId}* fue resuelto correctamente. " +
                $"Vas a recibir los detalles en tu email registrado.", ct);

            _logger.LogInformation("Ticket {TicketId} resuelto y notificado al usuario.", ticketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar ticket {TicketId} desde Telegram", ticketId);

            await NotificarAsync(mcpClient, inboundMessage,
                $"❌ El ticket *{ticketId}* no pudo resolverse automáticamente. " +
                $"Fue escalado a soporte nivel 2, quien se va a contactar pronto.", ct);
        }
    }

    // ── Flujo 2: mensaje de conversación normal ───────────────────────────────

    private async Task ProcesarConversacionAsync(
        InboundMessage inboundMessage,
        IncomingMessagePayload payload,
        CancellationToken ct)
    {
        await using var mcpClient = await CreateMcpClientAsync(ct);

        // Crear agent run
        var agentRunRaw = await mcpClient.CallToolAsync(
            "create_agent_run",
            new Dictionary<string, object?> { ["ticketId"] = int.Parse(inboundMessage.TicketId) },
            cancellationToken: ct);

        var agentRun = JsonSerializer.Deserialize<AgentRunResult>(
            agentRunRaw.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("create_agent_run no retornó contenido."),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("No se pudo deserializar AgentRunResult.");

        _logger.LogInformation("Agent run creado: {AgentRunId}", agentRun.Id);

        try
        {
            // Obtener datos del ticket
            var ticketRaw = await mcpClient.CallToolAsync(
                "get_ticket",
                new Dictionary<string, object?> { ["ticketId"] = int.Parse(inboundMessage.TicketId) },
                cancellationToken: ct);
            var ticket = ticketRaw.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

            // Obtener conversación
            var convRaw = await mcpClient.CallToolAsync(
                "get_conversation",
                new Dictionary<string, object?> { ["conversationId"] = payload.ConversationId },
                cancellationToken: ct);
            var conversation = convRaw.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

            // Obtener mensajes previos
            var msgsRaw = await mcpClient.CallToolAsync(
                "get_messages",
                new Dictionary<string, object?> { ["conversationId"] = payload.ConversationId },
                cancellationToken: ct);
            var messages = msgsRaw.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

            // Llamar al LLM con el contexto completo
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.User, $"""
                    ## Ticket
                    {ticket}

                    ## Conversación
                    {conversation}

                    ## Historial de mensajes
                    {messages}

                    ## Último mensaje del usuario
                    {payload.Body}
                    """)
            };

            var llmResponse = await _llmGateway.CompleteAsync(
                mcpClient, SystemPrompt, chatMessages, agentRun.Id, "conversacion", ct);

            var decision = JsonSerializer.Deserialize<LlmDecision>(llmResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("No se pudo deserializar la decisión del LLM.");

            _logger.LogInformation("Decisión del LLM para run {AgentRunId}: {Decision}",
                agentRun.Id, decision.Decision);

            var accionSalida = decision.Decision switch
            {
                "escalate" => "escalate",
                "ask_more" => "send_message",
                "continue" => "send_message",
                _ => throw new InvalidOperationException($"Decisión desconocida: {decision.Decision}")
            };

            await NotificarAsync(mcpClient, inboundMessage, decision.Message, ct, accionSalida);

            await mcpClient.CallToolAsync(
                "update_agent_run_status",
                new Dictionary<string, object?> { ["runId"] = agentRun.Id, ["status"] = "completed" },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando mensaje {MessageId} para agent run {AgentRunId}",
                payload.MessageId, agentRun.Id);

            await mcpClient.CallToolAsync(
                "update_agent_run_status",
                new Dictionary<string, object?> { ["runId"] = agentRun.Id, ["status"] = "failed" },
                cancellationToken: ct);

            throw;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task NotificarAsync(
        McpClient mcpClient,
        InboundMessage msg,
        string texto,
        CancellationToken ct,
        string action = "send_message")
    {
        var payloadJson = JsonSerializer.Serialize(new { Body = texto, MessageType = "text" });

        await mcpClient.CallToolAsync("send_outbound_message", new Dictionary<string, object?>
        {
            ["ticketId"]      = msg.TicketId,
            ["correlationId"] = msg.CorrelationId,
            ["customerId"]    = msg.CustomerId,
            ["action"]        = action,
            ["payload"]       = payloadJson,
            ["targetAgent"]   = "conversacion"
        }, cancellationToken: ct);
    }

    private async Task<McpClient> CreateMcpClientAsync(CancellationToken ct)
    {
        var mcpUrl = _config["McpServer:BaseUrl"]
            ?? throw new InvalidOperationException("Missing McpServer:BaseUrl");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{mcpUrl}/mcp"),
            Name     = "McpServer"
        });

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private record AgentRunResult(int Id, int TicketId, string Status, DateTime CreatedAt);
    private record LlmDecision(string Decision, string Message);
}
