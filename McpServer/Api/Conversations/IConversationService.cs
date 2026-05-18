using McpServer.Api.Conversations.Dto;

namespace McpServer.Api.Conversations;

public interface IConversationService
{
    Task<ConversationResponse?> GetConversationAsync(int conversationId, CancellationToken ct = default);
}
