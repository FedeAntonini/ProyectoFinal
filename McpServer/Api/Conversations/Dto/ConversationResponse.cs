namespace McpServer.Api.Conversations.Dto;

public record ConversationResponse(
    int Id,
    string SysId,
    int TicketId,
    string Channel,
    string Status,
    DateTime StartedAt,
    DateTime? EndedAt,
    DateTime LastSyncedAt
);
