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

// Solo la tool de registrar incidente
var toolsEntrada = todasLasTools
    .Where(t => t.Name is "registrar_incidente")
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
    Sos el Agente de Entrada del sistema de soporte de una turnera de pilates.
    Tu trabajo es hablar con el socio, recolectar información del problema y registrarlo.

    Para poder registrar el incidente necesitás obtener obligatoriamente:
    1. Descripción clara del problema
    2. Sistema afectado (socios, turnos, pagos, clases, instructores)
    3. Tipo de error (dato_incorrecto, operacion_bloqueada, inconsistencia, error_sistema)
    4. Email del socio

    Reglas:
    - Hablá en español, de forma amigable y clara.
    - Si falta información, preguntá puntualmente. Una pregunta a la vez.
    - Cuando tengas todos los datos, usá la tool registrar_incidente para crear el ticket.
    - Una vez que uses la tool registrar_incidente, respondé al socio con un mensaje claro como: "Tu consulta fue registrada con el ID: INC1234. Un agente la procesará a la brevedad."
    - No ejecutes acciones correctivas. Solo recolectá datos y registrá el incidente.
    """);

var historial = new List<ChatMessage> { systemPrompt };

Console.WriteLine("=== Agente de Entrada — Turnera de Pilates Soporte N1 ===");
Console.WriteLine("Hola! Soy el agente de soporte de la turnera. ¿En qué te puedo ayudar?");
Console.WriteLine();

while (true)
{
    Console.Write("Usuario: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "salir") break;

    historial.Add(new(ChatRole.User, input));

    try
    {
        var respuesta = await agente.GetResponseAsync(
            historial,
            new ChatOptions { Tools = [.. toolsEntrada] }
        );

        var texto = respuesta.Text ?? "";
        Console.WriteLine($"\n[Agente] {texto}\n");
        historial.Add(new(ChatRole.Assistant, texto));

        // Si el agente registró el incidente, terminamos la sesión
        if (texto.Contains("INC", StringComparison.OrdinalIgnoreCase) &&
            texto.Contains("ticket", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[Sistema] Incidente registrado. Derivando al Agente Enrutador...");
            break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Sesión del Agente de Entrada finalizada.");