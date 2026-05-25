using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServer.Services;

public interface IKnowledgeBaseApiService
{
    Task<DiagnosticarApiResponse?> DiagnosticarAsync(string sistema, string descripcion, CancellationToken ct = default);
    Task<IReadOnlyList<KbSearchResult>> SearchAsync(string descripcion, string sistema, CancellationToken ct = default);
}

public sealed class KnowledgeBaseApiService(HttpClient httpClient) : IKnowledgeBaseApiService
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public async Task<DiagnosticarApiResponse?> DiagnosticarAsync(string sistema, string descripcion, CancellationToken ct = default)
    {
        var payload = new { sistema, descripcion };
        using var response = await httpClient.PostAsJsonAsync("knowledge-base/diagnosticar", payload, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiagnosticarApiResponse>(_json, ct);
    }

    public async Task<IReadOnlyList<KbSearchResult>> SearchAsync(string descripcion, string sistema, CancellationToken ct = default)
    {
        var url = $"knowledge-base/search?query={Uri.EscapeDataString(descripcion)}&system={Uri.EscapeDataString(sistema)}&limit=3";
        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<KbSearchResult>>(_json, ct) ?? [];
    }
}

public sealed record DiagnosticarApiResponse(
    bool PuedoResolver,
    string Decision,
    string MensajeSugerido,
    string CriteriosEscalacion,
    string AccionesRecomendadas,
    string Confianza,
    int? ArticleId,
    string? ArticleCode
);

public sealed record KbSearchResult(
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
    string Confidence
);
