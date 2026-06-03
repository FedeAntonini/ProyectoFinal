using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

if (args.Any(arg => arg.Equals("--telegram-intake", StringComparison.OrdinalIgnoreCase)))
{
    var input = await Console.In.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<IntakeRequest>(input, IntakeJson.Options)
        ?? throw new InvalidOperationException("No pude leer el ticket para AgenteEntrada.");

    var decision = await IntakeAnalyzer.AnalyzeAsync(request);
    Console.WriteLine(JsonSerializer.Serialize(decision, IntakeJson.Options));
    return;
}

var API_KEY = Environment.GetEnvironmentVariable("GROQ_API_KEY")
    ?? throw new Exception("Falta la variable de entorno GROQ_API_KEY");
const string MODELO = "llama-3.3-70b-versatile";

Console.WriteLine("Conectando al MCP Server...");

var transporte = new StdioClientTransport(new StdioClientTransportOptions
{
    Command = "dotnet",
    Arguments = ["run", "--no-build", "--project", "../McpServer"],
    Name = "SoporteMcpServer",
});

await using var mcp = await McpClient.CreateAsync(transporte);
var todasLasTools = await mcp.ListToolsAsync();

// Tools para crear tickets nuevos o consultar tickets existentes
var toolsEntrada = todasLasTools
    .Where(t => t.Name is "registrar_incidente" or "obtener_ticket")
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
    Sos el Agente de Entrada de un sistema de soporte técnico de e-commerce nivel 1.
    Tu trabajo es recibir casos nuevos o tickets existentes, validar si falta información y dejarlos listos para derivar.

    Si el usuario menciona un ticket existente con formato INC seguido de números:
    - Usá la tool obtener_ticket.
    - Si el ticket existe, revisá si tiene descripción clara, sistema afectado y usuario/email.
    - Si falta información, preguntá una sola cosa puntual por vez.
    - Cuando el ticket tenga información suficiente, respondé exactamente con este formato:
      DERIVAR_A_ENRUTADOR: INC1234
      Motivo: [resumen breve del problema y datos disponibles]
    - No intentes resolver ni diagnosticar.

    Si el usuario no menciona un ticket existente, recolectá datos para registrar un incidente nuevo.
    Para poder registrar un incidente nuevo necesitás obtener obligatoriamente:
    1. Descripción clara del problema
    2. Sistema afectado (usuarios, pedidos, pagos, catalogo, stock)
    3. Tipo de error (dato_incorrecto, operacion_bloqueada, inconsistencia, error_sistema)
    4. Email del usuario

    Reglas:
    - Hablá en español, de forma amigable y clara.
    - Si falta información, preguntá puntualmente. Una pregunta a la vez.
    - Cuando tengas todos los datos, usá la tool registrar_incidente para crear el ticket.
    - Una vez que uses la tool registrar_incidente, respondé con:
      Tu incidente fue registrado con el ID: INC1234.
      DERIVAR_A_ENRUTADOR: INC1234
      Motivo: [resumen breve del problema y datos disponibles]
    - No ejecutes acciones correctivas. Solo recolectá datos, consultá o registrá el incidente y derivalo.
    """);

var historial = new List<ChatMessage> { systemPrompt };

Console.WriteLine("=== Agente de Entrada — E-Commerce Soporte N1 ===");
Console.WriteLine("Hola! Soy el agente de soporte. ¿En qué te puedo ayudar?");
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

        // Si el caso quedó listo para enrutamiento, terminamos la sesión
        if (texto.Contains("DERIVAR_A_ENRUTADOR", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[Sistema] Caso listo para derivar al Agente Enrutador.");
            break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Sesión del Agente de Entrada finalizada.");

public sealed record IntakeRequest(
    string Number,
    string SysId,
    string Title,
    string Description,
    string CreatedByEmail);

public sealed record IntakeDecision(
    string Decision,
    string? MissingField,
    string? Question,
    string? System,
    string? ArticleCode,
    string? Confidence,
    string? Action,
    string? Agent,
    string? User,
    string? Reason,
    string? RecommendedAction,
    string? SuggestedUserMessage);

public sealed record KnowledgeBaseSearchResult(
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

public static partial class IntakeAnalyzer
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("AGENTAI_API_URL") ?? "http://localhost:5038")
    };

    public static async Task<IntakeDecision> AnalyzeAsync(IntakeRequest request)
    {
        var text = $"{request.Title} {request.Description}".Trim();
        var system = InferSystem(text);

        if (!HasMeaningfulDescription(text))
            return Ask(request, "description", "Necesito una descripcion clara del problema: que intentabas hacer y que error aparece.", system);

        if (string.IsNullOrWhiteSpace(system))
            return Ask(request, "system", "Decime que sistema esta afectado: turnera, usuarios, pedidos, pagos, catalogo o stock.", system);

        if (ContainsHighRiskSignal(text))
            return Escalate(system, "El caso contiene senales de riesgo, fraude, seguridad o impacto economico que requieren revision humana.");

        KnowledgeBaseSearchResult? article;
        try
        {
            article = await SearchKnowledgeBaseAsync(text, system);
        }
        catch
        {
            return Escalate(system, "No pude consultar la KB para validar una solucion segura.");
        }

        if (article is null)
            return Escalate(system, "No encontre un articulo de KB aplicable para resolver el caso con seguridad.");

        if (article.Confidence.Equals("baja", StringComparison.OrdinalIgnoreCase))
            return Escalate(system, $"La KB encontrada ({article.ArticleCode}) tiene confianza baja.", article);

        var email = ExtractEmail(request);
        if (RequiresEmail(article) && string.IsNullOrWhiteSpace(email))
        {
            return Ask(
                request,
                "email",
                $"Encontre el ticket {request.Number}. Para operar sobre tu usuario de la turnera, decime el email con el que estas registrado.",
                system,
                article);
        }

        if (EscalationCriteriaApplies(text, article.EscalationCriteria))
            return Escalate(system, $"La KB {article.ArticleCode} indica criterios de escalacion aplicables.", article);

        var action = InferAction(article);
        if (action == "resetear_acceso")
        {
            return new IntakeDecision(
                Decision: "ejecutar_accion",
                MissingField: null,
                Question: null,
                System: system,
                ArticleCode: article.ArticleCode,
                Confidence: article.Confidence,
                Action: action,
                Agent: "AgenteAccionAcceso",
                User: email,
                Reason: null,
                RecommendedAction: article.RecommendedAction,
                SuggestedUserMessage: article.SuggestedUserMessage);
        }

        if (action == "consultar_pago")
        {
            return new IntakeDecision(
                Decision: "ejecutar_accion",
                MissingField: null,
                Question: null,
                System: system,
                ArticleCode: article.ArticleCode,
                Confidence: article.Confidence,
                Action: action,
                Agent: "AgenteAccionPago",
                User: email,
                Reason: null,
                RecommendedAction: article.RecommendedAction,
                SuggestedUserMessage: article.SuggestedUserMessage);
        }

        if (action == "consultar_turno")
        {
            return new IntakeDecision(
                Decision: "ejecutar_accion",
                MissingField: null,
                Question: null,
                System: system,
                ArticleCode: article.ArticleCode,
                Confidence: article.Confidence,
                Action: action,
                Agent: "AgenteAccionTurno",
                User: email,
                Reason: null,
                RecommendedAction: article.RecommendedAction,
                SuggestedUserMessage: article.SuggestedUserMessage);
        }

        return new IntakeDecision(
            Decision: "continuar",
            MissingField: null,
            Question: null,
            System: system,
            ArticleCode: article.ArticleCode,
            Confidence: article.Confidence,
            Action: action,
            Agent: null,
            User: email,
            Reason: null,
            RecommendedAction: article.RecommendedAction,
            SuggestedUserMessage: article.SuggestedUserMessage);
    }

    private static async Task<KnowledgeBaseSearchResult?> SearchKnowledgeBaseAsync(string query, string system)
    {
        var url = $"/knowledge-base/search?query={Uri.EscapeDataString(query)}&system={Uri.EscapeDataString(system)}&limit=1";
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<KnowledgeBaseSearchResult>>(IntakeJson.Options);
        return results?.FirstOrDefault();
    }

    private static IntakeDecision Ask(
        IntakeRequest request,
        string missingField,
        string question,
        string? system,
        KnowledgeBaseSearchResult? article = null)
        => new(
            Decision: "pedir_info",
            MissingField: missingField,
            Question: question,
            System: system,
            ArticleCode: article?.ArticleCode,
            Confidence: article?.Confidence,
            Action: null,
            Agent: null,
            User: ExtractEmail(request),
            Reason: null,
            RecommendedAction: article?.RecommendedAction,
            SuggestedUserMessage: article?.SuggestedUserMessage);

    private static IntakeDecision Escalate(
        string? system,
        string reason,
        KnowledgeBaseSearchResult? article = null)
        => new(
            Decision: "escalar",
            MissingField: null,
            Question: null,
            System: system,
            ArticleCode: article?.ArticleCode,
            Confidence: article?.Confidence,
            Action: null,
            Agent: null,
            User: null,
            Reason: reason,
            RecommendedAction: article?.RecommendedAction,
            SuggestedUserMessage: article?.SuggestedUserMessage);

    private static string InferSystem(string text)
    {
        var normalized = Normalize(text);

        if (normalized.Contains("pago") ||
            normalized.Contains("creditos") ||
            normalized.Contains("credito") ||
            normalized.Contains("tarjeta") ||
            normalized.Contains("debito") ||
            normalized.Contains("cobro") ||
            normalized.Contains("cargo"))
            return "pagos";
        if (normalized.Contains("turnera") ||
            normalized.Contains("turno") ||
            normalized.Contains("turno reservado") ||
            normalized.Contains("ver mis turnos") ||
            normalized.Contains("reserva"))
            return "turnera";
        if (normalized.Contains("usuario") ||
            normalized.Contains("login") ||
            normalized.Contains("sesion") ||
            normalized.Contains("credencial") ||
            normalized.Contains("contrasena") ||
            normalized.Contains("password") ||
            normalized.Contains("acceso"))
            return "usuarios";
        if (normalized.Contains("pedido") || normalized.Contains("ord-"))
            return "pedidos";
        if (normalized.Contains("catalogo") || normalized.Contains("precio"))
            return "catalogo";
        if (normalized.Contains("stock") || normalized.Contains("inventario"))
            return "stock";

        return string.Empty;
    }

    private static bool RequiresEmail(KnowledgeBaseSearchResult article)
    {
        var text = Normalize($"{article.RequiredData} {article.Preconditions} {article.RecommendedAction}");
        return text.Contains("email") || text.Contains("usuario") || text.Contains("socio");
    }

    private static string InferAction(KnowledgeBaseSearchResult article)
    {
        var text = Normalize($"{article.Actions} {article.RecommendedAction} {article.Tags} {article.System} {article.Description}");

        if (text.Contains("resetear_acceso") ||
            (text.Contains("reset") && (text.Contains("acceso") || text.Contains("login") || text.Contains("sesion"))))
            return "resetear_acceso";

        if (text.Contains("consultar_pago") ||
            text.Contains("pago") ||
            text.Contains("creditos"))
            return "consultar_pago";

        if (text.Contains("consultar_turno") ||
            text.Contains("turno"))
            return "consultar_turno";

        return string.IsNullOrWhiteSpace(article.RecommendedAction) ? "resolver_con_kb" : article.RecommendedAction;
    }

    private static bool HasMeaningfulDescription(string text)
    {
        if (text.Length < 25)
            return false;

        var normalized = Normalize(text);
        return normalized is not "prueba" and not "test";
    }

    private static bool ContainsHighRiskSignal(string text)
    {
        var normalized = Normalize(text);
        var signals = new[]
        {
            "fraude",
            "no reconozco",
            "no reconoci",
            "compra que no hice",
            "compras que no hice",
            "cargo desconocido",
            "cargos desconocidos",
            "doble cobro",
            "cobraron dos veces",
            "tarjeta robada",
            "accedio sin permiso",
            "accedieron sin permiso",
            "cuenta comprometida",
            "hackearon",
            "suplantacion",
            "datos sensibles",
            "denuncia"
        };

        return signals.Any(normalized.Contains);
    }

    private static bool EscalationCriteriaApplies(string text, string escalationCriteria)
    {
        if (string.IsNullOrWhiteSpace(escalationCriteria))
            return false;

        var criteria = Normalize(escalationCriteria);
        return ContainsHighRiskSignal(text) ||
            criteria.Contains("siempre escalar") ||
            criteria.Contains("escalar siempre") ||
            criteria.Contains("requiere soporte humano");
    }

    private static string? ExtractEmail(IntakeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CreatedByEmail))
            return request.CreatedByEmail.Trim();

        var match = EmailRegex().Match($"{request.Title} {request.Description}");
        return match.Success ? match.Value : null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}

public static class IntakeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
