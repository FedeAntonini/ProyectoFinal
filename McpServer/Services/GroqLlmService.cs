using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace McpServer.Services;

public class GroqLlmService : ILlmService
{
    private readonly IChatClient _client;

    public GroqLlmService(IChatClient client)
    {
        _client = client;
    }

    public async Task<string> CompleteAsync(string systemPrompt, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var fullMessages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt)
        };
        fullMessages.AddRange(messages);

        var response = await _client.GetResponseAsync(fullMessages, cancellationToken: ct);
        return response.Text;
    }

    public static GroqLlmService Create(string apiKey, string model)
    {
        var client = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1/") })
            .GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        return new GroqLlmService(client);
    }
}