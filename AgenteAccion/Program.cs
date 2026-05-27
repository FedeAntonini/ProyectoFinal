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

var systemPedido = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de pedidos de la turnera de pilates.
    Tu único trabajo es consultar el estado del pedido mencionado, informar al usuario y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemAcceso = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de acceso de la turnera de pilates.
    Tu único trabajo es resetear el acceso del usuario, informarle que recibió un link de recuperación y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemPago = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de pagos de la turnera de pilates.
    Tu único trabajo es consultar el estado del pago del usuario, informarle el resultado y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemPrecio = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de precios del catálogo de la turnera de pilates.
    Tu único trabajo es corregir el precio del producto indicado, confirmar la actualización y cerrar el ticket.
    No diagnostiques. Solo ejecutá.
    """);

var systemStock = new ChatMessage(ChatRole.System, """
    Sos el Subagente especializado en problemas de stock de la turnera de pilates.
    Tu único trabajo es sincronizar el stock del producto indicado, confirmar la sincronización y cerrar el ticket.
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
