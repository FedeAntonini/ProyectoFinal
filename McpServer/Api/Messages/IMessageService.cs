using McpServer.Api.Messages.Dto;

namespace McpServer.Api.Messages;

public interface IMessageService
{
    Task<IEnumerable<MessageResponse>> GetByConversationIdAsync(int conversationId, CancellationToken ct = default);
}
