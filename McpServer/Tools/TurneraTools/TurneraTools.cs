using McpServer.Api.Turnera;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpServer.Tools;

[McpServerToolType]
public class TurneraTools
{
    private readonly ITurneraService _turneraService;

    public TurneraTools(ITurneraService turneraService)
    {
        _turneraService = turneraService;
    }

    [McpServerTool, Description("Resetea el acceso de un socio de la turnera que no puede iniciar sesion. Devuelve una password temporal si corresponde.")]
    public async Task<string> ResetearAccesoTurnera(
        [Description("Email del socio con problema de acceso")]
        string email,
        CancellationToken ct = default)
    {
        var result = await _turneraService.ResetearAccesoAsync(email, ct);

        if (!result.Ok)
            return $"No se pudo resetear el acceso para {email}. {result.Mensaje}";

        return string.IsNullOrWhiteSpace(result.TempPassword)
            ? $"Acceso reseteado para {email}."
            : $"Acceso reseteado para {email}. Password temporal: {result.TempPassword}.";
    }

    [McpServerTool, Description("Consulta los pagos registrados y los creditos disponibles de un socio de la turnera.")]
    public async Task<string> ConsultarPagosTurnera(
        [Description("Email del socio con problema de pago")]
        string email,
        CancellationToken ct = default)
    {
        var result = await _turneraService.ConsultarPagosAsync(email, ct);

        if (result.Credits is null && (result.Pagos is null || result.Pagos.Count == 0))
            return $"No se pudieron consultar los pagos para {email}.";

        var cantidadPagos = result.Pagos?.Count ?? 0;
        var disponibles = result.Credits?.AvailableClasses ?? 0;
        var pagadas = result.Credits?.TotalPaidClasses ?? 0;
        var reservadas = result.Credits?.TotalBookedClasses ?? 0;

        return $"Pagos registrados para {email}: {cantidadPagos}. Creditos disponibles: {disponibles} ({pagadas} pagadas - {reservadas} reservadas).";
    }

    [McpServerTool, Description("Consulta los turnos reservados de un socio de la turnera.")]
    public async Task<string> ConsultarTurnosTurnera(
        [Description("Email del socio con consulta de turnos")]
        string email,
        CancellationToken ct = default)
    {
        var result = await _turneraService.ConsultarTurnosAsync(email, ct);

        if (result.Turnos is null)
            return $"No se pudieron consultar los turnos para {email}.";

        var turnos = result.Turnos ?? [];
        if (turnos.Count == 0)
            return $"No hay turnos reservados activos para {email}.";

        var detalle = string.Join("\n", turnos.Select((t, i) =>
        {
            var extra = string.IsNullOrWhiteSpace(t.TeacherName) ? "" : $" - {t.TeacherName}";
            extra += string.IsNullOrWhiteSpace(t.Specialty) ? "" : $" ({t.Specialty})";
            return $"{i + 1}. {t.Date} a las {t.Time}{extra}";
        }));

        return $"Turnos reservados para {email}: {turnos.Count}.\n{detalle}";
    }
}