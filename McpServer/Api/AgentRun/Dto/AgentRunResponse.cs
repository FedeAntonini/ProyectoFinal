namespace McpServer.Api.AgentRun.Dto;
public record AgentRunResponse(
    int Id,
    int TicketId,
    string Status,
    DateTime StartedAt,
    DateTime? EndedAt
);