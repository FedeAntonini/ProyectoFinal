using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;

var API_KEY = Environment.GetEnvironmentVariable("GROQ_API_KEY")
    ?? throw new Exception("Falta la variable de entorno GROQ_API_KEY");
const string MODELO = "llama-3.3-70b-versatile";

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
    Sos el Agente Enrutador de un sistema de soporte de e-commerce nivel 1.
    Tu único trabajo es:
    1. Obtener los datos del ticket
    2. Diagnosticar qué tipo de problema es según el sistema afectado
       (usuarios, pedidos, pagos, catalogo, stock)
    3. Indicar claramente qué agente debe resolverlo

    NO ejecutes ninguna acción correctiva. Solo diagnosticá y derivá.
    Al final de tu respuesta siempre incluí esta línea exacta:
    DELEGAR_A: [AgenteAccionPedido|AgenteAccionPago|AgenteAccionAcceso|AgenteAccionPrecio|AgenteAccionStock|Escalacion]
    """);

Console.WriteLine("=== Agente Enrutador — E-Commerce Soporte N1 ===");
Console.WriteLine("Ingresá el ID del ticket a procesar (o 'salir' para terminar):");
Console.WriteLine();

while (true)
{
    Console.Write("Input: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "salir") break;

    try
    {
        var respuesta = await agente.GetResponseAsync(
            [systemPrompt, new(ChatRole.User, input)],
            new ChatOptions { Tools = [.. toolsEnrutador] }
        );

        Console.WriteLine($"\n[Enrutador] {respuesta.Text}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Sesión terminada.");
