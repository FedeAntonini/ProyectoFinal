using McpServer.Api.Messages.Dto;

namespace McpServer.Api.Messages;

public class MessageService : IMessageService
{
    private readonly HttpClient _http;

    public MessageService(HttpClient http)
    {
        _http = http;
    }

    public async Task<IEnumerable<MessageResponse>> GetByConversationIdAsync(int conversationId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/messages/conversation/{conversationId}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IEnumerable<MessageResponse>>(ct) ?? [];
    }
}