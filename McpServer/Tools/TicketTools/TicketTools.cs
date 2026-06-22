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

    [McpServerTool, Description("Actualiza el sistema afectado de un ticket según el diagnóstico del enrutador.")]
    public async Task<string> ActualizarSistemaAfectado(
        [Description("ID numérico del ticket")]
        int ticketId,
        [Description("Sistema afectado: acceso, reserva, pago, notificacion, escalacion")]
        string sistemaAfectado,
        CancellationToken ct = default)
    {
        var request = new UpdateTicketRequest(
            Title: null,
            Description: null,
            State: null,
            StateLabel: null,
            Priority: null,
            PriorityLabel: null,
            AssignedTo: null,
            AssignmentGroup: null,
            AffectedSystem: sistemaAfectado.ToLower(),
            ResolvedAt: null);

        var ticket = await _ticketService.UpdateTicketAsync(ticketId, request, ct);

        return ticket is null
            ? $"Ticket {ticketId} no encontrado."
            : $"AffectedSystem del ticket {ticketId} actualizado a '{sistemaAfectado}'.";
    }
}