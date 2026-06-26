namespace McpServer.Api.Tickets.Dto;

public record UpdateTicketRequest(
    string? Title = null,
    string? Description = null,
    int? State = null,
    string? StateLabel = null,
    int? Priority = null,
    string? PriorityLabel = null,
    string? AssignedTo = null,
    string? AssignmentGroup = null,
    string? AffectedSystem = null,
    DateTime? ResolvedAt = null,
    string? CreatedByEmail = null,
    string? CreatedByName = null
);