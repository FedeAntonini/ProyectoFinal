namespace McpServer.MessageQueue;

public static class InboundAction
{
    public const string NewTicket = "new_ticket";
    public const string UserMessage = "user_message";
}

public static class OutboundAction
{
    public const string LlmRequest = "llm_request";
}
