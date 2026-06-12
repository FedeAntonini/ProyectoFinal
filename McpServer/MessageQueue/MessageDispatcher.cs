using McpServer.Agentes;
using McpServer.MessageQueue;

namespace McpServer.MessageQueue;

public class MessageDispatcher
{
    private readonly AgenteEntrada _agenteEntrada;
    private readonly AgenteConversacion _agenteConversacion;
    private readonly AgenteEnrutador _agenteEnrutador;
    private readonly ILogger<MessageDispatcher> _logger;

    public MessageDispatcher(
        AgenteEntrada agenteEntrada,
        AgenteConversacion agenteConversacion,
        AgenteEnrutador agenteEnrutador,
        ILogger<MessageDispatcher> logger)
    {
        _agenteEntrada = agenteEntrada;
        _agenteConversacion = agenteConversacion;
        _agenteEnrutador = agenteEnrutador;
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
                _logger.LogWarning("AgenteAccion no implementado aún para ticket {TicketId}", message.TicketId);
                return Task.CompletedTask;
            case InboundAction.NotificarResolucion:
                _logger.LogWarning("NotificarResolucion no implementado aún para ticket {TicketId}", message.TicketId);
                return Task.CompletedTask;
            default:
                _logger.LogWarning("Unexpected action type: {Action}", message.Action);
                return Task.CompletedTask;
        }
    }
}