using Microsoft.Extensions.AI;

public interface ILlmService
{
    Task<string> CompleteAsync(string systemPrompt, IEnumerable<ChatMessage> messages, CancellationToken ct = default);
}