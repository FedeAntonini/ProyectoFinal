using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var API_KEY = Environment.GetEnvironmentVariable("GROQ_API_KEY");
const string MODELO = "llama-3.3-70b-versatile";

Console.WriteLine("Conectando al MCP Server...");

var transporte = new StdioClientTransport(new StdioClientTransportOptions
{
    Command = "dotnet",
    Arguments = ["run", "--no-build", "--project", "../McpServer"],
    Name = "SoporteMcpServer",
});

await using var mcp = await McpClient.CreateAsync(transporte);
var todasLasTools = await mcp.ListToolsAsync();

var toolsPedido = todasLasTools
    .Where(t => t.Name is "consultar_estado_pedido")
    .ToList();

var toolsAcceso = todasLasTools
    .Where(t => t.Name is "resetear_acceso")
    .ToList();

var toolsPago = todasLasTools
    .Where(t => t.Name is "consultar_pago")
    .ToList();

var toolsPrecio = todasLasTools
    .Where(t => t.Name is "corregir_precio")
    .ToList();

var toolsStock = todasLasTools
    .Where(t => t.Name is "sincronizar_stock")
    .ToList();

var toolsEscalacion = todasLasTools
    .Where(t => t.Name is "escalar_ticket")
    .ToList();

IChatClient CrearSubagente() =>
    string.IsNullOrWhiteSpace(API_KEY)
        ? throw new Exception("Falta la variable de entorno GROQ_API_KEY")
        :
    new OpenAIClient(
        new ApiKeyCredential(API_KEY),
        new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.groq.com/openai/v1/")
        })
    .GetChatClient(MODELO)
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var promptLoader = new AgentPromptLoader();
var systemPedido = new ChatMessage(ChatRole.System, promptLoader.Load("pedido.md"));
var systemAcceso = new ChatMessage(ChatRole.System, promptLoader.Load("acceso.md"));
var systemPago = new ChatMessage(ChatRole.System, promptLoader.Load("pago.md"));
var systemTurnos = new ChatMessage(ChatRole.System, promptLoader.Load("turnos.md"));
var systemPrecio = new ChatMessage(ChatRole.System, promptLoader.Load("precio.md"));
var systemStock = new ChatMessage(ChatRole.System, promptLoader.Load("stock.md"));
var systemEscalacion = new ChatMessage(ChatRole.System, promptLoader.Load("escalacion.md"));

Console.WriteLine("=== Agente de Accion - Turnera Pilates Soporte N1 ===");
Console.WriteLine("Pega el diagnostico del AgenteEntrada (o 'salir' para terminar):");
Console.WriteLine();

while (true)
{
    Console.Write("Diagnostico: ");
    var diagnostico = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(diagnostico) || diagnostico.ToLower() == "salir") break;

    try
    {
        if (await TryHandleDeterministicAccessAsync(diagnostico))
            continue;

        if (await TryHandleDeterministicPaymentAsync(diagnostico))
            continue;

        if (await TryHandleDeterministicTurnsAsync(diagnostico))
            continue;

        if (await TryHandleDeterministicAvailabilityAsync(diagnostico))
            continue;

        if (diagnostico.Contains("AgenteAccionPedido", StringComparison.OrdinalIgnoreCase) ||
            diagnostico.Contains("pedido", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: PEDIDO");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemPedido, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsPedido] }
            );
            Console.WriteLine($"\n[SubagentePedido] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionAcceso", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("acceso", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: ACCESO");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemAcceso, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsAcceso] }
            );
            Console.WriteLine($"\n[SubagenteAcceso] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionPago", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("pago", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: PAGO");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemPago, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsPago] }
            );
            Console.WriteLine($"\n[SubagentePago] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionTurnos", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("consultar_turnos", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: TURNOS");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemTurnos, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsPedido] }
            );
            Console.WriteLine($"\n[SubagenteTurnos] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionDisponibilidad", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("consultar_disponibilidad", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("disponibilidad", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: DISPONIBILIDAD");
            Console.WriteLine("\n[SubagenteDisponibilidad] Faltan datos estructurados para consultar disponibilidad. Necesito profesor, fecha YYYY-MM-DD y horario HH:mm.\n");
        }
        else if (diagnostico.Contains("AgenteAccionPrecio", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("precio", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("catalogo", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: PRECIO");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemPrecio, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsPrecio] }
            );
            Console.WriteLine($"\n[SubagentePrecio] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionStock", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("stock", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: STOCK");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemStock, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsStock] }
            );
            Console.WriteLine($"\n[SubagenteStock] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("Escalacion", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("escalar", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: ESCALACION");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemEscalacion, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsEscalacion] }
            );
            Console.WriteLine($"\n[SubagenteEscalacion] {respuesta.Text}\n");
        }
        else
        {
            Console.WriteLine("\n[AgenteAccion] No se encontra subagente. Escalando a nivel 2.\n");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Seson Terminada.");

static async Task<bool> TryHandleDeterministicAccessAsync(string diagnostico)
{
    if (!diagnostico.Contains("AgenteAccionAcceso", StringComparison.OrdinalIgnoreCase) &&
        !diagnostico.Contains("resetear_acceso", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var email = ExtractEmail(diagnostico);
    var ticketId = ExtractTicketNumber(diagnostico);

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(ticketId))
    {
        Console.WriteLine("\n[SubagenteAcceso] Faltan datos para ejecutar acceso. Necesito TICKET y USUARIO/email.\n");
        return true;
    }

    Console.WriteLine("\n[AgenteAccion] Levantando subagente: ACCESO");

    var reset = await ResetTurneraAccessAsync(email);
    if (!reset.Ok)
    {
        Console.WriteLine($"\n[SubagenteAcceso] No pude resetear el acceso para {email}. Motivo: {reset.Message}\n");
        return true;
    }

    var resolution = string.IsNullOrWhiteSpace(reset.TempPassword)
        ? $"Acceso reseteado para {email}."
        : $"Acceso reseteado para {email}. Password temporal: {reset.TempPassword}.";

    Console.WriteLine($"\n[SubagenteAcceso] {resolution} No cierro el ticket; queda pendiente de confirmacion del usuario.\n");
    return true;
}

static async Task<bool> TryHandleDeterministicPaymentAsync(string diagnostico)
{
    if (!diagnostico.Contains("AgenteAccionPago", StringComparison.OrdinalIgnoreCase) &&
        !diagnostico.Contains("consultar_pago", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var email = ExtractEmail(diagnostico);
    var ticketId = ExtractTicketNumber(diagnostico);

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(ticketId))
    {
        Console.WriteLine("\n[SubagentePago] Faltan datos para consultar pagos. Necesito TICKET y USUARIO/email.\n");
        return true;
    }

    Console.WriteLine("\n[AgenteAccion] Levantando subagente: PAGO");

    var payment = await GetTurneraPaymentsAsync(email);
    if (!payment.Ok)
    {
        Console.WriteLine($"\n[SubagentePago] No pude consultar pagos para {email}. Motivo: {payment.Message}\n");
        return true;
    }

    var resolution = $"Pagos consultados para {email}. Pagos registrados: {payment.PaymentCount}. Creditos disponibles: {payment.Credits}.";
    if (payment.PaymentCount == 0)
    {
        Console.WriteLine($"\n[SubagentePago] {resolution} No cierro el ticket porque no hay pagos registrados para validar la acreditacion.\n");
        return true;
    }

    if (payment.Credits <= 0)
    {
        Console.WriteLine($"\n[SubagentePago] {resolution} No cierro el ticket porque el socio no tiene creditos disponibles; requiere acreditacion manual o una accion especifica de acreditacion.\n");
        return true;
    }

    Console.WriteLine($"\n[SubagentePago] {resolution} No cierro el ticket; queda pendiente de confirmacion del usuario.\n");
    return true;
}

static async Task<bool> TryHandleDeterministicTurnsAsync(string diagnostico)
{
    if (!diagnostico.Contains("AgenteAccionTurnos", StringComparison.OrdinalIgnoreCase) &&
        !diagnostico.Contains("consultar_turnos", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var email = ExtractEmail(diagnostico);
    var ticketId = ExtractTicketNumber(diagnostico);

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(ticketId))
    {
        Console.WriteLine("\n[SubagenteTurnos] Faltan datos para consultar turnos. Necesito TICKET y USUARIO/email.\n");
        AgentActionMemory.Save(ticketId, "turnos", "SubagenteTurnos", "no_resueltos", diagnostico, "Faltan datos para consultar turnos.", "consultar_turnos");
        return true;
    }

    Console.WriteLine("\n[AgenteAccion] Levantando subagente: TURNOS");

    var turns = await GetTurneraTurnsAsync(email);
    if (!turns.Ok)
    {
        Console.WriteLine($"\n[SubagenteTurnos] No pude consultar turnos para {email}. Motivo: {turns.Message}\n");
        AgentActionMemory.Save(ticketId, "turnos", "SubagenteTurnos", "no_resueltos", diagnostico, turns.Message, "consultar_turnos");
        return true;
    }

    var resolution = $"Turnos consultados para {email}. Reservas registradas: {turns.BookingCount}. Creditos disponibles: {turns.Credits}.";
    if (turns.BookingCount == 0)
    {
        Console.WriteLine($"\n[SubagenteTurnos] {resolution} No encontre reservas registradas; requiere seguimiento o correccion manual si el usuario tenia confirmacion.\n");
        AgentActionMemory.Save(ticketId, "turnos", "SubagenteTurnos", "no_resueltos", diagnostico, $"{resolution} Sin reservas registradas.", "consultar_turnos");
        return true;
    }

    Console.WriteLine($"\n[SubagenteTurnos] {resolution} No cierro el ticket; queda pendiente de confirmacion del usuario.\n");
    AgentActionMemory.Save(ticketId, "turnos", "SubagenteTurnos", "resueltos", diagnostico, resolution, "consultar_turnos");
    return true;
}

static async Task<bool> TryHandleDeterministicAvailabilityAsync(string diagnostico)
{
    if (!diagnostico.Contains("AgenteAccionDisponibilidad", StringComparison.OrdinalIgnoreCase) &&
        !diagnostico.Contains("consultar_disponibilidad", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    Console.WriteLine("\n[AgenteAccion] Levantando subagente: DISPONIBILIDAD");

    var teacher = ExtractTeacher(diagnostico);
    var date = ExtractDate(diagnostico);
    var time = ExtractTime(diagnostico);

    if (teacher is null || string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
    {
        Console.WriteLine("\n[SubagenteDisponibilidad] Faltan datos para consultar disponibilidad. Necesito profesor (Valentina, Martin o Camila), fecha YYYY-MM-DD y horario HH:mm.\n");
        return true;
    }

    var availability = await GetTurneraAvailabilityAsync(teacher.Value.Id, date, time);
    if (!availability.Ok)
    {
        Console.WriteLine($"\n[SubagenteDisponibilidad] No pude consultar disponibilidad para {teacher.Value.Name} el {date} a las {time}. Motivo: {availability.Message}\n");
        return true;
    }

    var resolution = $"Disponibilidad consultada para {teacher.Value.Name} el {date} a las {time}. Ocupados: {availability.Occupied}. Capacidad: {availability.Capacity}. Disponibles: {availability.Available}.";
    if (!availability.IsAvailable)
    {
        Console.WriteLine($"\n[SubagenteDisponibilidad] {resolution} No hay cupos disponibles; requiere seguimiento o alternativa.\n");
        return true;
    }

    Console.WriteLine($"\n[SubagenteDisponibilidad] {resolution} El profesor tiene disponibilidad en ese horario. No cierro el ticket; queda pendiente de confirmacion del usuario.\n");
    return true;
}

static string? ExtractEmail(string text)
{
    var match = Regex.Match(text, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
    return match.Success ? match.Value : null;
}

static string? ExtractTicketNumber(string text)
{
    var match = Regex.Match(text, @"\bINC[\s-]*\d{4,12}\b", RegexOptions.IgnoreCase);
    return match.Success ? string.Concat(match.Value.ToUpperInvariant().Where(char.IsLetterOrDigit)) : null;
}

static (string Id, string Name)? ExtractTeacher(string text)
{
    var normalized = text.ToLowerInvariant();

    if (normalized.Contains("valentina") || normalized.Contains("ruiz"))
        return ("valentina", "Valentina Ruiz");

    if (normalized.Contains("martin") || normalized.Contains("sosa"))
        return ("martin", "Martin Sosa");

    if (normalized.Contains("camila") || normalized.Contains("herrera"))
        return ("camila", "Camila Herrera");

    return null;
}

static string? ExtractDate(string text)
{
    var iso = Regex.Match(text, @"\b20\d{2}-\d{2}-\d{2}\b");
    if (iso.Success)
        return iso.Value;

    var slash = Regex.Match(text, @"\b(\d{1,2})[/-](\d{1,2})[/-](20\d{2})\b");
    if (slash.Success)
        return $"{slash.Groups[3].Value}-{int.Parse(slash.Groups[2].Value):00}-{int.Parse(slash.Groups[1].Value):00}";

    return null;
}

static string? ExtractTime(string text)
{
    var match = Regex.Match(text, @"\b([01]?\d|2[0-3])[:.](\d{2})\b");
    if (!match.Success)
        return null;

    return $"{int.Parse(match.Groups[1].Value):00}:{match.Groups[2].Value}";
}

static async Task<ResetAccessResult> ResetTurneraAccessAsync(string email)
{
    var apiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
        return new ResetAccessResult(false, null, "Falta AGENT_API_KEY.");

    var baseUrl = Environment.GetEnvironmentVariable("TURNERA_API_URL") ?? "http://localhost:3000";
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/reset-acceso")
    {
        Content = JsonContent.Create(new { email })
    };
    request.Headers.Add("x-api-key", apiKey);

    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return new ResetAccessResult(false, null, TryReadError(body));

    var result = JsonSerializer.Deserialize<TurneraResetAccessResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    return new ResetAccessResult(result?.Ok == true, result?.TempPassword, result?.Mensaje ?? "Respuesta invalida de Turnera.");
}

static async Task<PaymentQueryResult> GetTurneraPaymentsAsync(string email)
{
    var apiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
        return new PaymentQueryResult(false, 0, 0, "Falta AGENT_API_KEY.");

    var baseUrl = Environment.GetEnvironmentVariable("TURNERA_API_URL") ?? "http://localhost:3000";
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/agent/pagos?email={Uri.EscapeDataString(email)}");
    request.Headers.Add("x-api-key", apiKey);

    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return new PaymentQueryResult(false, 0, 0, TryReadError(body));

    using var document = JsonDocument.Parse(body);
    var root = document.RootElement;
    var payments = root.TryGetProperty("pagos", out var pagos) && pagos.ValueKind == JsonValueKind.Array
        ? pagos.GetArrayLength()
        : 0;
    var credits = 0;
    if (root.TryGetProperty("credits", out var creditElement))
    {
        if (creditElement.ValueKind == JsonValueKind.Number)
        {
            creditElement.TryGetInt32(out credits);
        }
        else if (creditElement.ValueKind == JsonValueKind.Object &&
                 creditElement.TryGetProperty("availableClasses", out var availableClasses))
        {
            availableClasses.TryGetInt32(out credits);
        }
    }

    return new PaymentQueryResult(true, payments, credits, "Pagos consultados.");
}

static async Task<TurnsQueryResult> GetTurneraTurnsAsync(string email)
{
    var apiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
        return new TurnsQueryResult(false, 0, 0, "Falta AGENT_API_KEY.");

    var baseUrl = Environment.GetEnvironmentVariable("TURNERA_API_URL") ?? "http://localhost:3000";
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/agent/turnos?email={Uri.EscapeDataString(email)}");
    request.Headers.Add("x-api-key", apiKey);

    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return new TurnsQueryResult(false, 0, 0, TryReadError(body));

    using var document = JsonDocument.Parse(body);
    var root = document.RootElement;
    var bookings = root.TryGetProperty("turnos", out var turnos) && turnos.ValueKind == JsonValueKind.Array
        ? turnos.GetArrayLength()
        : 0;
    var credits = root.TryGetProperty("credits", out var creditElement) && creditElement.TryGetInt32(out var creditValue)
        ? creditValue
        : 0;

    return new TurnsQueryResult(true, bookings, credits, "Turnos consultados.");
}

static async Task<AvailabilityQueryResult> GetTurneraAvailabilityAsync(string teacherId, string date, string time)
{
    var apiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
        return new AvailabilityQueryResult(false, 0, 0, 0, false, "Falta AGENT_API_KEY.");

    var baseUrl = Environment.GetEnvironmentVariable("TURNERA_API_URL") ?? "http://localhost:3000";
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var url = $"/api/agent/disponibilidad?teacherId={Uri.EscapeDataString(teacherId)}&date={Uri.EscapeDataString(date)}&time={Uri.EscapeDataString(time)}";
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Add("x-api-key", apiKey);

    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return new AvailabilityQueryResult(false, 0, 0, 0, false, TryReadError(body));

    using var document = JsonDocument.Parse(body);
    var root = document.RootElement;
    var occupied = root.TryGetProperty("ocupados", out var occupiedElement) && occupiedElement.TryGetInt32(out var occupiedValue) ? occupiedValue : 0;
    var capacity = root.TryGetProperty("capacidad", out var capacityElement) && capacityElement.TryGetInt32(out var capacityValue) ? capacityValue : 0;
    var available = root.TryGetProperty("disponibles", out var availableElement) && availableElement.TryGetInt32(out var availableValue) ? availableValue : Math.Max(capacity - occupied, 0);
    var isAvailable = root.TryGetProperty("disponible", out var isAvailableElement) && isAvailableElement.ValueKind == JsonValueKind.True;

    return new AvailabilityQueryResult(true, occupied, capacity, available, isAvailable, "Disponibilidad consultada.");
}

static string TryReadError(string body)
{
    if (string.IsNullOrWhiteSpace(body))
        return "sin detalle";

    try
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("error", out var error)
            ? error.GetString() ?? body
            : body;
    }
    catch (JsonException)
    {
        return body;
    }
}

public sealed class AgentPromptLoader
{
    public string Load(string fileName)
    {
        var root = GetPromptRoot();
        var path = Path.Combine(root, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No existe el prompt {fileName} en {root}.", path);

        return File.ReadAllText(path, Encoding.UTF8).Trim();
    }

    private static string GetPromptRoot()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTAI_PROMPTS_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Prompts"));
    }
}

public static class AgentActionMemory
{
    private const int MaxCasesPerFolder = 30;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public sealed record Case(
        string Ticket,
        string System,
        string Agent,
        string Outcome,
        string Summary,
        string? Action,
        string? Result,
        DateTime CreatedAtUtc);

    public static void Save(
        string? ticket,
        string system,
        string agent,
        string outcome,
        string summary,
        string? result,
        string? action)
    {
        var normalizedSystem = Normalize(system);
        var normalizedOutcome = Normalize(outcome);
        var caseItem = new Case(
            Ticket: string.IsNullOrWhiteSpace(ticket) ? $"CASE-{DateTime.UtcNow:yyyyMMddHHmmss}" : ticket,
            System: normalizedSystem,
            Agent: agent,
            Outcome: normalizedOutcome,
            Summary: summary,
            Action: action,
            Result: result,
            CreatedAtUtc: DateTime.UtcNow);

        var directory = Path.Combine(GetMemoryRoot(), normalizedSystem, normalizedOutcome);
        Directory.CreateDirectory(directory);

        var fileName = $"{SanitizeFileName(caseItem.Ticket)}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        File.WriteAllText(Path.Combine(directory, fileName), JsonSerializer.Serialize(caseItem, JsonOptions));
        TrimFolder(directory);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void TrimFolder(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(MaxCasesPerFolder))
        {
            File.Delete(file);
        }
    }

    private static string GetMemoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTAI_MEMORY_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AgentMemory"));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(invalid.Contains(character) ? '_' : character);

        return builder.ToString();
    }
}

public sealed record ResetAccessResult(bool Ok, string? TempPassword, string? Message);
public sealed record PaymentQueryResult(bool Ok, int PaymentCount, int Credits, string? Message);
public sealed record TurnsQueryResult(bool Ok, int BookingCount, int Credits, string? Message);
public sealed record AvailabilityQueryResult(bool Ok, int Occupied, int Capacity, int Available, bool IsAvailable, string? Message);
public sealed record TurneraResetAccessResponse(bool Ok, string? TempPassword, string? Mensaje);


