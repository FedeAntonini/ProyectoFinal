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

        if (await TryHandleDeterministicTurnoAsync(diagnostico))
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
            Console.WriteLine("\n[AgenteAccion] No se encontro subagente. Escalando a nivel 2.\n");
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
    var credits = ReadAvailableCredits(root);

    return new PaymentQueryResult(true, payments, credits, "Pagos consultados.");
}


static int ReadAvailableCredits(JsonElement root)
{
    if (!root.TryGetProperty("credits", out var creditElement))
        return 0;

    if (creditElement.ValueKind == JsonValueKind.Number &&
        creditElement.TryGetInt32(out var creditValue))
    {
        return creditValue;
    }

    if (creditElement.ValueKind == JsonValueKind.Object &&
        creditElement.TryGetProperty("availableClasses", out var availableClasses) &&
        availableClasses.TryGetInt32(out var availableValue))
    {
        return availableValue;
    }

    return 0;
}


static string FormatDate(string date)
{
    if (!DateOnly.TryParse(date, out var d))
        return date;

    var dia = d.DayOfWeek switch
    {
        DayOfWeek.Monday    => "Lunes",
        DayOfWeek.Tuesday   => "Martes",
        DayOfWeek.Wednesday => "Miércoles",
        DayOfWeek.Thursday  => "Jueves",
        DayOfWeek.Friday    => "Viernes",
        DayOfWeek.Saturday  => "Sábado",
        DayOfWeek.Sunday    => "Domingo",
        _                   => ""
    };

    return $"{dia} {d:dd/MM/yyyy}";
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

static async Task<bool> TryHandleDeterministicTurnoAsync(string diagnostico)
{
    if (!diagnostico.Contains("AgenteAccionTurno", StringComparison.OrdinalIgnoreCase) &&
        !diagnostico.Contains("consultar_turno", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var email = ExtractEmail(diagnostico);
    var ticketId = ExtractTicketNumber(diagnostico);

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(ticketId))
    {
        Console.WriteLine("\n[SubagenteTurno] Faltan datos para consultar turnos. Necesito TICKET y USUARIO/email.\n");
        return true;
    }

    Console.WriteLine("\n[AgenteAccion] Levantando subagente: TURNO");

    var turno = await GetTurneraTurnosAsync(email);
    if (!turno.Ok)
    {
        Console.WriteLine($"\n[SubagenteTurno] No pude consultar turnos para {email}. Motivo: {turno.Message}\n");
        return true;
    }

    string resolution;
    if (turno.Turnos.Count == 0)
    {
        resolution = $"Turnos consultados para {email}. No hay turnos reservados activos.";
    }
    else
    {
        var detalle = string.Join("\n", turno.Turnos.Select((t, i) =>
        {
            var fecha = FormatDate(t.Date);
            var extra = string.IsNullOrWhiteSpace(t.Teacher) ? "" : $" — {t.Teacher}" +
                        (string.IsNullOrWhiteSpace(t.Specialty) ? "" : $" ({t.Specialty})");
            return $"  {i + 1}. {fecha} a las {t.Time}{extra}";
        }));
        resolution = $"Turnos consultados para {email}. Turnos reservados: {turno.Turnos.Count}.\n{detalle}";
    }

    var closeResult = await CloseAgentAiTicketAsync(ticketId, resolution);
    Console.WriteLine($"\n[SubagenteTurno] {resolution} {closeResult}\n");
    return true;
}

static async Task<TurnoQueryResult> GetTurneraTurnosAsync(string email)
{
    var apiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
        return new TurnoQueryResult(false, [], "Falta AGENT_API_KEY.");

    var baseUrl = Environment.GetEnvironmentVariable("TURNERA_API_URL") ?? "http://localhost:3000";
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/agent/turnos?email={Uri.EscapeDataString(email)}");
    request.Headers.Add("x-api-key", apiKey);

    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return new TurnoQueryResult(false, [], TryReadError(body));

    using var document = JsonDocument.Parse(body);
    var root = document.RootElement;

    var turnos = new List<TurnoInfo>();
    if (root.TryGetProperty("turnos", out var turnosEl) && turnosEl.ValueKind == JsonValueKind.Array)
    {
        foreach (var t in turnosEl.EnumerateArray())
        {
            var date = t.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
            var time = t.TryGetProperty("time", out var ti) ? ti.GetString() ?? "" : "";
            var teacher = t.TryGetProperty("teacherName", out var tn) ? tn.GetString() ?? "" : "";
            var specialty = t.TryGetProperty("specialty", out var sp) ? sp.GetString() ?? "" : "";
            turnos.Add(new TurnoInfo(date, time, teacher, specialty));
        }
    }

    return new TurnoQueryResult(true, turnos, "Turnos consultados.");
}

static async Task<string> CloseAgentAiTicketAsync(string ticketId, string resolution)
{
    var baseUrl = Environment.GetEnvironmentVariable("AGENTAI_API_URL") ?? "http://localhost:5038";
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

    using var getResponse = await http.GetAsync($"/tickets/by-number/{Uri.EscapeDataString(ticketId.ToUpperInvariant())}");
    if (!getResponse.IsSuccessStatusCode)
        return $"No pude encontrar el ticket {ticketId}.";

    var ticket = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
    var id = ticket.GetProperty("id").GetInt32();

    var request = new { description = resolution, state = 4, stateLabel = "Resolved", resolvedAt = DateTime.UtcNow };
    using var putResponse = await http.PutAsJsonAsync($"/tickets/{id}", request);
    return putResponse.IsSuccessStatusCode
        ? $"Ticket {ticketId} cerrado exitosamente."
        : $"No pude cerrar el ticket {ticketId}.";
}

public sealed record ResetAccessResult(bool Ok, string? TempPassword, string? Message);
public sealed record PaymentQueryResult(bool Ok, int PaymentCount, int Credits, string? Message);
public sealed record TurnoQueryResult(bool Ok, List<TurnoInfo> Turnos, string? Message);
public sealed record TurnoInfo(string Date, string Time, string Teacher, string Specialty);
public sealed record TurneraResetAccessResponse(bool Ok, string? TempPassword, string? Mensaje);

public sealed class AgentPromptLoader
{
    private readonly string _promptsPath;

    public AgentPromptLoader()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTAI_PROMPTS_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            _promptsPath = configured;
            return;
        }

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Prompts")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Prompts")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Prompts")),
        };

        _promptsPath = candidates.FirstOrDefault(Directory.Exists) ?? Directory.GetCurrentDirectory();
    }

    public string Load(string fileName)
    {
        var path = Path.Combine(_promptsPath, fileName);
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "Eres un agente de soporte especializado. Resuelve el problema del usuario usando las tools disponibles.";
    }
}


