using McpServer.Api.AgentRun;
using McpServer.Api.AgentStep;
using McpServer.Api.Auth;
using McpServer.Api.Conversations;
using McpServer.Api.Messages;
using McpServer.Api.Tickets;
using McpServer.Services;

namespace McpServer.Api;

public static class ApiModule
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        var baseUrl = config["Api:BaseUrl"] ?? throw new Exception("Missing Api:BaseUrl");

        services.AddTransient<AuthHandler>();

        services.AddHttpClient<IAgentRunService, AgentRunService>(c => c.BaseAddress = new Uri(baseUrl))
                .AddHttpMessageHandler<AuthHandler>();

        services.AddHttpClient<IAgentStepService, AgentStepService>(c => c.BaseAddress = new Uri(baseUrl))
                .AddHttpMessageHandler<AuthHandler>();

        services.AddHttpClient<ITicketService, TicketService>(c => c.BaseAddress = new Uri(baseUrl))
                .AddHttpMessageHandler<AuthHandler>();

        services.AddHttpClient<IConversationService, ConversationService>(c => c.BaseAddress = new Uri(baseUrl))
                .AddHttpMessageHandler<AuthHandler>();

        services.AddHttpClient<IMessageService, MessageService>(c => c.BaseAddress = new Uri(baseUrl))
                .AddHttpMessageHandler<AuthHandler>();

        services.AddHttpClient<IAuthService, AuthService>(c => c.BaseAddress = new Uri(baseUrl));

        services.AddHttpClient<IKnowledgeBaseApiService, KnowledgeBaseApiService>(c => c.BaseAddress = new Uri(baseUrl));

        return services;
    }
}