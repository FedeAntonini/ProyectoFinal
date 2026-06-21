using McpServer.Api.Turnera.Dto;

namespace McpServer.Api.Turnera;

public interface ITurneraService
{
    Task<ResetAccesoResponse> ResetearAccesoAsync(string email, CancellationToken ct = default);
    Task<PagosResponse> ConsultarPagosAsync(string email, CancellationToken ct = default);
    Task<TurnosResponse> ConsultarTurnosAsync(string email, CancellationToken ct = default);
}