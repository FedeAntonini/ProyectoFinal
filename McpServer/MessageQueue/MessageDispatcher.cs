using McpServer.Agentes;
using McpServer.MessageQueue;

namespace McpServer.MessageQueue;

public class MessageDispatcher
{
    private readonly AgenteEntrada _agenteEntrada;
    private readonly AgenteConversacion _agenteConversacion;
    private readonly AgenteEnrutador _agenteEnrutador;
    private readonly AgenteAccion _agenteAccion;
    private readonly ILogger<MessageDispatcher> _logger;

    public MessageDispatcher(
        AgenteEntrada agenteEntrada,
        AgenteConversacion agenteConversacion,
        AgenteEnrutador agenteEnrutador,
        AgenteAccion agenteAccion,
        ILogger<MessageDispatcher> logger)
    {
        _agenteEntrada = agenteEntrada;
        _agenteConversacion = agenteConversacion;
        _agenteEnrutador = agenteEnrutador;
        _agenteAccion = agenteAccion;
        _logger = logger;
    }

    public Task DispatchAsync(InboundMessage message, CancellationToken ct)
    {
        switch (message.Action)
        {
            case InboundAction.UserMessage:
                return _agenteConversacion.ProcessAsync(message, ct);
            case InboundAction.NewTicket:
                return _agenteEntrada.ProcessAsync(message, ct);
            case InboundAction.TicketParaEnrutar:
                return _agenteEnrutador.ProcessAsync(message, ct);
            case InboundAction.TicketParaEjecutar:
                return _agenteAccion.ProcessAsync(message, ct);
            default:
                _logger.LogWarning("Unexpected action type: {Action}", message.Action);
                return Task.CompletedTask;
        }
    }
}