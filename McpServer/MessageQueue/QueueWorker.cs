using System.Text.Json;
using McpServer.Agentes;
namespace McpServer.MessageQueue;
public class QueueWorker : BackgroundService
{
    private readonly IMessageQueue _inbound;
    private readonly IMessageQueue _outbound;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueueWorker> _logger;

    public QueueWorker(
        [FromKeyedServices("inbound")] IMessageQueue inbound,
        [FromKeyedServices("outbound")] IMessageQueue outbound,
        IServiceScopeFactory scopeFactory,
        ILogger<QueueWorker> logger)
    {
        _inbound = inbound;
        _outbound = outbound;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _inbound.ReceiveMessagesAsync(10, stoppingToken);

                foreach (var message in messages)
                    await ProcessMessageAsync(message, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error polling queue");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Procesando mensaje {MessageId}", message.MessageId);

        try
        {
            var inbound = JsonSerializer.Deserialize<InboundMessage>(message.Body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("No se pudo deserializar el mensaje");

            using var scope = _scopeFactory.CreateScope();
            var router = scope.ServiceProvider.GetRequiredService<AgenteEntrada>();

            var result = await router.ProcessAsync(inbound);

            await _inbound.DeleteMessageAsync(message.ReceiptHandle, stoppingToken);

            _logger.LogInformation("Mensaje {MessageId} procesado exitosamente", message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al procesar el mensaje {MessageId}", message.MessageId);
        }
    }
}