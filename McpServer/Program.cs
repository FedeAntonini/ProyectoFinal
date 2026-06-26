using McpServer.MessageQueue;
using McpServer.Agentes;
using McpServer.Services;
using McpServer.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

builder.Services.AddScoped<AgenteEntrada>();
builder.Services.AddScoped<AgenteConversacion>();
builder.Services.AddScoped<AgenteEnrutador>();
builder.Services.AddScoped<AgenteAccion>();
builder.Services.AddLlmServices(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddKeyedSingleton<IMessageQueue>("outbound", (sp, _) => new NullMessageQueue());
    builder.Services.AddScoped<McpServer.MessageQueue.OutboundQueueService>();
}
else
{
    builder.Services.AddMessageQueues(builder.Configuration);
    builder.Services.AddHostedService<QueueWorker>();
}

var app = builder.Build();

app.MapMcp("/mcp");

app.MapGet("/debug/tools", () =>
{
    var assembly = typeof(Program).Assembly;
    var tools = assembly
        .GetTypes()
        .Where(t => t.GetCustomAttributes(typeof(McpServerToolTypeAttribute), true).Any())
        .Select(t => new
        {
            ToolType = t.FullName,
            Methods = t.GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), true).Any())
                .Select(m => new
                {
                    Method = m.Name,
                    ReturnType = m.ReturnType.FullName
                })
        });
    return Results.Json(tools);
});

app.MapPost("/debug/ejecutar-flujo/{ticketId:int}", async (
    int ticketId,
    AgenteEnrutador enrutador,
    CancellationToken ct) =>
{
    var message = new InboundMessage(
        TicketId: ticketId.ToString(),
        CorrelationId: ticketId.ToString(),
        CustomerId: "5919549290",
        Action: InboundAction.TicketParaEnrutar,
        Payload: null);

    await enrutador.ProcessAsync(message, ct);

    return Results.Ok(new { Mensaje = $"Enrutador ejecutado para ticket {ticketId}. Revisar AffectedSystem en la BD." });
});

app.MapPost("/debug/probar-accion/{ticketId:int}/{agente}", async (
    int ticketId,
    string agente,
    AgenteAccion accion,
    CancellationToken ct) =>
{
    var decision = new EnrutadorResult(ticketId, agente, "Prueba manual sin pasar por el enrutador real");

    var message = new InboundMessage(
        TicketId: ticketId.ToString(),
        CorrelationId: Guid.NewGuid().ToString(),
        CustomerId: string.Empty,
        Action: InboundAction.TicketParaEjecutar,
        Payload: JsonSerializer.Serialize(decision));

    var resultado = await accion.ProcessAsync(message, ct);

    return Results.Ok(resultado);
});

await app.RunAsync();

public class NullMessageQueue : IMessageQueue
{
    public Task<IReadOnlyList<QueueMessage>> ReceiveMessagesAsync(int maxMessages, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<QueueMessage>>(new List<QueueMessage>());

    public Task SendMessageAsync(string body, CancellationToken ct)
        => Task.CompletedTask;

    public Task DeleteMessageAsync(string receiptHandle, CancellationToken ct)
        => Task.CompletedTask;
}