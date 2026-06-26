namespace McpServer.Api.Tickets.Dto;

public record TicketResponse(
    int Id,
    string SysId,
    string Number,
    string Title,
    string Description,
    int State,
    string StateLabel,
    int Priority,
    string PriorityLabel,
    string CreatedByName,
    string CreatedByEmail,
    DateTime OpenedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    DateTime LastSyncedAt
);