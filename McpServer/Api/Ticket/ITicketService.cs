using McpServer.Api.Tickets.Dto;

namespace McpServer.Api.Tickets;

public interface ITicketService
{
    Task<TicketResponse?> GetTicketAsync(int ticketId, CancellationToken ct = default);
    Task<TicketResponse?> UpdateTicketAsync(int ticketId, UpdateTicketRequest request, CancellationToken ct = default);
}