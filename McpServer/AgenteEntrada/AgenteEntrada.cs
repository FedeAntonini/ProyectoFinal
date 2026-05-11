using McpServer.MessageQueue;

namespace McpServer.Agentes;
public class AgenteEntrada
{
    private readonly ILogger<AgenteEntrada> _logger;

    public AgenteEntrada(ILogger<AgenteEntrada> logger)
    {
        _logger = logger;
    }

    public Task<EntradaResult> ProcessAsync(InboundMessage message)
    {
        _logger.LogInformation(
            "Mensaje recibido — TicketId: {TicketId}, CustomerId: {CustomerId}",
            message.TicketId,
            message.CustomerId);

        return Task.FromResult(EntradaResult.Accepted(message));
    }
}
