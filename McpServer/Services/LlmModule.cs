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

        services.AddSingleton<ILlmService>(_ =>
            GroqLlmService.Create(apiKey, model));

        services.AddScoped<LlmGateway>();
        return services;
    }
}