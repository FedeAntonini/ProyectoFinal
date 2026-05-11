namespace McpServer.MessageQueue;

public record InboundMessage(
    string TicketId,
    string CorrelationId,
    string CustomerId,
    Dictionary<string, string> Metadata
);
public record OutboundMessage(
    string TicketId,
    string CorrelationId,
    bool Found,
    string? TargetAgent,
    string? Action,
    string? Payload
);

public record EntradaResult(bool IsAccepted, InboundMessage Message, string? RejectionReason)
{
    public static EntradaResult Accepted(InboundMessage message) =>
        new(true, message, null);

    public static EntradaResult Rejected(InboundMessage message, string reason) =>
        new(false, message, reason);
}