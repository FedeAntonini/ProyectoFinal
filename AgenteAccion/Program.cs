using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;

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

IChatClient CrearSubagente() =>
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

var systemSocio = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de acceso de socios de la turnera de pilates.
    Tu único trabajo es resetear el acceso del socio mencionado en el diagnóstico, informarle que recibió un link de recuperación y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemTurno = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de turnos de la turnera de pilates.
    Tu único trabajo es consultar y reprogramar el turno del socio mencionado en el diagnóstico, informar el resultado y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemPago = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de pagos de la turnera de pilates.
    Tu único trabajo es verificar el cobro del socio mencionado en el diagnóstico, informar el resultado y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemClase = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de clases de la turnera de pilates.
    Tu único trabajo es habilitar la clase que figura incorrectamente como no disponible, confirmar la habilitación y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemInstructor = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de instructores de la turnera de pilates.
    Tu único trabajo es asignar un instructor disponible a la clase indicada en el diagnóstico, confirmar la asignación y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

Console.WriteLine("=== Agente de Acción — Turnera de Pilates Soporte N1 ===");
Console.WriteLine("Pegá el diagnóstico del Enrutador (o 'salir' para terminar):");
Console.WriteLine();

while (true)
{
    Console.Write("Diagnóstico: ");
    var diagnostico = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(diagnostico) || diagnostico.ToLower() == "salir") break;

    try
    {
        if (diagnostico.Contains("AgenteAccionSocio", StringComparison.OrdinalIgnoreCase) ||
            diagnostico.Contains("socios", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: SOCIO");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemSocio, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsSocio] }
            );
            Console.WriteLine($"\n[SubagenteSocio] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionTurno", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("turnos", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: TURNO");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemTurno, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsTurno] }
            );
            Console.WriteLine($"\n[SubagenteTurno] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionPago", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("pagos", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: PAGO");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemPago, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsPago] }
            );
            Console.WriteLine($"\n[SubagentePago] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionClase", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("clases", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: CLASE");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemClase, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsClase] }
            );
            Console.WriteLine($"\n[SubagenteClase] {respuesta.Text}\n");
        }
        else if (diagnostico.Contains("AgenteAccionInstructor", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("instructores", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n[AgenteAccion] Levantando subagente: INSTRUCTOR");
            var respuesta = await CrearSubagente().GetResponseAsync(
                [systemInstructor, new(ChatRole.User, diagnostico)],
                new ChatOptions { Tools = [.. toolsInstructor] }
            );
            Console.WriteLine($"\n[SubagenteInstructor] {respuesta.Text}\n");
        }
        else
        {
            Console.WriteLine("\n[AgenteAccion] No se encontró subagente para el sistema indicado. Escalando a nivel 2.\n");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Sesión terminada.");
