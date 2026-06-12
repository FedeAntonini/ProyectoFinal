namespace McpServer.Services;

public sealed class AgentPromptLoader
{
    public string Load(string fileName)
    {
        var root = GetPromptRoot();
        var path = Path.Combine(root, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No existe el prompt {fileName} en {root}.", path);

        return File.ReadAllText(path, System.Text.Encoding.UTF8).Trim();
    }

    private static string GetPromptRoot()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTAI_PROMPTS_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Prompts"));
    }
}