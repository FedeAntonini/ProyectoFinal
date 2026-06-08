using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

if (args.Any(arg => arg.Equals("--telegram-intake", StringComparison.OrdinalIgnoreCase)))
{
    var input = (await Console.In.ReadToEndAsync()).TrimStart('\uFEFF');
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

var promptLoader = new AgentPromptLoader();
var systemPrompt = new ChatMessage(ChatRole.System, promptLoader.Load("entrada.md"));

var historial = new List<ChatMessage> { systemPrompt };

Console.WriteLine("=== Agente de Entrada - Turnera Pilates Soporte N1 ===");
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
    private static IReadOnlyList<KnowledgeBaseSearchResult>? CachedKnowledgeBase;

    public static async Task<IntakeDecision> AnalyzeAsync(IntakeRequest request)
    {
        var text = $"{request.Title} {request.Description}".Trim();
        var memoryMatch = AgentCaseMemory.FindBestMatch(text);
        var system = SelectSystem(text, memoryMatch);

        if (!HasMeaningfulDescription(text))
            return Ask(request, "description", "Necesito una descripcion clara del problema: que intentabas hacer y que error aparece.", system);

        if (string.IsNullOrWhiteSpace(system))
            return Ask(request, "system", "Decime que modulo esta afectado: acceso, socios, turnos, profesores, pagos, disponibilidad o clases.", system);

        if (ContainsHighRiskSignal(text))
        {
            var decision = Escalate(system, "El caso contiene senales de riesgo, fraude, seguridad o impacto economico que requieren revision humana.");
            AgentCaseMemory.Save(request.Number, system, "AgenteEntrada", "no_resueltos", text, null, decision.Decision, decision.Reason, null, memoryMatch);
            return decision;
        }

        KnowledgeBaseSearchResult? article;
        article = await SearchKnowledgeBaseAsync(text, system);

        if (article is null)
        {
            var decision = Escalate(system, "No encontre un articulo de KB aplicable para resolver el caso con seguridad.");
            AgentCaseMemory.Save(request.Number, system, "AgenteEntrada", "no_resueltos", text, null, decision.Decision, decision.Reason, null, memoryMatch);
            return decision;
        }

        if (article.Confidence.Equals("baja", StringComparison.OrdinalIgnoreCase))
        {
            var decision = Escalate(system, $"La KB encontrada ({article.ArticleCode}) tiene confianza baja.", article);
            AgentCaseMemory.Save(request.Number, system, "AgenteEntrada", "no_resueltos", text, article.ArticleCode, decision.Decision, decision.Reason, article.RecommendedAction, memoryMatch);
            return decision;
        }

        var email = ExtractEmail(request);
        if (RequiresEmail(article) && string.IsNullOrWhiteSpace(email))
        {
            return Ask(
                request,
                "email",
                BuildMissingEmailQuestion(request.Number, article),
                system,
                article);
        }

        if (EscalationCriteriaApplies(text, article.EscalationCriteria))
        {
            var decision = Escalate(system, $"La KB {article.ArticleCode} indica criterios de escalacion aplicables.", article);
            AgentCaseMemory.Save(request.Number, system, "AgenteEntrada", "no_resueltos", text, article.ArticleCode, decision.Decision, decision.Reason, article.RecommendedAction, memoryMatch);
            return decision;
        }

        var action = InferAction(article);
        if (action == "consultar_turnos" && !HasTurnDetails(text))
        {
            return Ask(
                request,
                "description",
                $"Encontre el ticket {request.Number}. Para consultar la reserva necesito que me indiques profesor, fecha y horario del turno.",
                system,
                article);
        }

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

    private static Task<KnowledgeBaseSearchResult?> SearchKnowledgeBaseAsync(string query, string system)
    {
        var articles = CachedKnowledgeBase ??= LoadMarkdownKnowledgeBase();
        var queryText = Normalize($"{system} {query}");

        var result = articles
            .Select(article => new
            {
                Article = article,
                Score = ScoreArticle(article, queryText, Normalize(system))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Article.ArticleId)
            .Select(item => item.Article)
            .FirstOrDefault();

        return Task.FromResult(result);
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
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["acceso"] = Score(normalized,
                ("credenciales invalidas", 16), ("credencial", 12), ("login", 12), ("iniciar sesion", 12),
                ("sesion", 10), ("password", 10), ("contrasena", 10), ("acceso", 9), ("bloquead", 8)),
            ["pagos"] = Score(normalized,
                ("pago", 12), ("pague", 14), ("pagaste", 14), ("abone", 14), ("abonado", 10),
                ("abono", 10), ("creditos", 12), ("credito", 10), ("me dieron", 12), ("me cargaron", 12),
                ("menos clases", 12), ("dieron menos", 12), ("acreditaron", 10), ("acreditacion", 10), ("paquete", 9), ("tarjeta", 7),
                ("debito", 7), ("cobro", 9), ("cargo", 9)),
            ["profesores"] = Score(normalized,
                ("profesor", 10), ("instructor", 10), ("docente", 8)),
            ["disponibilidad"] = Score(normalized,
                ("disponibilidad", 12), ("cupo", 10), ("cupos", 10), ("completo", 9),
                ("lugares", 9), ("sin lugar", 10)),
            ["turnos"] = Score(normalized,
                ("reserva", 10), ("reservas", 10), ("turno", 10), ("turnos", 10),
                ("confirmacion", 6), ("no aparece", 6), ("turnera", 1)),
            ["clases"] = Score(normalized,
                ("clase", 7), ("clases", 7), ("horario", 8), ("agenda", 8), ("calendario", 8)),
            ["socios"] = Score(normalized,
                ("socio", 8), ("usuario", 5), ("perfil", 8), ("registrado", 6)),
            ["pedidos"] = Score(normalized,
                ("pedido", 10), ("ord-", 10)),
            ["catalogo"] = Score(normalized,
                ("catalogo", 10), ("precio", 10)),
            ["stock"] = Score(normalized,
                ("stock", 10), ("inventario", 10))
        };

        var best = scores
            .Where(item => item.Value > 0)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key)
            .FirstOrDefault();

        if (normalized.Contains("pago") ||
            normalized.Contains("pague") ||
            normalized.Contains("pagaste") ||
            normalized.Contains("abone") ||
            normalized.Contains("abonado") ||
            normalized.Contains("abono") ||
            normalized.Contains("creditos") ||
            normalized.Contains("credito") ||
            normalized.Contains("me dieron") ||
            normalized.Contains("me cargaron") ||
            normalized.Contains("acreditaron") ||
            normalized.Contains("acreditacion") ||
            normalized.Contains("paquete") ||
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
            return "acceso";
        if (normalized.Contains("socio") ||
            normalized.Contains("usuario") ||
            normalized.Contains("perfil") ||
            normalized.Contains("registrado"))
            return "socios";
        if (normalized.Contains("pedido") || normalized.Contains("ord-"))
            return "pedidos";
        if (normalized.Contains("catalogo") || normalized.Contains("precio"))
            return "catalogo";
        if (normalized.Contains("stock") || normalized.Contains("inventario"))
            return "stock";

        return string.Empty;
    }

    private static string SelectSystem(string text, AgentCaseMemory.Match? memoryMatch)
    {
        var inferred = InferSystem(text);
        if (memoryMatch is null || memoryMatch.Score < 12)
            return inferred;

        if (string.IsNullOrWhiteSpace(inferred))
            return memoryMatch.Case.System;

        if (IsWeakSystemInference(inferred, text))
            return memoryMatch.Case.System;

        return inferred;
    }

    private static bool IsWeakSystemInference(string system, string text)
    {
        var normalized = Normalize(text);
        if (system is "socios" && (normalized.Contains("usuario") || normalized.Contains("socio")))
            return true;

        if (system is "acceso" && normalized.Contains("usuario") && !ContainsAny(normalized, "login", "sesion", "credencial", "contrasena", "password", "acceso"))
            return true;

        return false;
    }

    private static bool ContainsAny(string text, params string[] values)
        => values.Any(text.Contains);

    private static int Score(string text, params (string Token, int Weight)[] weightedTokens)
        => weightedTokens.Where(item => text.Contains(item.Token)).Sum(item => item.Weight);

    private static bool RequiresEmail(KnowledgeBaseSearchResult article)
    {
        var text = Normalize($"{article.RequiredData} {article.Preconditions} {article.RecommendedAction}");
        if (text.Contains("consultar_disponibilidad"))
            return false;

        return text.Contains("email") || text.Contains("usuario") || text.Contains("socio");
    }

    private static string BuildMissingEmailQuestion(string ticketNumber, KnowledgeBaseSearchResult article)
    {
        var text = Normalize($"{article.Actions} {article.RecommendedAction} {article.System} {article.Tags}");
        var operation = text switch
        {
            var value when value.Contains("pago") || value.Contains("credito") => "consultar tus pagos y creditos",
            var value when value.Contains("turno") || value.Contains("reserva") => "consultar tus turnos",
            var value when value.Contains("acceso") || value.Contains("resetear") => "operar sobre tu usuario de la turnera",
            _ => "continuar con la accion automatica"
        };

        return $"Encontre el ticket {ticketNumber}. Para {operation}, decime el email con el que estas registrado.";
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

    private static IReadOnlyList<KnowledgeBaseSearchResult> LoadMarkdownKnowledgeBase()
    {
        var paths = FindKnowledgeBaseFiles();
        if (paths.Count == 0)
            return Array.Empty<KnowledgeBaseSearchResult>();

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

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                fields[key] = value;
            }
        }

        AddArticle();
        return articles;

        void AddArticle()
        {
            if (currentCode is null)
                return;

            var system = GetField("Sistema");
            articles.Add(new KnowledgeBaseSearchResult(
                ArticleId: articleId++,
                ArticleCode: currentCode,
                System: system,
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
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "KnowledgeBase")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "KnowledgeBase")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "KnowledgeBase"))
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
            "alguien entro a mi cuenta",
            "entraron a mi cuenta",
            "ingresaron a mi cuenta",
            "ingreso a mi cuenta",
            "sin autorizacion",
            "sin mi autorizacion",
            "cuenta comprometida",
            "cuenta hackeada",
            "me hackearon",
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
        var hasMultipleImpactCriteria =
            criteria.Contains("multiples") ||
            criteria.Contains("varios") ||
            criteria.Contains("multiple");

        return ContainsHighRiskSignal(text) ||
            criteria.Contains("siempre escalar") ||
            criteria.Contains("escalar siempre") ||
            criteria.Contains("requiere soporte humano") ||
            (hasMultipleImpactCriteria && ContainsMultipleImpactSignal(text));
    }

    private static bool ContainsMultipleImpactSignal(string text)
    {
        var normalized = Normalize(text);
        var signals = new[]
        {
            "varios socios",
            "multiples socios",
            "muchos socios",
            "varios usuarios",
            "multiples usuarios",
            "muchos usuarios",
            "otros socios",
            "otros usuarios",
            "le paso a varios",
            "le pasa a varios",
            "les paso lo mismo",
            "les pasa lo mismo",
            "paso lo mismo",
            "afecta a varios",
            "afecta a multiples"
        };

        return signals.Any(normalized.Contains);
    }

    private static bool HasTurnDetails(string text)
    {
        var normalized = Normalize(text);
        var hasTeacher =
            normalized.Contains("profesor") ||
            normalized.Contains("instructor") ||
            normalized.Contains("martin") ||
            normalized.Contains("valentina") ||
            normalized.Contains("camila");
        var hasDate =
            Regex.IsMatch(normalized, @"\b20\d{2}-\d{2}-\d{2}\b") ||
            Regex.IsMatch(normalized, @"\b\d{1,2}[/-]\d{1,2}[/-]20\d{2}\b");
        var hasTime = Regex.IsMatch(normalized, @"\b([01]?\d|2[0-3])[:.](\d{2})\b");

        return hasTeacher && hasDate && hasTime;
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

public static class AgentCaseMemory
{
    private const int MaxCasesPerFolder = 30;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public sealed record Case(
        string Ticket,
        string System,
        string Agent,
        string Outcome,
        string Summary,
        string? ArticleCode,
        string Decision,
        string? Reason,
        string? RecommendedAction,
        DateTime CreatedAtUtc);

    public sealed record Match(Case Case, int Score);

    public static Match? FindBestMatch(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return LoadCases()
            .Select(item => new Match(item, Score(item, normalized)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Case.CreatedAtUtc)
            .FirstOrDefault();
    }

    public static void Save(
        string ticket,
        string? system,
        string agent,
        string outcome,
        string summary,
        string? articleCode,
        string decision,
        string? reason,
        string? recommendedAction,
        Match? memoryMatch)
    {
        if (string.IsNullOrWhiteSpace(system))
            return;

        var normalizedSystem = Normalize(system);
        var caseItem = new Case(
            Ticket: string.IsNullOrWhiteSpace(ticket) ? $"CASE-{DateTime.UtcNow:yyyyMMddHHmmss}" : ticket,
            System: normalizedSystem,
            Agent: agent,
            Outcome: outcome,
            Summary: summary,
            ArticleCode: articleCode,
            Decision: decision,
            Reason: reason,
            RecommendedAction: recommendedAction,
            CreatedAtUtc: DateTime.UtcNow);

        var directory = Path.Combine(GetMemoryRoot(), normalizedSystem, outcome);
        Directory.CreateDirectory(directory);

        var fileName = $"{SanitizeFileName(caseItem.Ticket)}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        File.WriteAllText(Path.Combine(directory, fileName), JsonSerializer.Serialize(caseItem, JsonOptions));
        TrimFolder(directory);
    }

    private static IEnumerable<Case> LoadCases()
    {
        var root = GetMemoryRoot();
        if (!Directory.Exists(root))
            return [];

        return Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(300)
            .Select(ReadCase)
            .Where(item => item is not null)!;
    }

    private static Case? ReadCase(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Case>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static int Score(Case caseItem, string normalizedText)
    {
        var score = 0;
        var memoryText = Normalize($"{caseItem.System} {caseItem.Summary} {caseItem.Reason} {caseItem.RecommendedAction}");
        foreach (var token in normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
        {
            if (token.Length < 4)
                continue;

            if (memoryText.Contains(token))
                score += 3;
        }

        if (normalizedText.Contains(caseItem.System))
            score += 8;

        return score;
    }

    private static void TrimFolder(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(MaxCasesPerFolder))
        {
            File.Delete(file);
        }
    }

    private static string GetMemoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTAI_MEMORY_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AgentMemory"));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(invalid.Contains(character) ? '_' : character);

        return builder.ToString();
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

public sealed class AgentPromptLoader
{
    public string Load(string fileName)
    {
        var root = GetPromptRoot();
        var path = Path.Combine(root, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No existe el prompt {fileName} en {root}.", path);

        return File.ReadAllText(path, Encoding.UTF8).Trim();
    }

    private static string GetPromptRoot()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTAI_PROMPTS_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Prompts"));
    }
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

