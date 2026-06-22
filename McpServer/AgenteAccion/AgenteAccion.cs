using System.Text.Json;
using McpServer.Agentes;
using McpServer.Api.Tickets;
using McpServer.Api.Tickets.Dto;
using McpServer.Api.Turnera;
using McpServer.MessageQueue;

namespace McpServer.Agentes;

public class AgenteAccion
{
    private readonly ITicketService _ticketService;
    private readonly ITurneraService _turneraService;
    private readonly ILogger<AgenteAccion> _logger;

    public AgenteAccion(
        ITicketService ticketService,
        ITurneraService turneraService,
        ILogger<AgenteAccion> logger)
    {
        _ticketService = ticketService;
        _turneraService = turneraService;
        _logger = logger;
    }

    public async Task<AccionResult> ProcessAsync(InboundMessage message, CancellationToken ct = default)
    {
        var decision = JsonSerializer.Deserialize<EnrutadorResult>(message.Payload ?? string.Empty,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (decision is null)
        {
            _logger.LogWarning("AgenteAccion no pudo deserializar el Payload para ticket {TicketId}", message.TicketId);
            return AccionResult.Fallido(message.TicketId, "Payload invalido recibido del enrutador.");
        }

        _logger.LogInformation(
            "AgenteAccion ejecutando ticket {TicketId} -> {Agente}",
            decision.TicketId,
            decision.Agente);

        var ticket = await _ticketService.GetTicketAsync(decision.TicketId, ct);
        if (ticket is null)
        {
            _logger.LogWarning("Ticket {TicketId} no encontrado al ejecutar accion", decision.TicketId);
            return AccionResult.Fallido(decision.TicketId.ToString(), $"Ticket {decision.TicketId} no encontrado.");
        }

        return decision.Agente switch
        {
            "AgenteAccionAcceso" => await EjecutarAccesoAsync(ticket, ct),
            "AgenteAccionPago" => await EjecutarPagoAsync(ticket, ct),
            "AgenteAccionTurnos" => await EjecutarTurnoAsync(ticket, ct),
            "AgenteAccionDisponibilidad" => await EjecutarDisponibilidadAsync(ticket, ct),
            _ => await EjecutarEscalacionAsync(ticket, decision.Motivo, ct)
        };
    }

    private async Task<AccionResult> EjecutarAccesoAsync(TicketResponse ticket, CancellationToken ct)
    {
        var email = ticket.CreatedByEmail;

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Ticket {TicketId} sin email asociado, no puedo ejecutar reset de acceso", ticket.Number);
            return AccionResult.PendienteDatos(ticket.Number, "Falta el email del socio para resetear el acceso.");
        }

        var reset = await _turneraService.ResetearAccesoAsync(email, ct);

        if (!reset.Ok)
        {
            var mensajeFalla = $"No pude resetear el acceso para {email}. Motivo: {reset.Mensaje}";
            _logger.LogWarning("Reset de acceso fallido para ticket {TicketId}: {Mensaje}", ticket.Number, mensajeFalla);
            return AccionResult.Fallido(ticket.Number, mensajeFalla);
        }

        var resolucion = string.IsNullOrWhiteSpace(reset.TempPassword)
            ? $"Acceso reseteado para {email}."
            : $"Acceso reseteado para {email}. Password temporal: {reset.TempPassword}.";

        // No se cierra el ticket: queda pendiente de confirmacion del usuario.
        return AccionResult.PendienteConfirmacion(ticket.Number, resolucion);
    }

    private async Task<AccionResult> EjecutarPagoAsync(TicketResponse ticket, CancellationToken ct)
    {
        var email = ticket.CreatedByEmail;

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Ticket {TicketId} sin email asociado, no puedo consultar pagos", ticket.Number);
            return AccionResult.PendienteDatos(ticket.Number, "Falta el email del socio para consultar pagos.");
        }

        var pagos = await _turneraService.ConsultarPagosAsync(email, ct);

        if (!pagos.Ok)
        {
            var mensajeFalla = $"No pude consultar pagos para {email}.";
            _logger.LogWarning("Consulta de pago fallida para ticket {TicketId}", ticket.Number);
            return AccionResult.Fallido(ticket.Number, mensajeFalla);
        }

        var cantidadPagos = pagos.Pagos?.Count ?? 0;
        var disponibles = pagos.Credits?.AvailableClasses ?? 0;
        var pagadas = pagos.Credits?.TotalPaidClasses ?? 0;
        var reservadas = pagos.Credits?.TotalBookedClasses ?? 0;

        var resolucion = $"Pagos consultados para {email}. Pagos registrados: {cantidadPagos}. " +
                          $"Creditos disponibles: {disponibles} ({pagadas} pagadas - {reservadas} reservadas).";

        if (cantidadPagos == 0)
        {
            return AccionResult.PendienteConfirmacion(ticket.Number,
                $"{resolucion} No se cierra el ticket porque no hay pagos registrados para validar la acreditacion.");
        }

        if (disponibles <= 0)
        {
            return AccionResult.PendienteConfirmacion(ticket.Number,
                $"{resolucion} No se cierra el ticket porque el socio no tiene creditos disponibles; requiere acreditacion manual.");
        }

        // No se cierra el ticket: queda pendiente de confirmacion del usuario.
        return AccionResult.PendienteConfirmacion(ticket.Number, resolucion);
    }

    private async Task<AccionResult> EjecutarTurnoAsync(TicketResponse ticket, CancellationToken ct)
    {
        var email = ticket.CreatedByEmail;

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Ticket {TicketId} sin email asociado, no puedo consultar turnos", ticket.Number);
            return AccionResult.PendienteDatos(ticket.Number, "Falta el email del socio para consultar turnos.");
        }

        var turnosResult = await _turneraService.ConsultarTurnosAsync(email, ct);

        if (!turnosResult.Ok)
        {
            var mensajeFalla = $"No pude consultar turnos para {email}.";
            _logger.LogWarning("Consulta de turnos fallida para ticket {TicketId}", ticket.Number);
            return AccionResult.Fallido(ticket.Number, mensajeFalla);
        }

        var turnos = turnosResult.Turnos ?? [];
        string resolucion;

        if (turnos.Count == 0)
        {
            resolucion = $"Turnos consultados para {email}. No hay turnos reservados activos.";
        }
        else
        {
            var detalle = string.Join("\n", turnos.Select((t, i) =>
            {
                var fecha = FormatearFecha(t.Date);
                var extra = string.IsNullOrWhiteSpace(t.TeacherName) ? "" : $" - {t.TeacherName}";
                extra += string.IsNullOrWhiteSpace(t.Specialty) ? "" : $" ({t.Specialty})";
                return $"  {i + 1}. {fecha} a las {t.Time}{extra}";
            }));
            resolucion = $"Turnos consultados para {email}. Turnos reservados: {turnos.Count}.\n{detalle}";
        }

        await CerrarTicketAsync(ticket, resolucion, ct);

        return AccionResult.Resuelto(ticket.Number, resolucion);
    }

    private async Task<AccionResult> EjecutarDisponibilidadAsync(TicketResponse ticket, CancellationToken ct)
    {
        var email = ticket.CreatedByEmail;

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Ticket {TicketId} sin email asociado, no puedo consultar disponibilidad", ticket.Number);
            return AccionResult.PendienteDatos(ticket.Number, "Falta el email del socio para consultar disponibilidad.");
        }

        // Endpoint de disponibilidad pendiente de confirmar con el equipo
        // Por ahora escala hasta que esté implementado en la turnera
        _logger.LogWarning("SubagenteDisponibilidad no implementado aun, escalando ticket {TicketId}", ticket.Number);
        return await EjecutarEscalacionAsync(ticket, "Consulta de disponibilidad no implementada aún en la turnera.", ct);
    }

    private async Task<AccionResult> EjecutarEscalacionAsync(TicketResponse ticket, string motivo, CancellationToken ct)
    {
        _logger.LogInformation("Escalando ticket {TicketId}. Motivo: {Motivo}", ticket.Number, motivo);

        var request = new UpdateTicketRequest(
            Title: null,
            Description: null,
            State: 2,
            StateLabel: "In Progress - Escalated",
            Priority: ticket.Priority <= 2 ? ticket.Priority : 2,
            PriorityLabel: ticket.Priority <= 2 ? ticket.PriorityLabel : "High",
            AssignedTo: null,
            AssignmentGroup: "Nivel 2",
            AffectedSystem: null,
            ResolvedAt: null);

        await _ticketService.UpdateTicketAsync(ticket.Id, request, ct);

        return AccionResult.Escalado(ticket.Number, $"Escalado a Nivel 2. Motivo: {motivo}");
    }

    private async Task CerrarTicketAsync(TicketResponse ticket, string resolucion, CancellationToken ct)
    {
        var request = new UpdateTicketRequest(
            Title: null,
            Description: $"{ticket.Description}\n\nResolucion: {resolucion}",
            State: 4,
            StateLabel: "Resolved",
            Priority: null,
            PriorityLabel: null,
            AssignedTo: null,
            AssignmentGroup: null,
            AffectedSystem: null,
            ResolvedAt: DateTime.UtcNow);

        await _ticketService.UpdateTicketAsync(ticket.Id, request, ct);
    }

    private static string FormatearFecha(string date)
    {
        if (!DateOnly.TryParse(date, out var d))
            return date;

        var dia = d.DayOfWeek switch
        {
            DayOfWeek.Monday => "Lunes",
            DayOfWeek.Tuesday => "Martes",
            DayOfWeek.Wednesday => "Miercoles",
            DayOfWeek.Thursday => "Jueves",
            DayOfWeek.Friday => "Viernes",
            DayOfWeek.Saturday => "Sabado",
            DayOfWeek.Sunday => "Domingo",
            _ => ""
        };

        return $"{dia} {d:dd/MM/yyyy}";
    }
}

public enum AccionEstado
{
    Resuelto,
    PendienteConfirmacion,
    PendienteDatos,
    Escalado,
    Fallido
}

public record AccionResult(string TicketId, AccionEstado Estado, string Mensaje)
{
    public static AccionResult Resuelto(string ticketId, string mensaje) =>
        new(ticketId, AccionEstado.Resuelto, mensaje);

    public static AccionResult PendienteConfirmacion(string ticketId, string mensaje) =>
        new(ticketId, AccionEstado.PendienteConfirmacion, mensaje);

    public static AccionResult PendienteDatos(string ticketId, string mensaje) =>
        new(ticketId, AccionEstado.PendienteDatos, mensaje);

    public static AccionResult Escalado(string ticketId, string mensaje) =>
        new(ticketId, AccionEstado.Escalado, mensaje);

    public static AccionResult Fallido(string ticketId, string mensaje) =>
        new(ticketId, AccionEstado.Fallido, mensaje);
}