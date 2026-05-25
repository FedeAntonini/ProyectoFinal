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

var toolsEnrutador = todasLasTools
    .Where(t => t.Name is "obtener_ticket" or "diagnosticar_problema")
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
    Sos el Agente Enrutador de soporte de e-commerce nivel 1.

    Cuando recibas un ID de ticket seguí estos pasos en orden:
    1. Llamá a la tool obtener_ticket para obtener los datos del ticket.
    2. Si el ticket no existe, respondé: TICKET_NO_ENCONTRADO: No se encontró un ticket con ese ID.
    3. Si el ticket existe, llamá a la tool diagnosticar_problema pasando el sistema y la descripción del problema.
    4. Usá el resultado del diagnóstico para decidir a qué agente derivar:
       - Si el campo "decision" es "escalar" → elegí Escalacion.
       - Si el campo "decision" es "continuar" o "pedir_mas_info", elegí el agente según el sistema afectado:
           * usuarios → AgenteAccionAcceso
           * pedidos  → AgenteAccionPedido
           * pagos    → AgenteAccionPago
           * catalogo o precio → AgenteAccionPrecio
           * stock    → AgenteAccionStock
           * cualquier otro → Escalacion
    5. Respondé ÚNICAMENTE con este formato:
       DELEGAR_A: [nombre del agente]
       DECISION_KB: [valor del campo decision del diagnóstico]
       CONFIANZA_KB: [valor del campo confianza del diagnóstico]
       MENSAJE_SUGERIDO: [valor del campo mensajeSugerido del diagnóstico]

    No incluyas tags XML ni HTML. No agregues texto adicional.
    """);

Console.WriteLine("=== Agente Enrutador — E-Commerce Soporte N1 ===");
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
        Console.WriteLine("\n[Enrutador] Input inválido. Ingresá un ID de ticket válido (ejemplo: INC0001).\n");
        continue;
    }

    var ticketId = match.Value.ToUpper();

    try
    {
        var respuesta = await agente.GetResponseAsync(
            [systemPrompt, new(ChatRole.User, ticketId)],
            new ChatOptions { Tools = [.. toolsEnrutador] }
        );

        var texto = respuesta.Text ?? "";

        if (texto.Contains("TICKET_NO_ENCONTRADO", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"\n[Enrutador] No se encontró un ticket con ese ID.\n");
        else
            Console.WriteLine($"\n[Enrutador] {texto}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Sesión terminada.");