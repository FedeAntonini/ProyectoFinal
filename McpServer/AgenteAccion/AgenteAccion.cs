using System.Text.Json;
using McpServer.Api.Tickets;
using McpServer.Api.Tickets.Dto;
using McpServer.Api.Turnera;
using McpServer.MessageQueue;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpServer.Agentes;

public class AgenteAccion
{
    private readonly ITicketService _ticketService;
    private readonly ITurneraService _turneraService;
    private readonly IConfiguration _config;
    private readonly ILogger<AgenteAccion> _logger;

    public AgenteAccion(
        ITicketService ticketService,
        ITurneraService turneraService,
        IConfiguration config,
        ILogger<AgenteAccion> logger)
    {
        _ticketService = ticketService;
        _turneraService = turneraService;
        _config = config;
        _logger = logger;
    }

    public async Task<AccionResult> ProcessAsync(InboundMessage message, CancellationToken ct = default)
    {
        var decision = JsonSerializer.Deserialize<EnrutadorAccionPayload>(message.Payload ?? string.Empty,
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

        await using var mcpClient = await CreateMcpClientAsync(ct);

        return decision.Agente switch
        {
            "AgenteAccionAcceso" => await EjecutarAccesoAsync(ticket, mcpClient, message, decision.ConversationId, ct),
            "AgenteAccionPago" => await EjecutarPagoAsync(ticket, mcpClient, message, decision.ConversationId, ct),
            "AgenteAccionTurnos" => await EjecutarTurnoAsync(ticket, mcpClient, message, decision.ConversationId, ct),
            "AgenteAccionDisponibilidad" => await EjecutarDisponibilidadAsync(ticket, mcpClient, message, decision.ConversationId, ct),
            _ => await EjecutarEscalacionAsync(ticket, decision.Motivo, mcpClient, message, decision.ConversationId, ct)
        };
    }

    private async Task<McpClient> CreateMcpClientAsync(CancellationToken ct)
    {
        var mcpUrl = _config["McpServer:BaseUrl"] ?? throw new InvalidOperationException("Missing McpServer:BaseUrl");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{mcpUrl}/mcp"),
            Name = "McpServer"
        });

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private async Task NotificarUsuarioAsync(McpClient mcpClient, InboundMessage message, int conversationId, string texto, CancellationToken ct)
    {
        await mcpClient.CallToolAsync(
            "send_outbound_message",
            new Dictionary<string, object?>
            {
                ["ticketId"] = message.TicketId,
                ["correlationId"] = message.CorrelationId,
                ["customerId"] = message.CustomerId,
                ["targetAgent"] = "accion",
                ["action"] = "send_message",
                ["payload"] = JsonSerializer.Serialize(new
                {
                    ConversationId = conversationId,
                    Body = texto,
                    MessageType = "text"
                })
            },
            cancellationToken: ct);
    }

    private async Task<AccionResult> EjecutarAccesoAsync(TicketResponse ticket, McpClient mcpClient, InboundMessage message, int conversationId, CancellationToken ct)
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
            ? "Tu acceso fue reseteado correctamente."
            : $"Tu acceso fue reseteado. Tu nueva contraseña temporal es: {reset.TempPassword}. Por favor cambiala cuando ingreses.";

        await NotificarUsuarioAsync(mcpClient, message, conversationId, resolucion, ct);

        return AccionResult.PendienteConfirmacion(ticket.Number, resolucion);
    }

    private async Task<AccionResult> EjecutarPagoAsync(TicketResponse ticket, McpClient mcpClient, InboundMessage message, int conversationId, CancellationToken ct)
    {
        var email = ticket.CreatedByEmail;

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Ticket {TicketId} sin email asociado, no puedo consultar pagos", ticket.Number);
            return AccionResult.PendienteDatos(ticket.Number, "Falta el email del socio para consultar pagos.");
        }

        var pagos = await _turneraService.ConsultarPagosAsync(email, ct);

        if (pagos.Credits is null && (pagos.Pagos is null || pagos.Pagos.Count == 0))
        {
            _logger.LogWarning("Consulta de pago fallida para ticket {TicketId}", ticket.Number);
            return AccionResult.Fallido(ticket.Number, $"No pude consultar pagos para {email}.");
        }

        var cantidadPagos = pagos.Pagos?.Count ?? 0;
        var disponibles = pagos.Credits?.AvailableClasses ?? 0;
        var pagadas = pagos.Credits?.TotalPaidClasses ?? 0;
        var reservadas = pagos.Credits?.TotalBookedClasses ?? 0;

        var resolucion = $"Consulté tu situación de pagos:\n" +
                         $"• Pagos registrados: {cantidadPagos}\n" +
                         $"• Clases disponibles: {disponibles}\n" +
                         $"• Clases pagadas: {pagadas}\n" +
                         $"• Clases reservadas: {reservadas}";

        await NotificarUsuarioAsync(mcpClient, message, conversationId, resolucion, ct);

        return AccionResult.PendienteConfirmacion(ticket.Number, resolucion);
    }

    private async Task<AccionResult> EjecutarTurnoAsync(TicketResponse ticket, McpClient mcpClient, InboundMessage message, int conversationId, CancellationToken ct)
    {
        var email = ticket.CreatedByEmail;

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Ticket {TicketId} sin email asociado, no puedo consultar turnos", ticket.Number);
            return AccionResult.PendienteDatos(ticket.Number, "Falta el email del socio para consultar turnos.");
        }

        var turnosResult = await _turneraService.ConsultarTurnosAsync(email, ct);

        string resolucion;

        if (turnosResult.Turnos is null || turnosResult.Turnos.Count == 0)
        {
            resolucion = $"No encontré turnos reservados activos para {email}.";
        }
        else
        {
            var detalle = string.Join("\n", turnosResult.Turnos.Select((t, i) =>
            {
                var fecha = FormatearFecha(t.Date);
                var extra = string.IsNullOrWhiteSpace(t.TeacherName) ? "" : $" - {t.TeacherName}";
                extra += string.IsNullOrWhiteSpace(t.Specialty) ? "" : $" ({t.Specialty})";
                return $"  {i + 1}. {fecha} a las {t.Time}{extra}";
            }));
            resolucion = $"Tus turnos reservados son:\n{detalle}";
        }

        await NotificarUsuarioAsync(mcpClient, message, conversationId, resolucion, ct);
        await CerrarTicketAsync(ticket, resolucion, ct);

        return AccionResult.Resuelto(ticket.Number, resolucion);
    }

    private async Task<AccionResult> EjecutarDisponibilidadAsync(TicketResponse ticket, McpClient mcpClient, InboundMessage message, int conversationId, CancellationToken ct)
    {
        var email = ticket.CreatedByEmail;

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Ticket {TicketId} sin email asociado", ticket.Number);
            return AccionResult.PendienteDatos(ticket.Number, "Falta el email del socio para consultar disponibilidad.");
        }

        _logger.LogWarning("SubagenteDisponibilidad no implementado aun, escalando ticket {TicketId}", ticket.Number);
        return await EjecutarEscalacionAsync(ticket, "Consulta de disponibilidad no implementada aún en la turnera.", mcpClient, message, conversationId, ct);
    }

    private async Task<AccionResult> EjecutarEscalacionAsync(TicketResponse ticket, string motivo, McpClient mcpClient, InboundMessage message, int conversationId, CancellationToken ct)
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

        var mensajeEscalacion = "Tu consulta requiere atención especializada. La derivamos al equipo de soporte nivel 2 que te contactará a la brevedad.";

        await NotificarUsuarioAsync(mcpClient, message, conversationId, mensajeEscalacion, ct);

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

public record EnrutadorAccionPayload(int TicketId, string Agente, string Motivo, int ConversationId);

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