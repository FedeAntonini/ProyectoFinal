using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;
using System.Net.Http.Json;
using System.Text.Json;
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
    .Where(t => t.Name is "consultar_estado_pedido" or "cerrar_ticket_pedido")
    .ToList();

var toolsAcceso = todasLasTools
    .Where(t => t.Name is "resetear_acceso" or "cerrar_ticket_acceso")
    .ToList();

var toolsPago = todasLasTools
    .Where(t => t.Name is "consultar_pago" or "cerrar_ticket_pago")
    .ToList();

var toolsPrecio = todasLasTools
    .Where(t => t.Name is "corregir_precio" or "cerrar_ticket_precio")
    .ToList();

var toolsStock = todasLasTools
    .Where(t => t.Name is "sincronizar_stock" or "cerrar_ticket_stock")
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

var systemPedido = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de pedidos de e-commerce.
    Tu único trabajo es consultar el estado del pedido mencionado, informar al usuario y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemAcceso = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de acceso de e-commerce.
    Tu único trabajo es resetear el acceso del usuario, informarle que recibió un link de recuperación y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemPago = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de pagos de e-commerce.
    Tu único trabajo es consultar el estado del pago del usuario, informarle el resultado y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemPrecio = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de precios del catálogo de e-commerce.
    Tu único trabajo es corregir el precio del producto indicado, confirmar la actualización y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemStock = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de stock de e-commerce.
    Tu único trabajo es sincronizar el stock del producto indicado, confirmar la sincronización y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemEscalacion = new ChatMessage(ChatRole.System, """
    Sos el agente de escalación de soporte de e-commerce.
    Tu único trabajo es escalar el ticket cuando no haya solución segura en la KB o cuando falten permisos/datos para resolver.
    Usá la tool escalar_ticket y explicá brevemente el motivo.
    No intentes resolver el caso.
    """);

Console.WriteLine("=== Agente de Acción — E-Commerce Soporte N1 ===");
Console.WriteLine("Pegá el diagnóstico del Enrutador (o 'salir' para terminar):");
Console.WriteLine();

while (true)
{
    Console.Write("Diagnóstico: ");
    var diagnostico = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(diagnostico) || diagnostico.ToLower() == "salir") break;

    try
    {
        if (await TryHandleDeterministicAccessAsync(diagnostico))
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
            Console.WriteLine("\n[AgenteAccion] No se encontró subagente. Escalando a nivel 2.\n");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Sesión terminada.");

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

    var closeResult = await CloseAgentAiTicketAsync(ticketId, resolution);
    Console.WriteLine($"\n[SubagenteAcceso] {resolution} {closeResult}\n");
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

static async Task<string> CloseAgentAiTicketAsync(string ticketId, string resolution)
{
    var baseUrl = Environment.GetEnvironmentVariable("AGENTAI_API_URL") ?? "http://localhost:5038";
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

    using var ticketResponse = await http.GetAsync($"/tickets/by-number/{Uri.EscapeDataString(ticketId)}");
    if (ticketResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        return $"No encontre el ticket {ticketId} para cerrarlo.";

    ticketResponse.EnsureSuccessStatusCode();
    var ticket = await ticketResponse.Content.ReadFromJsonAsync<AgentTicketResponse>();
    if (ticket is null)
        return $"No encontre el ticket {ticketId} para cerrarlo.";

    var request = new
    {
        description = $"{ticket.Description}\n\nResolucion: {resolution}",
        state = 4,
        stateLabel = "Resolved",
        resolvedAt = DateTime.UtcNow
    };

    using var response = await http.PutAsJsonAsync($"/tickets/{ticket.Id}", request);
    return response.IsSuccessStatusCode
        ? $"Ticket {ticketId} cerrado."
        : $"No pude cerrar el ticket {ticketId}. API respondio {(int)response.StatusCode}.";
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

public sealed record ResetAccessResult(bool Ok, string? TempPassword, string? Message);
public sealed record TurneraResetAccessResponse(bool Ok, string? TempPassword, string? Mensaje);
public sealed record AgentTicketResponse(int Id, string Number, string Description);
