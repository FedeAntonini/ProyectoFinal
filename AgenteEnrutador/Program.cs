using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;

var API_KEY = Environment.GetEnvironmentVariable("GROQ_API_KEY")
    ?? throw new Exception("Falta la variable de entorno GROQ_API_KEY");
const string MODELO = "llama-3.3-70b-versatile";
//const string MODELO = "llama-3.1-8b-instant";

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
    Sos el Agente Enrutador de soporte de e-commerce.
    
    Cuando recibas un mensaje con un ID de ticket:
    - Llamá a obtener_ticket con ese ID
    - Llamá a diagnosticar_problema con la descripción y sistema del ticket
    - Respondé con: DELEGAR_A: [nombre del agente]
    
    Cuando recibas un mensaje sin ID de ticket válido:
    - Respondé: "Input inválido. Ingresá un ID de ticket válido (ejemplo: INC0001)."
    """);

Console.WriteLine("=== Agente Enrutador — E-Commerce Soporte N1 ===");
Console.WriteLine("Ingresá el ID del ticket a procesar (o 'salir' para terminar):");
Console.WriteLine();

while (true)
{
    Console.Write("Input: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "salir") break;

    // Validar formato del ticket antes de mandar al modelo
    if (!System.Text.RegularExpressions.Regex.IsMatch(input.Trim(), @"INC\d{4}", System.Text.RegularExpressions.RegexOptions.IgnoreCase))

{
    Console.WriteLine("\n[Enrutador] Input inválido. Ingresá un ID de ticket válido (ejemplo: INC0001).\n");
    continue;
}
    
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
