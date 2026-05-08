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

// Filtrar tools por subagente
var toolsPedido  = todasLasTools.Where(t => t.Name is "consultar_estado_pedido" or "cerrar_ticket_pedido").ToList();
var toolsAcceso  = todasLasTools.Where(t => t.Name is "resetear_acceso" or "cerrar_ticket_acceso").ToList();
var toolsPago    = todasLasTools.Where(t => t.Name is "consultar_pago" or "cerrar_ticket_pago").ToList();
var toolsPrecio  = todasLasTools.Where(t => t.Name is "corregir_precio" or "cerrar_ticket_precio").ToList();
var toolsStock   = todasLasTools.Where(t => t.Name is "sincronizar_stock" or "cerrar_ticket_stock").ToList();

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

// System prompts de cada subagente
var systemPedido = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de pedidos de e-commerce.
    Tu único trabajo es consultar el estado del pedido mencionado, informar al usuario y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemAcceso = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de acceso de usuarios.
    Tu único trabajo es resetear el acceso del usuario afectado y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemPago = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de pagos rechazados.
    Tu único trabajo es consultar el estado del pago, informar al usuario y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemPrecio = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de precios incorrectos en el catálogo.
    Tu único trabajo es corregir el precio del producto afectado y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemStock = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de sincronización de stock.
    Tu único trabajo es sincronizar el stock del producto afectado y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
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
        ChatMessage system;
        List<McpClientTool> tools;
        string nombreSubagente;

        if (diagnostico.Contains("AgenteAccionPedido", StringComparison.OrdinalIgnoreCase) ||
            diagnostico.Contains("pedido", StringComparison.OrdinalIgnoreCase))
        {
            system = systemPedido; tools = toolsPedido; nombreSubagente = "PEDIDO";
        }
        else if (diagnostico.Contains("AgenteAccionAcceso", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("acceso", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("sesión", StringComparison.OrdinalIgnoreCase))
        {
            system = systemAcceso; tools = toolsAcceso; nombreSubagente = "ACCESO";
        }
        else if (diagnostico.Contains("AgenteAccionPago", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("pago", StringComparison.OrdinalIgnoreCase))
        {
            system = systemPago; tools = toolsPago; nombreSubagente = "PAGO";
        }
        else if (diagnostico.Contains("AgenteAccionPrecio", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("precio", StringComparison.OrdinalIgnoreCase))
        {
            system = systemPrecio; tools = toolsPrecio; nombreSubagente = "PRECIO";
        }
        else if (diagnostico.Contains("AgenteAccionStock", StringComparison.OrdinalIgnoreCase) ||
                 diagnostico.Contains("stock", StringComparison.OrdinalIgnoreCase))
        {
            system = systemStock; tools = toolsStock; nombreSubagente = "STOCK";
        }
        else
        {
            Console.WriteLine("\n[AgenteAccion] No se encontró subagente. Escalando a nivel 2.\n");
            continue;
        }

        Console.WriteLine($"\n[AgenteAccion] Levantando subagente: {nombreSubagente}");
        var respuesta = await CrearSubagente().GetResponseAsync(
            [system, new(ChatRole.User, diagnostico)],
            new ChatOptions { Tools = [.. tools] }
        );
        Console.WriteLine($"\n[Subagente{nombreSubagente}] {respuesta.Text}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Sesión terminada.");
