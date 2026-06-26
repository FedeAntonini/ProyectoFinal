namespace McpServer.Api.Tickets.Dto;

public record CreateAgentTicketRequest(
    string Description,
    string System,
    string ErrorType,
    string UserEmail);