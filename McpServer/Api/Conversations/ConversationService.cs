using McpServer.Api.Conversations.Dto;

namespace McpServer.Api.Conversations;

public class ConversationService : IConversationService
{
    private readonly HttpClient _http;

    public ConversationService(HttpClient http)
    {
        _http = http;
    }

    public async Task<ConversationResponse?> GetConversationAsync(int conversationId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/conversations/{conversationId}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConversationResponse>(ct);
    }
}