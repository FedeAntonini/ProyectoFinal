namespace McpServer.Api.Messages.Dto;

public record MessageResponse(
    int Id,
    string SysId,
    int ConversationId,
    string SenderType,
    string SenderName,
    string Body,
    string MessageType,
    DateTime SentAt,
    DateTime LastSyncedAt
);
