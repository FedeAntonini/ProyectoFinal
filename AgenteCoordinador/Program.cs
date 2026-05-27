using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;
using System.Text.RegularExpressions;

var API_KEY = Environment.GetEnvironmentVariable("GROQ_API_KEY")
    ?? throw new Exception("Falta la variable de entorno GROQ_API_KEY");
const string MODELO = "llama-3.3-70b-versatile";

var mcpUrl = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:61559";

Console.WriteLine($"Conectando al MCP Server en {mcpUrl}...");

var transporte = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri($"{mcpUrl}/mcp"),
    Name = "SoporteMcpServer",
});

await using var mcp = await McpClient.CreateAsync(transporte);
var todasLasTools = await mcp.ListToolsAsync();

// ── Tools por fase ──────────────────────────────────────────────────────────
var toolsEnrutador = todasLasTools
    .Where(t => t.Name is "obtener_ticket" or "diagnosticar_problema")
    .ToList();

var toolsSocio = todasLasTools
    .Where(t => t.Name is "resetear_acceso_socio" or "cerrar_ticket_socio")
    .ToList();

var toolsTurno = todasLasTools
    .Where(t => t.Name is "consultar_turno" or "cerrar_ticket_turno")
    .ToList();

var toolsPago = todasLasTools
    .Where(t => t.Name is "consultar_cuota" or "cerrar_ticket_pago")
    .ToList();

var toolsClase = todasLasTools
    .Where(t => t.Name is "habilitar_clase" or "cerrar_ticket_clase")
    .ToList();

var toolsInstructor = todasLasTools
    .Where(t => t.Name is "asignar_instructor" or "cerrar_ticket_instructor")
    .ToList();

// ── Fábrica de agentes ──────────────────────────────────────────────────────
IChatClient CrearAgente() =>
    new OpenAIClient(
        new ApiKeyCredential(API_KEY),
        new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1/") })
    .GetChatClient(MODELO)
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

// ── System prompts ──────────────────────────────────────────────────────────
var systemEnrutador = new ChatMessage(ChatRole.System, """
    Sos el Agente Enrutador de soporte de la turnera de pilates nivel 1.

    Cuando recibas un ID de ticket seguí estos pasos en orden:
    1. Llamá a la tool obtener_ticket para obtener los datos del ticket.
    2. Si el ticket no existe, respondé: TICKET_NO_ENCONTRADO
    3. Si el ticket existe, llamá a la tool diagnosticar_problema pasando el sistema y la descripción del problema.
    4. Según el resultado del diagnóstico:
       - Si "decision" es "escalar" → DELEGAR_A: Escalacion
       - Si "decision" es "continuar" o "pedir_mas_info":
           * socios       → DELEGAR_A: AgenteAccionSocio
           * turnos       → DELEGAR_A: AgenteAccionTurno
           * pagos        → DELEGAR_A: AgenteAccionPago
           * clases       → DELEGAR_A: AgenteAccionClase
           * instructores → DELEGAR_A: AgenteAccionInstructor
           * otro         → DELEGAR_A: Escalacion
    5. Respondé ÚNICAMENTE con este formato (sin texto adicional):
       DELEGAR_A: [nombre del agente]
       DECISION_KB: [decision]
       CONFIANZA_KB: [confianza]
       MENSAJE_SUGERIDO: [mensajeSugerido]
    """);

var systemSocio = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de acceso de socios de la turnera de pilates.
    El mensaje del usuario incluye el ID del ticket y el email del socio.
    Seguí estos pasos en orden:
    1. Llamá a resetear_acceso_socio con el email del socio.
    2. Llamá a cerrar_ticket_socio con el ID del ticket y una descripción de la resolución.
    No diagnostiques. Solo ejecutá.
    """);

var systemTurno = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de turnos de la turnera de pilates.
    El mensaje del usuario incluye el ID del ticket y el email del socio.
    Seguí estos pasos en orden:
    1. Llamá a consultar_turno con el email del socio.
    2. Llamá a cerrar_ticket_turno con el ID del ticket y una descripción de la resolución.
    No diagnostiques. Solo ejecutá.
    """);

var systemPago = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de pagos de la turnera de pilates.
    El mensaje del usuario incluye el ID del ticket y el email del socio.
    Seguí estos pasos en orden:
    1. Llamá a consultar_cuota con el email del socio.
    2. Llamá a cerrar_ticket_pago con el ID del ticket y una descripción de la resolución.
    No diagnostiques. Solo ejecutá.
    """);

var systemClase = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de clases de la turnera de pilates.
    El mensaje del usuario incluye el ID del ticket y la descripción del problema.
    Seguí estos pasos en orden:
    1. Llamá a habilitar_clase con la descripción de la clase afectada.
    2. Llamá a cerrar_ticket_clase con el ID del ticket y una descripción de la resolución.
    No diagnostiques. Solo ejecutá.
    """);

var systemInstructor = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de instructores de la turnera de pilates.
    El mensaje del usuario incluye el ID del ticket y la descripción del problema.
    Seguí estos pasos en orden:
    1. Llamá a asignar_instructor con la descripción de la clase afectada.
    2. Llamá a cerrar_ticket_instructor con el ID del ticket y una descripción de la resolución.
    No diagnostiques. Solo ejecutá.
    """);

// ── Loop principal ──────────────────────────────────────────────────────────
Console.WriteLine("=== Agente Coordinador — Turnera de Pilates Soporte N1 ===");
Console.WriteLine("Ingresá el ID del ticket a procesar (o 'salir' para terminar):");
Console.WriteLine();

while (true)
{
    Console.Write("Ticket ID: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "salir") break;

    var match = Regex.Match(input.Trim(), @"INC\d{4}", RegexOptions.IgnoreCase);
    if (!match.Success)
    {
        Console.WriteLine("\n[Coordinador] Input inválido. Ingresá un ID de ticket válido (ejemplo: INC0001).\n");
        continue;
    }

    var ticketId = match.Value.ToUpper();

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        // ── FASE 1: AgenteEnrutador ─────────────────────────────────────────
        Console.WriteLine($"\n[Coordinador] ▶ Fase 1 — AgenteEnrutador procesando {ticketId}...");

        var respuestaEnrutador = await CrearAgente().GetResponseAsync(
            [systemEnrutador, new(ChatRole.User, ticketId)],
            new ChatOptions { Tools = [.. toolsEnrutador] },
            cts.Token
        );

        var routing = respuestaEnrutador.Text ?? "";

        if (routing.Contains("TICKET_NO_ENCONTRADO", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"\n[Coordinador] No se encontró el ticket {ticketId}.\n");
            continue;
        }

        Console.WriteLine($"\n[Enrutador]\n{routing}\n");

        if (routing.Contains("Escalacion", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[Coordinador] Ticket escalado a nivel 2. No se ejecuta acción automática.\n");
            continue;
        }

        // ── FASE 2: AgenteAccion ────────────────────────────────────────────
        Console.WriteLine("[Coordinador] ▶ Fase 2 — AgenteAccion seleccionando subagente...");

        ChatMessage systemAccion;
        IReadOnlyList<McpClientTool> toolsAccion;
        string nombreSubagente;

        if (routing.Contains("AgenteAccionSocio", StringComparison.OrdinalIgnoreCase))
        {
            nombreSubagente = "SOCIO";
            systemAccion = systemSocio;
            toolsAccion = toolsSocio;
        }
        else if (routing.Contains("AgenteAccionTurno", StringComparison.OrdinalIgnoreCase))
        {
            nombreSubagente = "TURNO";
            systemAccion = systemTurno;
            toolsAccion = toolsTurno;
        }
        else if (routing.Contains("AgenteAccionPago", StringComparison.OrdinalIgnoreCase))
        {
            nombreSubagente = "PAGO";
            systemAccion = systemPago;
            toolsAccion = toolsPago;
        }
        else if (routing.Contains("AgenteAccionClase", StringComparison.OrdinalIgnoreCase))
        {
            nombreSubagente = "CLASE";
            systemAccion = systemClase;
            toolsAccion = toolsClase;
        }
        else if (routing.Contains("AgenteAccionInstructor", StringComparison.OrdinalIgnoreCase))
        {
            nombreSubagente = "INSTRUCTOR";
            systemAccion = systemInstructor;
            toolsAccion = toolsInstructor;
        }
        else
        {
            Console.WriteLine("[Coordinador] No se pudo determinar el subagente. Escalando a nivel 2.\n");
            continue;
        }

        Console.WriteLine($"[Coordinador] Levantando subagente: {nombreSubagente}");

        // El Coordinador obtiene los datos del ticket directamente (sin LLM)
        var ticketResult = await mcp.CallToolAsync("obtener_ticket",
            new Dictionary<string, object?> { ["ticketId"] = ticketId });
        var ticketData = System.Text.Json.JsonSerializer.Serialize(ticketResult.Content);

        var respuestaAccion = await CrearAgente().GetResponseAsync(
            [systemAccion, new(ChatRole.User, $"Ticket: {ticketId}\nDatos del ticket: {ticketData}\n{routing}")],
            new ChatOptions { Tools = [.. toolsAccion] },
            cts.Token
        );

        Console.WriteLine($"\n[Subagente{nombreSubagente}]\n{respuestaAccion.Text}\n");
        Console.WriteLine("[Coordinador] ✔ Ticket procesado correctamente.\n");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("[Error] Timeout: el agente tardó más de 90 segundos. Reiniciá el McpServer y volvé a intentar.\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.GetType().Name}: {ex.Message}\n");
    }
}

Console.WriteLine("Sesión terminada.");
