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

var tools = todasLasTools
    .Where(t => t.Name is
        "obtener_ticket" or
        "diagnosticar_problema" or
        "resetear_acceso_socio" or "cerrar_ticket_socio" or
        "consultar_turno" or "cerrar_ticket_turno" or
        "consultar_cuota" or "cerrar_ticket_pago" or
        "habilitar_clase" or "cerrar_ticket_clase" or
        "asignar_instructor" or "cerrar_ticket_instructor")
    .ToList();

IChatClient agente = new OpenAIClient(
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

var systemPrompt = new ChatMessage(ChatRole.System, """
    Sos el Agente Orquestador de soporte de la turnera de pilates nivel 1.
    Tu trabajo es resolver tickets de punta a punta siguiendo estos pasos en orden:

    1. Llamá a obtener_ticket para obtener los datos del ticket.
    2. Si el ticket no existe, respondé solo: TICKET_NO_ENCONTRADO.
    3. Llamá a diagnosticar_problema pasando el sistema y la descripcion del ticket.
    4. Según el campo "decision" del diagnóstico:
       - Si es "escalar": no ejecutes ninguna acción, solo informá que se escala a nivel 2.
       - Si es "continuar" o "pedir_mas_info": ejecutá la acción según el sistema afectado:
           * socios       → llamá a resetear_acceso_socio con el email del ticket, luego cerrar_ticket_socio
           * turnos       → llamá a consultar_turno con el email del ticket, luego cerrar_ticket_turno
           * pagos        → llamá a consultar_cuota con el email del ticket, luego cerrar_ticket_pago
           * clases       → llamá a habilitar_clase con la descripción de la clase del problema, luego cerrar_ticket_clase
           * instructores → llamá a asignar_instructor con la descripción de la clase del problema, luego cerrar_ticket_instructor
    5. Respondé ÚNICAMENTE con este resumen final:
       TICKET: [id del ticket]
       SISTEMA: [sistema afectado]
       ACCION: [descripcion breve de lo que se ejecutó]
       RESULTADO: [resultado de la acción]
       ESTADO: [Resuelto / Escalado]

    No incluyas texto adicional fuera del resumen.
    """);

Console.WriteLine("=== Agente Orquestador — Turnera de Pilates Soporte N1 ===");
Console.WriteLine("Ingresá el ID del ticket a procesar (o 'salir' para terminar):");
Console.WriteLine();

while (true)
{
    Console.Write("Input: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "salir") break;

    var match = System.Text.RegularExpressions.Regex.Match(
        input.Trim(),
        @"INC\d{4}",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    if (!match.Success)
    {
        Console.WriteLine("\n[Orquestador] Input inválido. Ingresá un ID de ticket válido (ejemplo: INC0001).\n");
        continue;
    }

    var ticketId = match.Value.ToUpper();

    try
    {
        var respuesta = await agente.GetResponseAsync(
            [systemPrompt, new(ChatRole.User, ticketId)],
            new ChatOptions { Tools = [.. tools] }
        );

        var texto = respuesta.Text ?? "";

        if (texto.Contains("TICKET_NO_ENCONTRADO", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"\n[Orquestador] No se encontró un ticket con ese ID.\n");
        else
            Console.WriteLine($"\n[Orquestador]\n{texto}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Sesión terminada.");
