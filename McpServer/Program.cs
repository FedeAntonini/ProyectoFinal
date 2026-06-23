using McpServer.MessageQueue;
using McpServer.Agentes;
using McpServer.Services;
using McpServer.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
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
    await enrutador.ProcessAsync(new InboundMessage(
        TicketId: ticketId.ToString(),
        CorrelationId: Guid.NewGuid().ToString(),
        CustomerId: string.Empty,
        Action: InboundAction.TicketParaEnrutar,
        Payload: null), ct);

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

app.Run();

// ============================================================
// TOOLS DEL AGENTE ENTRADA
// ============================================================
[McpServerToolType]
public static class ToolsEntrada
{
    [McpServerTool, Description("Registra los datos del incidente cuando el agente tiene toda la información necesaria para derivar.")]
    public static async Task<string> RegistrarIncidente(
        [Description("Descripción clara del problema reportado por el usuario")]
        string descripcion,
        [Description("Sistema afectado: usuarios, pedidos, pagos, catalogo, stock")]
        string sistema,
        [Description("Tipo de error: dato_incorrecto, operacion_bloqueada, inconsistencia, error_sistema")]
        string tipoError,
        [Description("Email del usuario que reporta el incidente")]
        string usuario)
    {
        var ticket = await AgentAiApi.CreateTicketFromAgentAsync(new CreateAgentTicketRequest(
            descripcion,
            sistema,
            tipoError,
            usuario));

        return JsonSerializer.Serialize(new
        {
            ticketId = ticket.Number,
            id = ticket.Id,
            descripcion = ticket.Description,
            sistema,
            tipoError,
            usuario = ticket.CreatedByEmail,
            estado = ticket.StateLabel
        }, AgentAiApi.JsonOptions);
    }
}


// ============================================================
// TOOLS DEL AGENTE ENRUTADOR
// ============================================================
[McpServerToolType]
public static class ToolsEnrutador
{
    [McpServerTool, Description("Obtiene los detalles de un ticket de soporte por su ID.")]
    public static async Task<string> ObtenerTicket(
        [Description("ID del ticket, por ejemplo: INC0001")]
        string ticketId)
    {
        var ticket = await AgentAiApi.GetTicketByNumberAsync(ticketId);

        if (ticket is null)
            return $"Ticket {ticketId} no encontrado.";

        return $"Ticket {ticket.Number}: {JsonSerializer.Serialize(ticket, AgentAiApi.JsonOptions)}";
    }

    [McpServerTool, Description("Analiza el problema del ticket y determina qué agente de acción debe resolverlo.")]
    public static string DiagnosticarProblema(
        [Description("Descripción del problema")]
        string descripcion,
        [Description("Sistema afectado: usuarios, pedidos, pagos, catalogo, stock")]
        string sistema)
    {
        return (sistema.ToLower()) switch
        {
            "usuarios" => "{ \"agente\": \"AgenteAccionAcceso\", \"accion\": \"resetear_acceso\", \"confianza\": \"alta\" }",
            "pedidos"  => "{ \"agente\": \"AgenteAccionPedido\", \"accion\": \"consultar_estado_pedido\", \"confianza\": \"alta\" }",
            "pagos"    => "{ \"agente\": \"AgenteAccionPago\", \"accion\": \"consultar_pago\", \"confianza\": \"alta\" }",
            "catalogo" or "precio" => "{ \"agente\": \"AgenteAccionPrecio\", \"accion\": \"corregir_precio\", \"confianza\": \"alta\" }",
            "stock"    => "{ \"agente\": \"AgenteAccionStock\", \"accion\": \"sincronizar_stock\", \"confianza\": \"alta\" }",
            _          => "{ \"agente\": \"Escalacion\", \"accion\": \"escalar_nivel2\", \"confianza\": \"baja\" }"
        };
    }

    [McpServerTool, Description("Busca en la base de conocimiento una solución aplicable para el problema y sistema del ticket.")]
    public static async Task<string> BuscarKb(
        [Description("Descripción del problema del ticket")]
        string descripcion,
        [Description("Sistema afectado: usuarios, pedidos, pagos, catalogo, stock")]
        string sistema)
    {
        var resultado = await AgentAiApi.SearchKnowledgeBaseAsync(descripcion, sistema);
        return JsonSerializer.Serialize(resultado, AgentAiApi.JsonOptions);
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — PEDIDO
// ============================================================
[McpServerToolType]
public static class ToolsAccionPedido
{
    [McpServerTool, Description("Consulta el estado actual de un pedido por su ID.")]
    public static string ConsultarEstadoPedido(
        [Description("ID del pedido, por ejemplo: ORD-5521")]
        string pedidoId)
    {
        var pedidos = new Dictionary<string, object>
        {
            ["ORD-5521"] = new { Estado = "En preparación", FechaEstimada = "2026-05-10", Ubicacion = "Depósito central" },
            ["ORD-8834"] = new { Estado = "Enviado", FechaEstimada = "2026-05-08", Ubicacion = "En tránsito - correo argentino" },
            ["ORD-1122"] = new { Estado = "Entregado", FechaEstimada = "2026-05-05", Ubicacion = "Entregado al destinatario" },
        };

        if (pedidos.TryGetValue(pedidoId.ToUpper(), out var pedido))
            return $"Pedido {pedidoId}: {System.Text.Json.JsonSerializer.Serialize(pedido)}";

        return $"Pedido {pedidoId} no encontrado.";
    }

    [McpServerTool, Description("Cierra un ticket de pedido una vez resuelto.")]
    public static async Task<string> CerrarTicketPedido(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return await AgentAiApi.CloseTicketAsync(ticketId, resolucion, "PEDIDO");
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — ACCESO
// ============================================================
[McpServerToolType]
public static class ToolsAccionAcceso
{
    [McpServerTool, Description("Resetea el acceso de un usuario que no puede iniciar sesión.")]
    public static async Task<string> ResetearAcceso(
        [Description("Email del usuario con problema de acceso")]
        string usuario)
    {
        return await TurneraApi.ResetearAccesoAsync(usuario);
    }

    [McpServerTool, Description("Cierra un ticket de acceso una vez resuelto.")]
    public static async Task<string> CerrarTicketAcceso(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return await AgentAiApi.CloseTicketAsync(ticketId, resolucion, "ACCESO");
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — PAGO
// ============================================================
[McpServerToolType]
public static class ToolsAccionPago
{
    [McpServerTool, Description("Consulta el estado de un pago rechazado y verifica si el débito fue aplicado.")]
    public static string ConsultarPago(
        [Description("Email del usuario con problema de pago")]
        string usuario)
    {
        return $"[PAGO] Consultando pago para {usuario}. Se detectó un débito pendiente de reversión. El área de pagos fue notificada para procesar el reembolso en 48hs hábiles.";
    }

    [McpServerTool, Description("Cierra un ticket de pago una vez resuelto.")]
    public static async Task<string> CerrarTicketPago(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return await AgentAiApi.CloseTicketAsync(ticketId, resolucion, "PAGO");
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — PRECIO
// ============================================================
[McpServerToolType]
public static class ToolsAccionPrecio
{
    [McpServerTool, Description("Corrige el precio de un producto en el catálogo.")]
    public static string CorregirPrecio(
        [Description("SKU del producto con precio incorrecto")]
        string sku,
        [Description("Precio correcto del producto")]
        string precioCorrecto)
    {
        return $"[PRECIO] Precio del producto {sku} actualizado a {precioCorrecto}. Catálogo actualizado exitosamente.";
    }

    [McpServerTool, Description("Cierra un ticket de precio una vez resuelto.")]
    public static async Task<string> CerrarTicketPrecio(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return await AgentAiApi.CloseTicketAsync(ticketId, resolucion, "PRECIO");
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — STOCK
// ============================================================
[McpServerToolType]
public static class ToolsAccionStock
{
    [McpServerTool, Description("Sincroniza el stock de un producto que no se actualizó correctamente.")]
    public static string SincronizarStock(
        [Description("SKU del producto con stock desincronizado")]
        string sku)
    {
        return $"[STOCK] Stock del producto {sku} sincronizado exitosamente. Inventario actualizado con los datos del último archivo de importación.";
    }

    [McpServerTool, Description("Cierra un ticket de stock una vez resuelto.")]
    public static async Task<string> CerrarTicketStock(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return await AgentAiApi.CloseTicketAsync(ticketId, resolucion, "STOCK");
    }
}

public static class AgentAiApi
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("AGENTAI_API_URL") ?? "http://localhost:5038")
    };

    public static async Task<TicketResponse> CreateTicketFromAgentAsync(CreateAgentTicketRequest request)
    {
        using var response = await Http.PostAsJsonAsync("/tickets/from-agent", request, JsonOptions);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions)
            ?? throw new InvalidOperationException("La API no devolvió el ticket creado.");
    }

    public static async Task<TicketResponse?> GetTicketByNumberAsync(string number)
    {
        using var response = await Http.GetAsync($"/tickets/by-number/{Uri.EscapeDataString(number.ToUpperInvariant())}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions);
    }

    public static async Task<string> CloseTicketAsync(string ticketId, string resolution, string area)
    {
        var ticket = await GetTicketByNumberAsync(ticketId);

        if (ticket is null)
            return $"[{area}] Ticket {ticketId} no encontrado.";

        var request = new UpdateTicketRequest(
            Title: null,
            Description: $"{ticket.Description}\n\nResolución: {resolution}",
            State: 4,
            StateLabel: "Resuelto",
            Priority: null,
            PriorityLabel: null,
            AssignedTo: null,
            AssignmentGroup: null,
            ResolvedAt: DateTime.UtcNow,
            AffectedSystem: area.ToLower());

        using var response = await Http.PutAsJsonAsync($"/tickets/{ticket.Id}", request, JsonOptions);
        response.EnsureSuccessStatusCode();

        return $"[{area}] Ticket {ticketId} cerrado. Resolución: {resolution}";
    }

    public static async Task<string> EscalateTicketAsync(string ticketId, string reason)
    {
        var ticket = await GetTicketByNumberAsync(ticketId);

        if (ticket is null)
            return $"[ESCALACION] Ticket {ticketId} no encontrado.";

        var request = new UpdateTicketRequest(
            Title: null,
            Description: $"{ticket.Description}\n\nEscalación N2: {reason}",
            State: 2,
            StateLabel: "En proceso Nivel 2",
            Priority: ticket.Priority <= 2 ? ticket.Priority : 2,
            PriorityLabel: ticket.Priority <= 2 ? ticket.PriorityLabel : "High",
            AssignedTo: null,
            AssignmentGroup: "Nivel 2",
            ResolvedAt: null,
            AffectedSystem: null);

        using var response = await Http.PutAsJsonAsync($"/tickets/{ticket.Id}", request, JsonOptions);
        response.EnsureSuccessStatusCode();

        return $"[ESCALACION] Ticket {ticketId} escalado a Nivel 2. Motivo: {reason}";
    }

    public static async Task<KbResult> SearchKnowledgeBaseAsync(string query, string system)
    {
        var article = MarkdownKnowledgeBase.Search(query, system);
        if (article is null)
        {
            return new KbResult(
                "KB-SIN_SOLUCION",
                system,
                "No hay artículo de KB aplicable con confianza suficiente.",
                "baja",
                "escalar_nivel2");
        }

        return new KbResult(
            article.ArticleCode,
            string.IsNullOrWhiteSpace(article.System) ? system : article.System,
            article.RecommendedAction,
            article.Confidence,
            InferRecommendedAction(article));
    }

    private static string InferRecommendedAction(KnowledgeBaseSearchResult article)
    {
        var text = $"{article.Actions} {article.RecommendedAction} {article.Tags} {article.System} {article.Description}".ToLowerInvariant();

        if (text.Contains("reset") || text.Contains("acceso") || text.Contains("login") || text.Contains("sesion"))
            return "resetear_acceso";
        if (text.Contains("pedido") || text.Contains("orden") || text.Contains("ord-"))
            return "consultar_estado_pedido";
        if (text.Contains("pago") || text.Contains("tarjeta") || text.Contains("debito") || text.Contains("reembolso"))
            return "consultar_pago";
        if (text.Contains("precio") || text.Contains("catalogo"))
            return "corregir_precio";
        if (text.Contains("stock") || text.Contains("inventario"))
            return "sincronizar_stock";

        return article.Confidence is "alta" or "media" ? "resolver_con_kb" : "escalar_nivel2";
    }
}

public class NullMessageQueue : IMessageQueue
{
    public Task<IReadOnlyList<QueueMessage>> ReceiveMessagesAsync(int maxMessages, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<QueueMessage>>(new List<QueueMessage>());

    public Task SendMessageAsync(string body, CancellationToken ct)
        => Task.CompletedTask;

    public Task DeleteMessageAsync(string receiptHandle, CancellationToken ct)
        => Task.CompletedTask;
}

public static class MarkdownKnowledgeBase
{
    private static IReadOnlyList<KnowledgeBaseSearchResult>? CachedArticles;

    public static KnowledgeBaseSearchResult? Search(string query, string system)
    {
        var articles = CachedArticles ??= Load();
        var queryText = Normalize($"{system} {query}");
        var normalizedSystem = Normalize(system);

        return articles
            .Select(article => new
            {
                Article = article,
                Score = ScoreArticle(article, queryText, normalizedSystem)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Article.ArticleId)
            .Select(item => item.Article)
            .FirstOrDefault();
    }

    private static IReadOnlyList<KnowledgeBaseSearchResult> Load()
    {
        var paths = FindKnowledgeBaseFiles();
        if (paths.Count == 0)
            return [];

        var articles = new List<KnowledgeBaseSearchResult>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentCode = null;
        var articleId = 1;

        foreach (var path in paths)
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("## KB-", StringComparison.OrdinalIgnoreCase))
                {
                    AddArticle();
                    var title = line.TrimStart('#').Trim();
                    currentCode = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? title;
                    fields.Clear();
                    continue;
                }

                if (currentCode is null || string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("- "))
                    continue;

                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                fields[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        AddArticle();
        return articles;

        void AddArticle()
        {
            if (currentCode is null)
                return;

            articles.Add(new KnowledgeBaseSearchResult(
                ArticleId: articleId++,
                ArticleCode: currentCode,
                System: GetField("Sistema"),
                SystemType: "turnera",
                Tags: GetField("Tags"),
                Actions: GetField("Acciones"),
                Description: GetField("Descripcion"),
                Symptoms: GetField("Sintomas"),
                ProbableCause: GetField("Causa probable"),
                RequiredData: GetField("Datos requeridos"),
                Preconditions: GetField("Precondiciones"),
                RecommendedAction: GetField("Accion recomendada"),
                Validation: GetField("Validacion"),
                ExpectedResult: GetField("Resultado esperado"),
                EscalationCriteria: GetField("Criterios de escalacion"),
                SuggestedUserMessage: GetField("Mensaje sugerido"),
                Confidence: GetField("Confianza")));
        }

        string GetField(string key)
            => fields.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static IReadOnlyList<string> FindKnowledgeBaseFiles()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTAI_KB_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return [Path.GetFullPath(configured)];

        var directories = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "KnowledgeBase")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "KnowledgeBase")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "KnowledgeBase")),
        };

        foreach (var directory in directories.Where(Directory.Exists))
        {
            var splitFiles = Directory.GetFiles(directory, "*.md")
                .Where(path =>
                    !Path.GetFileName(path).Equals("entrada.md", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(path).Equals("kb-turnera-pilates.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path)
                .ToList();

            if (splitFiles.Count > 0)
                return splitFiles;

            var monolith = Path.Combine(directory, "kb-turnera-pilates.md");
            if (File.Exists(monolith))
                return [monolith];
        }

        return Array.Empty<string>();
    }

    private static int ScoreArticle(KnowledgeBaseSearchResult article, string queryText, string system)
    {
        var articleSystem = Normalize(article.System);
        var score = articleSystem.Equals(system, StringComparison.OrdinalIgnoreCase) ? 25 : 0;

        if (!string.IsNullOrWhiteSpace(system) && Normalize(article.Tags).Contains(system))
            score += 10;

        var searchable = Normalize(string.Join(' ', article.ArticleCode, article.System, article.Tags, article.Description, article.Symptoms, article.RequiredData, article.RecommendedAction));
        foreach (var token in queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
        {
            if (token.Length < 4)
                continue;

            if (searchable.Contains(token))
                score += 3;
        }

        return score;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

public static class TurneraApi
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("TURNERA_API_URL") ?? "http://localhost:3000")
    };

    public static async Task<string> ResetearAccesoAsync(string email)
    {
        var apiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return "[ACCESO] No puedo resetear el acceso: falta configurar AGENT_API_KEY en el entorno del agente.";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/reset-acceso")
        {
            Content = JsonContent.Create(new { email }, options: AgentAiApi.JsonOptions)
        };
        request.Headers.Add("x-api-key", apiKey);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return $"[ACCESO] No pude resetear el acceso para {email}. Turnera respondió {(int)response.StatusCode}: {TryReadError(body)}";

        var result = JsonSerializer.Deserialize<TurneraResetAccessResponse>(body, AgentAiApi.JsonOptions)
            ?? throw new InvalidOperationException("La turnera no devolvió una respuesta válida.");

        var message = string.IsNullOrWhiteSpace(result.Mensaje)
            ? "Acceso reseteado correctamente."
            : result.Mensaje;

        return $"[ACCESO] {message}";
    }

    private static string TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "sin detalle";

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                ? error.GetString() ?? body
                : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}

public record CreateAgentTicketRequest(
    string Description,
    string System,
    string ErrorType,
    string UserEmail);

public record UpdateTicketRequest(
    string? Title = null,
    string? Description = null,
    int? State = null,
    string? StateLabel = null,
    int? Priority = null,
    string? PriorityLabel = null,
    string? AssignedTo = null,
    string? AssignmentGroup = null,
    DateTime? ResolvedAt = null,
    string? AffectedSystem = null,
    string? CreatedByEmail = null,
    string? CreatedByName = null);

public record TicketResponse(
    int Id,
    string SysId,
    string Number,
    string Title,
    string Description,
    int State,
    string StateLabel,
    int Priority,
    string PriorityLabel,
    string CreatedByName,
    string CreatedByEmail,
    DateTime OpenedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    DateTime LastSyncedAt);

public record KbResult(
    string ArticleId,
    string System,
    string Solution,
    string Confidence,
    string RecommendedAction);

public record TurneraResetAccessResponse(
    bool Ok,
    TurneraSocioResponse? Socio,
    string? TempPassword,
    string? Mensaje);

public record TurneraSocioResponse(
    string Id,
    string Name,
    string Email);

public record KnowledgeBaseSearchResult(
    int ArticleId,
    string ArticleCode,
    string System,
    string SystemType,
    string Tags,
    string Actions,
    string Description,
    string Symptoms,
    string ProbableCause,
    string RequiredData,
    string Preconditions,
    string RecommendedAction,
    string Validation,
    string ExpectedResult,
    string EscalationCriteria,
    string SuggestedUserMessage,
    string Confidence);
