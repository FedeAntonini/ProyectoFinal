using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace McpServer.Services;

public class GroqLlmService : ILlmService
{
    private readonly IChatClient _client;
    private readonly string _systemPrompt;

    public GroqLlmService(IChatClient client, string systemPrompt)
    {
        _client = client;
        _systemPrompt = systemPrompt;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _systemPrompt),
            new(ChatRole.User, prompt)
        };

        var response = await _client.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text;
    }

    public static GroqLlmService Create(string apiKey, string model, string systemPrompt)
    {
        var client = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1/") })
            .GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        return new GroqLlmService(client, systemPrompt);
    }
}