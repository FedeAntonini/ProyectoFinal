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

    [McpServerTool, Description("Escalates a ticket by updating its assignment group to level two.")]
    public async Task<string> EscalarTicket(
    [Description("ID numérico del ticket")]
    int ticketId,
    CancellationToken ct = default)
    {
        var request = new UpdateTicketRequest(AssignmentGroup: "Soporte Nivel 2");
        var ticket = await _ticketService.UpdateTicketAsync(ticketId, request, ct);
        return ticket is null
            ? $"Ticket {ticketId} no encontrado."
            : $"AssignmentGroup del ticket {ticketId} actualizado a 'Soporte Nivel 2'.";
    }

    [McpServerTool, Description("Assigns an email to a ticket")]
    public async Task<string> AsignarEmailTicket(
    [Description("ID numérico del ticket")]
    int ticketId,
    [Description("Email del usuario")]
    string email,
    CancellationToken ct = default)
    {
        var request = new UpdateTicketRequest(CreatedByEmail: email);
        var ticket = await _ticketService.UpdateTicketAsync(ticketId, request, ct);
        return ticket is null
            ? $"Ticket {ticketId} no encontrado."
            : $"CreatedByEmail del ticket {ticketId} actualizado.";
    }

    [McpServerTool, Description("Actualiza el sistema afectado de un ticket según el diagnóstico del enrutador.")]
    public async Task<string> ActualizarSistemaAfectado(
        [Description("ID numérico del ticket")]
        int ticketId,
        [Description("Sistema afectado: acceso, reserva, pago, notificacion, escalacion")]
        string sistemaAfectado,
        CancellationToken ct = default)
    {
        var request = new UpdateTicketRequest(AffectedSystem: sistemaAfectado.ToLower());

        var ticket = await _ticketService.UpdateTicketAsync(ticketId, request, ct);

        return ticket is null
            ? $"Ticket {ticketId} no encontrado."
            : $"AffectedSystem del ticket {ticketId} actualizado a '{sistemaAfectado}'.";
    }

    [McpServerTool, Description("Registra un nuevo incidente de soporte. Util para pruebas sin necesidad de ServiceNow.")]
    public async Task<string> RegistrarIncidente(
        [Description("Descripción del problema")]
        string descripcion,
        [Description("Sistema afectado: acceso, turnos, pagos, disponibilidad")]
        string sistema,
        [Description("Tipo de error: dato_incorrecto, operacion_bloqueada, inconsistencia, error_sistema")]
        string tipoError,
        [Description("Email del usuario que reporta")]
        string usuario,
        CancellationToken ct = default)
    {
        var ticket = await _ticketService.CreateFromAgentAsync(
            new CreateAgentTicketRequest(descripcion, sistema, tipoError, usuario), ct);

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ticketId = ticket.Number,
            id = ticket.Id,
            descripcion = ticket.Description,
            sistema,
            tipoError,
            usuario = ticket.CreatedByEmail,
            estado = ticket.StateLabel
        });
    }
}