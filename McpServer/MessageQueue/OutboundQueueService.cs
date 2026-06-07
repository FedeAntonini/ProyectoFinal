using System.Text.Json;

namespace McpServer.MessageQueue;

public class OutboundQueueService
{
    private readonly IMessageQueue _outbound;

    public OutboundQueueService([FromKeyedServices("outbound")] IMessageQueue outbound)
    {
        _outbound = outbound;
    }

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default) =>
        _outbound.SendMessageAsync(JsonSerializer.Serialize(message), ct);
}