namespace McpServer.MessageQueue;

/// <summary>
/// Cola vacía para modo Development. Permite que OutboundQueueService
/// se registre en DI sin necesitar SQS real.
/// </summary>
public class NoOpMessageQueue : IMessageQueue
{
    public Task<IReadOnlyList<QueueMessage>> ReceiveMessagesAsync(int maxMessages, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<QueueMessage>>(Array.Empty<QueueMessage>());

    public Task SendMessageAsync(string body, CancellationToken ct)
        => Task.CompletedTask;

    public Task DeleteMessageAsync(string receiptHandle, CancellationToken ct)
        => Task.CompletedTask;
}
