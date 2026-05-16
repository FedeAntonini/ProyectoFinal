namespace McpServer.MessageQueue;

public enum MessageAction
{
    LlmRequest
}

public static class MessageActionExtensions
{
    public static string ToValue(this MessageAction action) => action switch
    {
        MessageAction.LlmRequest => "llm_request",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}