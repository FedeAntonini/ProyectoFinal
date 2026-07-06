using System.Globalization;
using System.Text;

namespace McpServer.Api.Kb;

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

    public static List<KnowledgeBaseSearchResult> GetAllArticles()
    {
        var articles = CachedArticles ??= Load();
        return articles.ToList();
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
}