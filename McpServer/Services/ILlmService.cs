namespace McpServer.Services;

public interface ILlmService
{
    Task<string> CompleteAsync(string prompt, CancellationToken ct = default);
}