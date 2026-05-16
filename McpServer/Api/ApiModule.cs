using McpServer.Api.AgentRun;
using McpServer.Api.AgentStep;

namespace McpServer.Api;
public static class ApiModule
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        var baseUrl = config["Api:BaseUrl"] ?? throw new Exception("Missing Api:BaseUrl");

        services.AddHttpClient<IAgentRunService, AgentRunService>(c => c.BaseAddress = new Uri(baseUrl));
        services.AddHttpClient<IAgentStepService, AgentStepService>(c => c.BaseAddress = new Uri(baseUrl));

        return services;
    }
}