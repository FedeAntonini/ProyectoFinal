using McpServer.Api.AgentRun;
using McpServer.MessageQueue;
using McpServer.Services;
using McpServer.Api.AgentRun.Dto;

namespace McpServer.Agentes;

public class AgenteEntrada
{
    private readonly ILogger<AgenteEntrada> _logger;
    private readonly IAgentRunService _agentRunService;
    private readonly LlmGateway _gateway;

    public AgenteEntrada(
        ILogger<AgenteEntrada> logger,
        IAgentRunService agentRunService,
        LlmGateway gateway)
    {
        _logger = logger;
        _agentRunService = agentRunService;
        _gateway = gateway;
    }

    public async Task<EntradaResult> ProcessAsync(InboundMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Mensaje recibido — TicketId: {TicketId}, CustomerId: {CustomerId}",
            message.TicketId,
            message.CustomerId);

        var run = await _agentRunService.CreateAsync(
            new CreateAgentRunRequest(TicketId: int.Parse(message.TicketId)), ct);

        _logger.LogInformation(
            "AgentRun creado — RunId: {RunId}, TicketId: {TicketId}",
            run.Id,
            message.TicketId);

        try
        {
            // TODO: llamada a Groq via LlmGateway
             var prompt = BuildPrompt(message);
            var response = await _gateway.CompleteAsync(message, run.Id, nameof(AgenteEntrada), prompt, ct);

            await _agentRunService.UpdateStatusAsync(run.Id, "completed", ct);
            return EntradaResult.Accepted(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando TicketId: {TicketId}", message.TicketId);
            await _agentRunService.UpdateStatusAsync(run.Id, "failed", ct);
            throw;
        }
    }
    private string BuildPrompt(InboundMessage message) =>
    $"Ticket {message.TicketId}: {message.Metadata}";
}