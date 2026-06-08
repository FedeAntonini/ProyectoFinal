using System.Text.Json;
using McpServer.Api.Messages.Dto;
using McpServer.MessageQueue;
using McpServer.Services;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpServer.Agentes;

public class AgenteConversacion
{
    private readonly LlmGateway _llmGateway;
    private readonly IConfiguration _config;
    private readonly ILogger<AgenteConversacion> _logger;

    private const string SystemPrompt = """
        Eres un asistente de soporte técnico. Tu trabajo es analizar el ticket, el historial de la
        conversación y el último mensaje del usuario, para luego decidir una de tres acciones:

        - ask_more: Necesitás más información del usuario para poder continuar.
        - escalate: El problema requiere intervención o actividad física (por ejemplo, reemplazo de
          hardware, visitas presenciales, reparaciones físicas). Siempre escalá estos casos al nivel 2.
        - continue: Tenés suficiente información para resolver el problema brindando asesoramiento.

        Respondé ÚNICAMENTE con un objeto JSON en este formato:
        {
            "decision": "ask_more" | "escalate" | "continue",
            "message": "<el mensaje a enviar al usuario>"
        }
        No uses bloques de código ni backticks. Solo el objeto JSON, sin ningún texto adicional.
        """;

    public AgenteConversacion(
        LlmGateway llmGateway,
        IConfiguration config,
        ILogger<AgenteConversacion> logger)
    {
        _llmGateway = llmGateway;
        _config = config;
        _logger = logger;
    }

    public async Task ProcessAsync(InboundMessage inboundMessage, CancellationToken ct = default)
    {
        // 1. Deserialize payload
        var payload = JsonSerializer.Deserialize<IncomingMessagePayload>(inboundMessage.Payload ?? string.Empty)
            ?? throw new InvalidOperationException("Could not deserialize InboundMessage payload.");

        _logger.LogInformation(
            "AgenteConversacion processing message {MessageId} for conversation {ConversationId}",
            payload.MessageId, payload.ConversationId);

        await using var mcpClient = await CreateMcpClientAsync(ct);

        // 2. Create agent run
        var agentRun = await mcpClient.CallToolAsync(
            "create_agent_run",
            new Dictionary<string, object?> { ["ticketId"] = int.Parse(inboundMessage.TicketId) },
            cancellationToken: ct);

        var agentRunResult = JsonSerializer.Deserialize<AgentRunResult>(
            agentRun.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("create_agent_run returned no content."),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize AgentRunResult.");

        _logger.LogInformation("Created agent run {AgentRunId}", agentRunResult.Id);

        try
        {
            // 3. Fetch ticket
            var ticketResult = await mcpClient.CallToolAsync(
                "get_ticket",
                new Dictionary<string, object?> { ["ticketId"] = int.Parse(inboundMessage.TicketId) },
                cancellationToken: ct);

            var ticket = ticketResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("get_ticket returned no content.");

            // 4. Fetch conversation
            var conversationResult = await mcpClient.CallToolAsync(
                "get_conversation",
                new Dictionary<string, object?> { ["conversationId"] = payload.ConversationId },
                cancellationToken: ct);

            var conversation = conversationResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("get_conversation returned no content.");

            // 5. Fetch messages
            var messagesResult = await mcpClient.CallToolAsync(
                "get_messages",
                new Dictionary<string, object?> { ["conversationId"] = payload.ConversationId },
                cancellationToken: ct);

            var messages = messagesResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("get_messages returned no content.");

            // 6. Build chat messages for LLM
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.User, $"""
                    ## Ticket
                    {ticket}

                    ## Conversation
                    {conversation}

                    ## Message History
                    {messages}

                    ## Latest Message
                    {payload.Body}
                    """)
            };

            // 7. Call LLM
            var llmResponse = await _llmGateway.CompleteAsync(
                mcpClient,
                SystemPrompt,
                chatMessages,
                agentRunResult.Id,
                "conversacion",
                ct);

            // 8. Parse decision
            var decision = JsonSerializer.Deserialize<LlmDecision>(llmResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to deserialize LLM decision.");

            _logger.LogInformation(
                "LLM decision for run {AgentRunId}: {Decision}",
                agentRunResult.Id, decision.Decision);

            // 9. Map decision to outbound action
            var outboundAction = decision.Decision switch
            {
                "escalate" => "escalate",
                "ask_more" => "send_message",
                "continue" => "send_message",
                _ => throw new InvalidOperationException($"Unknown LLM decision: {decision.Decision}")
            };

            // 10. Send to outbound queue
            await mcpClient.CallToolAsync(
                "send_outbound_message",
                new Dictionary<string, object?>
                {
                    ["ticketId"] = inboundMessage.TicketId,
                    ["correlationId"] = inboundMessage.CorrelationId,
                    ["customerId"] = inboundMessage.CustomerId,
                    ["targetAgent"] = "conversacion",
                    ["action"] = outboundAction,
                    ["payload"] = JsonSerializer.Serialize(new
                    {
                        payload.ConversationId,
                        Body = decision.Message,
                        MessageType = "text"
                    })
                },
                cancellationToken: ct);

            // 11. Complete agent run
            await mcpClient.CallToolAsync(
                "update_agent_run_status",
                new Dictionary<string, object?>
                {
                    ["runId"] = agentRunResult.Id,
                    ["status"] = "completed"
                },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId} for agent run {AgentRunId}",
                payload.MessageId, agentRunResult.Id);

            await mcpClient.CallToolAsync(
                "update_agent_run_status",
                new Dictionary<string, object?>
                {
                    ["runId"] = agentRunResult.Id,
                    ["status"] = "failed"
                },
                cancellationToken: ct);

            throw;
        }
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

    private record AgentRunResult(int Id, int TicketId, string Status, DateTime CreatedAt);
    private record LlmDecision(string Decision, string Message);
}