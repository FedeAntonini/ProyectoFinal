using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace McpServer.Services;

public static class LlmModule
{
    public static IServiceCollection AddLlmServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        var apiKey = config["Groq:ApiKey"] ?? throw new Exception("Missing Groq:ApiKey");
        var model = config["Groq:Modelo"] ?? throw new Exception("Missing Groq:Modelo");

        var systemPrompt = """
    Sos el Agente Enrutador de soporte de e-commerce nivel 1.

    Cuando recibas un ID de ticket:
    1. Llamá a la tool obtener_ticket para obtener los datos
    2. Si el ticket no existe, respondé: TICKET_NO_ENCONTRADO: No se encontró un ticket con ese ID.
    3. Si el ticket existe, analizá el problema y el sistema afectado y decidí cuál de estos agentes es el más adecuado para resolverlo:
       - AgenteAccionAcceso: problemas de login, autenticación o acceso de usuarios
       - AgenteAccionPedido: problemas con el estado o seguimiento de pedidos
       - AgenteAccionPago: problemas con pagos, cobros o transacciones
       - AgenteAccionPrecio: problemas con precios incorrectos en el catálogo
       - AgenteAccionStock: problemas con inventario o sincronización de stock
       - Escalacion: si el problema no encaja en ninguno de los anteriores
    4. Respondé ÚNICAMENTE con: DELEGAR_A: [nombre del agente elegido]

    No incluyas tags XML ni HTML. No agregues texto adicional.
    """;

        services.AddSingleton<ILlmService>(_ =>
            GroqLlmService.Create(
                apiKey,
                model,
                systemPrompt
            ));

        services.AddScoped<LlmGateway>();

        return services;
    }
}