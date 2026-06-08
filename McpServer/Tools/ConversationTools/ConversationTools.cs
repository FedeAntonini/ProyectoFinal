using McpServer.Api.Conversations;
using McpServer.Api.Conversations.Dto;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpServer.Tools;

[McpServerToolType]
public class ConversationTools
{
    private readonly IConversationService _conversationService;

    public ConversationTools(IConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    [McpServerTool, Description("Fetches a conversation by its ID. Returns the conversation data or null if not found.")]
    public async Task<ConversationResponse?> GetConversation(
        [Description("The conversation ID to fetch")]
        int conversationId,
        CancellationToken ct = default)
    {
        return await _conversationService.GetConversationAsync(conversationId, ct);
    }
}