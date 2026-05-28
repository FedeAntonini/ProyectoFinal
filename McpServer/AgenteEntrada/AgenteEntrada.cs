using McpServer.MessageQueue;
using McpServer.Services;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpServer.Agentes;

public class AgenteEntrada
{
    private readonly ILogger<AgenteEntrada> _logger;
    private readonly IConfiguration _config;

    public AgenteEntrada(
        ILogger<AgenteEntrada> logger,
        IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<EntradaResult> ProcessAsync(InboundMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Procesando ticket — TicketId: {TicketId}, CustomerId: {CustomerId}",
            message.TicketId,
            message.CustomerId);

        await using var mcpClient = await CreateMcpClientAsync(ct);
        var runId = await CreateAgentRunAsync(mcpClient, int.Parse(message.TicketId), ct);

        try
        {
            // ── FASE 1: Obtener datos del ticket ────────────────────────────
            var ticketRaw = await CallToolAsync(mcpClient, "obtener_ticket",
                new Dictionary<string, object?> { ["ticketId"] = message.TicketId }, ct);

            _logger.LogInformation("Ticket obtenido: {TicketData}", ticketRaw);

            var ticket = ParseTicket(ticketRaw);
            if (ticket == null)
            {
                _logger.LogWarning("Ticket {TicketId} no encontrado o no se pudo parsear", message.TicketId);
                await UpdateAgentRunStatusAsync(mcpClient, runId, "failed", ct);
                return EntradaResult.Accepted(message);
            }

            // ── FASE 2: Diagnosticar via Knowledge Base ─────────────────────
            var diagnosticoRaw = await CallToolAsync(mcpClient, "diagnosticar_problema",
                new Dictionary<string, object?>
                {
                    ["descripcion"] = ticket.Problema,
                    ["sistema"]     = ticket.Sistema
                }, ct);

            _logger.LogInformation("Diagnóstico KB: {Diagnostico}", diagnosticoRaw);

            var decision = ParseDecision(diagnosticoRaw);

            // ── FASE 3: Ejecutar acción según sistema y decisión ───────────
            string accion;
            string resultado;

            if (decision == "escalar")
            {
                accion   = "Escalado a nivel 2";
                resultado = "El problema requiere atención de soporte nivel 2.";
                _logger.LogInformation("Ticket {TicketId} escalado a nivel 2", message.TicketId);
            }
            else
            {
                (accion, resultado) = await EjecutarAccionAsync(mcpClient, message.TicketId, ticket, ct);
            }

            _logger.LogInformation(
                "Ticket {TicketId} resuelto — Acción: {Accion} | Resultado: {Resultado}",
                message.TicketId, accion, resultado);

            await UpdateAgentRunStatusAsync(mcpClient, runId, "completed", ct);
            return EntradaResult.Accepted(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando ticket {TicketId}", message.TicketId);
            await UpdateAgentRunStatusAsync(mcpClient, runId, "failed", ct);
            throw;
        }
    }

    // ── Acciones por sistema ────────────────────────────────────────────────

    private async Task<(string Accion, string Resultado)> EjecutarAccionAsync(
        McpClient mcpClient, string ticketId, TicketInfo ticket, CancellationToken ct)
    {
        return ticket.Sistema.ToLower() switch
        {
            "socios"       => await EjecutarAccionSocioAsync(mcpClient, ticketId, ticket.Usuario, ct),
            "turnos"       => await EjecutarAccionTurnoAsync(mcpClient, ticketId, ticket.Usuario, ct),
            "pagos"        => await EjecutarAccionPagoAsync(mcpClient, ticketId, ticket.Usuario, ct),
            "clases"       => await EjecutarAccionClaseAsync(mcpClient, ticketId, ticket.Usuario, ticket.Problema, ct),
            "instructores" => await EjecutarAccionInstructorAsync(mcpClient, ticketId, ticket.Usuario, ticket.Problema, ct),
            _              => ("Escalado", "Sistema no reconocido. Escalado a nivel 2.")
        };
    }

    private async Task<(string, string)> EjecutarAccionSocioAsync(
        McpClient client, string ticketId, string email, CancellationToken ct)
    {
        var resultado = await CallToolAsync(client, "resetear_acceso_socio",
            new Dictionary<string, object?> { ["email"] = email }, ct);
        await CallToolAsync(client, "cerrar_ticket_socio",
            new Dictionary<string, object?> { ["ticketId"] = ticketId, ["resolucion"] = "Acceso reseteado y link de recuperación enviado." }, ct);
        return ("Resetear acceso socio", resultado);
    }

    private async Task<(string, string)> EjecutarAccionTurnoAsync(
        McpClient client, string ticketId, string email, CancellationToken ct)
    {
        var resultado = await CallToolAsync(client, "consultar_turno",
            new Dictionary<string, object?> { ["email"] = email }, ct);
        await CallToolAsync(client, "cerrar_ticket_turno",
            new Dictionary<string, object?> { ["ticketId"] = ticketId, ["resolucion"] = "Turno verificado y reprogramado." }, ct);
        return ("Consultar turno", resultado);
    }

    private async Task<(string, string)> EjecutarAccionPagoAsync(
        McpClient client, string ticketId, string email, CancellationToken ct)
    {
        var resultado = await CallToolAsync(client, "consultar_cuota",
            new Dictionary<string, object?> { ["email"] = email }, ct);
        await CallToolAsync(client, "cerrar_ticket_pago",
            new Dictionary<string, object?> { ["ticketId"] = ticketId, ["resolucion"] = "Pago verificado y créditos acreditados." }, ct);
        return ("Consultar cuota", resultado);
    }

    private async Task<(string, string)> EjecutarAccionClaseAsync(
        McpClient client, string ticketId, string email, string problema, CancellationToken ct)
    {
        var resultado = await CallToolAsync(client, "habilitar_clase",
            new Dictionary<string, object?> { ["email"] = email, ["clase"] = problema }, ct);
        await CallToolAsync(client, "cerrar_ticket_clase",
            new Dictionary<string, object?> { ["ticketId"] = ticketId, ["resolucion"] = "Disponibilidad verificada y clase habilitada." }, ct);
        return ("Verificar disponibilidad clase", resultado);
    }

    private async Task<(string, string)> EjecutarAccionInstructorAsync(
        McpClient client, string ticketId, string email, string problema, CancellationToken ct)
    {
        var resultado = await CallToolAsync(client, "asignar_instructor",
            new Dictionary<string, object?> { ["email"] = email, ["clase"] = problema }, ct);
        await CallToolAsync(client, "cerrar_ticket_instructor",
            new Dictionary<string, object?> { ["ticketId"] = ticketId, ["resolucion"] = "Instructor verificado y confirmado correctamente." }, ct);
        return ("Verificar instructor", resultado);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<string> CallToolAsync(
        McpClient client, string tool, Dictionary<string, object?> args, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: ct);
        return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
    }

    private static TicketInfo? ParseTicket(string raw)
    {
        try
        {
            // El formato es: "Ticket INC0001: {...json...}"
            var jsonStart = raw.IndexOf('{');
            if (jsonStart < 0) return null;
            var json = raw[jsonStart..];
            return JsonSerializer.Deserialize<TicketInfo>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static string ParseDecision(string diagnosticoJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(diagnosticoJson);
            return doc.RootElement.GetProperty("decision").GetString() ?? "escalar";
        }
        catch { return "escalar"; }
    }

    private async Task<McpClient> CreateMcpClientAsync(CancellationToken ct)
    {
        var mcpUrl = _config["McpServer:BaseUrl"]
            ?? throw new InvalidOperationException("Missing McpServer:BaseUrl");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{mcpUrl}/mcp"),
            Name     = "McpServer"
        });

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private async Task<int> CreateAgentRunAsync(McpClient client, int ticketId, CancellationToken ct)
    {
        var result = await client.CallToolAsync(
            "create_agent_run",
            new Dictionary<string, object?> { ["ticketId"] = ticketId },
            cancellationToken: ct);

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("create_agent_run returned no text content.");

        var run = JsonSerializer.Deserialize<AgentRunResult>(text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize agent run.");

        return run.Id;
    }

    private async Task UpdateAgentRunStatusAsync(McpClient client, int runId, string status, CancellationToken ct)
    {
        await client.CallToolAsync(
            "update_agent_run_status",
            new Dictionary<string, object?> { ["runId"] = runId, ["status"] = status },
            cancellationToken: ct);
    }

    private record TicketInfo(string Usuario, string Problema, string Prioridad, string Sistema);
    private record AgentRunResult(int Id, int TicketId, string Status, DateTime StartedAt, DateTime? EndedAt);
}
