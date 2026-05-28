namespace McpServer.Api.Turnera;

public interface ITurneraApiService
{
    Task<string> ResetearAccesoAsync(string email, CancellationToken ct = default);
    Task<string> ConsultarTurnosAsync(string email, CancellationToken ct = default);
    Task<string> ConsultarPagosAsync(string email, CancellationToken ct = default);
    Task<string> VerificarDisponibilidadClaseAsync(string email, CancellationToken ct = default);
    Task<string> VerificarAsignacionInstructorAsync(string email, CancellationToken ct = default);
}
