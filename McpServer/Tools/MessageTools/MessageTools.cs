using McpServer.Api.Messages;
using McpServer.Api.Messages.Dto;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpServer.Tools;

[McpServerToolType]
public class MessageTools
{
    private readonly IMessageService _messageService;

    public MessageTools(IMessageService messageService)
    {
        _messageService = messageService;
    }

    [McpServerTool, Description("Fetches all messages for a given conversation ID.")]
    public async Task<IEnumerable<MessageResponse>> GetMessages(
        [Description("The conversation ID to fetch messages for")]
        int conversationId,
        CancellationToken ct = default)
    {
        return await _messageService.GetByConversationIdAsync(conversationId, ct);
    }
}