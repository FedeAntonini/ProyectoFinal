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
    .Where(t => t.Name is "obtener_ticket" or "diagnosticar_problema" or "buscar_kb")
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
    1. Llamá a la tool obtener_ticket para obtener los datos
    2. Si el ticket no existe, respondé: TICKET_NO_ENCONTRADO: No se encontró un ticket con ese ID.
    3. Si el ticket existe, analizá el problema y el sistema afectado.
    4. Llamá a buscar_kb usando la descripción y el sistema del ticket.
    5. Si la KB devuelve una solución con confianza alta o media, decidí cuál agente debe ejecutarla:
       - AgenteAccionAcceso: problemas de login, autenticación o acceso de usuarios
       - AgenteAccionPedido: problemas con el estado o seguimiento de pedidos
       - AgenteAccionPago: problemas con pagos, cobros o transacciones
       - AgenteAccionPrecio: problemas con precios incorrectos en el catálogo
       - AgenteAccionStock: problemas con inventario o sincronización de stock
    6. Si la KB no tiene solución aplicable, respondé con Escalacion.
    7. Respondé ÚNICAMENTE con uno de estos formatos:
       DELEGAR_A: [nombre del agente elegido] | TICKET: [INC1234] | KB: [id de artículo] | ACCION: [acción recomendada]
       DELEGAR_A: Escalacion | TICKET: [INC1234] | MOTIVO: [por qué no se puede resolver con KB]
    
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
