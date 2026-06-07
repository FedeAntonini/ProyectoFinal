using Amazon.SQS;
using McpServer.Agentes;

namespace McpServer.MessageQueue;

public static class QueueModule
{
    public static IServiceCollection AddMessageQueues(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddAWSService<IAmazonSQS>();

        var inboundUrl = config["SQS:InboundQueueUrl"]!;
        var outboundUrl = config["SQS:OutboundQueueUrl"]!;

        services.AddKeyedSingleton<IMessageQueue>("inbound", (sp, _) =>
            new SqsMessageQueue(sp.GetRequiredService<IAmazonSQS>(), inboundUrl));

        services.AddKeyedSingleton<IMessageQueue>("outbound", (sp, _) =>
            new SqsMessageQueue(sp.GetRequiredService<IAmazonSQS>(), outboundUrl));

        services.AddScoped<MessageDispatcher>();
        services.AddScoped<OutboundQueueService>();

        services.AddHostedService<QueueWorker>();

        return services;
    }
}