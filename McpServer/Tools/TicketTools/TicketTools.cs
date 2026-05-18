using McpServer.Api.Tickets;
using McpServer.Api.Tickets.Dto;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpServer.Tools;

[McpServerToolType]
public class TicketTools
{
    private readonly ITicketService _ticketService;

    public TicketTools(ITicketService ticketService)
    {
        _ticketService = ticketService;
    }

    [McpServerTool, Description("Fetches a ticket by its ID. Returns the ticket data or null if not found.")]
    public async Task<TicketResponse?> GetTicket(
        [Description("The ticket ID to fetch")]
        int ticketId,
        CancellationToken ct = default)
    {
        return await _ticketService.GetTicketAsync(ticketId, ct);
    }
}