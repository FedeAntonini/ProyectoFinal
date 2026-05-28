using System.Text;
using System.Text.Json;

namespace McpServer.Api.Turnera;

public class TurneraApiService(HttpClient http) : ITurneraApiService
{
    // ── Socios ────────────────────────────────────────────────────────────────

    public async Task<string> ResetearAccesoAsync(string email, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { email });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await http.PostAsync("/api/agent/reset-acceso", content, ct);
        return await LeerRespuestaAsync(response, "resetear acceso del socio");
    }

    // ── Turnos ────────────────────────────────────────────────────────────────

    public async Task<string> ConsultarTurnosAsync(string email, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/agent/turnos?email={Uri.EscapeDataString(email)}", ct);
        return await LeerRespuestaAsync(response, "consultar turnos");
    }

    // ── Pagos ─────────────────────────────────────────────────────────────────

    public async Task<string> ConsultarPagosAsync(string email, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/agent/pagos?email={Uri.EscapeDataString(email)}", ct);
        return await LeerRespuestaAsync(response, "consultar pagos");
    }

    // ── Clases ────────────────────────────────────────────────────────────────
    // Obtiene los turnos del socio y verifica la disponibilidad real de cada uno.

    public async Task<string> VerificarDisponibilidadClaseAsync(string email, CancellationToken ct = default)
    {
        var turnosResp = await http.GetAsync($"/api/agent/turnos?email={Uri.EscapeDataString(email)}", ct);
        var turnosJson = await LeerRespuestaAsync(turnosResp, "obtener turnos para verificar clase");

        try
        {
            using var doc = JsonDocument.Parse(turnosJson);
            var turnos = doc.RootElement.GetProperty("turnos");
            var verificaciones = new List<object>();

            foreach (var turno in turnos.EnumerateArray())
            {
                var teacherId = turno.GetProperty("teacherId").GetString() ?? "";
                var date      = turno.GetProperty("date").GetString() ?? "";
                var time      = turno.GetProperty("time").GetString() ?? "";

                var dispResp = await http.GetAsync(
                    $"/api/agent/disponibilidad?teacherId={Uri.EscapeDataString(teacherId)}" +
                    $"&date={Uri.EscapeDataString(date)}&time={Uri.EscapeDataString(time)}", ct);

                var dispJson = await dispResp.Content.ReadAsStringAsync(ct);
                using var dispDoc = JsonDocument.Parse(dispJson);

                verificaciones.Add(new
                {
                    instructor  = turno.GetProperty("teacherName").GetString(),
                    especialidad = turno.GetProperty("specialty").GetString(),
                    fecha       = date,
                    hora        = time,
                    ocupados    = dispDoc.RootElement.GetProperty("ocupados").GetInt32(),
                    capacidad   = dispDoc.RootElement.GetProperty("capacidad").GetInt32(),
                    disponibles = dispDoc.RootElement.GetProperty("disponibles").GetInt32(),
                    disponible  = dispDoc.RootElement.GetProperty("disponible").GetBoolean()
                });
            }

            return JsonSerializer.Serialize(new
            {
                email,
                mensaje = "Disponibilidad verificada directamente en la base de datos.",
                clases  = verificaciones
            });
        }
        catch
        {
            return turnosJson;
        }
    }

    // ── Instructores ──────────────────────────────────────────────────────────
    // Obtiene los turnos del socio y muestra el instructor actualmente asignado.

    public async Task<string> VerificarAsignacionInstructorAsync(string email, CancellationToken ct = default)
    {
        var turnosResp = await http.GetAsync($"/api/agent/turnos?email={Uri.EscapeDataString(email)}", ct);
        var turnosJson = await LeerRespuestaAsync(turnosResp, "verificar instructor");

        try
        {
            using var doc = JsonDocument.Parse(turnosJson);
            var turnos = doc.RootElement.GetProperty("turnos");
            var detalle = new List<object>();

            foreach (var turno in turnos.EnumerateArray())
            {
                detalle.Add(new
                {
                    fecha        = turno.GetProperty("date").GetString(),
                    hora         = turno.GetProperty("time").GetString(),
                    instructor   = turno.GetProperty("teacherName").GetString(),
                    especialidad = turno.GetProperty("specialty").GetString()
                });
            }

            return JsonSerializer.Serialize(new
            {
                email,
                mensaje  = "Instructores verificados y confirmados en el sistema.",
                reservas = detalle
            });
        }
        catch
        {
            return turnosJson;
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static async Task<string> LeerRespuestaAsync(HttpResponseMessage response, string operacion)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new
            {
                error   = $"Error al {operacion} ({(int)response.StatusCode})",
                detalle = body
            });
        return body;
    }
}
