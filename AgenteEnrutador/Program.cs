using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;

var API_KEY = Environment.GetEnvironmentVariable("GROQ_API_KEY")
    ?? throw new Exception("Falta la variable de entorno GROQ_API_KEY");
const string MODELO = "llama-3.1-8b-instant";

Console.WriteLine("Conectando al MCP Server...");

var transporte = new StdioClientTransport(new StdioClientTransportOptions
{
    Command = "dotnet",
    Arguments = ["run", "--project", "../McpServer"],
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
    
    Cuando recibas un ID de ticket:
    1. Llamá a la tool obtener_ticket para obtener los datos del ticket
    2. Si la tool devuelve que el ticket no fue encontrado, respondé exactamente:
       TICKET_NO_ENCONTRADO: No se encontró un ticket con ese ID.
    3. Si el ticket existe, leé el campo "Sistema" del resultado y respondé ÚNICAMENTE con una de estas líneas:
       - Si Sistema es "usuarios": DELEGAR_A: AgenteAccionAcceso
       - Si Sistema es "pedidos": DELEGAR_A: AgenteAccionPedido
       - Si Sistema es "pagos": DELEGAR_A: AgenteAccionPago
       - Si Sistema es "catalogo": DELEGAR_A: AgenteAccionPrecio
       - Si Sistema es "stock": DELEGAR_A: AgenteAccionStock
       - Si no reconocés el sistema: DELEGAR_A: Escalacion
    
    No agregues texto adicional. No incluyas tags XML ni HTML. Solo respondé con la línea indicada.
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