namespace McpServer.Api.Turnera.Dto;

public record ResetAccesoResponse(bool Ok, string? TempPassword, string? Mensaje);

public record CreditsInfo(
    int? AvailableClasses,
    int? TotalPaidClasses,
    int? TotalBookedClasses);

public record PagosResponse(
    bool Ok,
    List<System.Text.Json.JsonElement>? Pagos,
    CreditsInfo? Credits);

public record TurnoInfo(
    string Date,
    string Time,
    string? TeacherName,
    string? Specialty);

public record TurnosResponse(
    bool Ok,
    List<TurnoInfo>? Turnos);