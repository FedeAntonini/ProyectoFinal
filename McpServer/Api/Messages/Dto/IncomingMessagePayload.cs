namespace McpServer.Api.Messages.Dto;
public record IncomingMessagePayload(
    int MessageId,
    int ConversationId,
    string ConversationSysId,
    string SysId,
    string SenderType,
    string SenderName,
    string Body,
    string MessageType,
    DateTime SentAt
);