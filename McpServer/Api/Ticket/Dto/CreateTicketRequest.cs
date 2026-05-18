namespace McpServer.Api.Tickets.Dto;
public record CreateTicketRequest(
    string SysId,
    string Number,
    string Title,
    string Description,
    int State,
    string StateLabel,
    int Priority,
    string PriorityLabel,
    DateTime OpenedAt
);