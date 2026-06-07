using McpServer.MessageQueue;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpServer.Tools;

[McpServerToolType]
public class OutboundMessageTools
{
    private readonly OutboundQueueService _outboundQueueService;

    public OutboundMessageTools(OutboundQueueService outboundQueueService)
    {
        _outboundQueueService = outboundQueueService;
    }

    [McpServerTool, Description("Sends a message to the outbound queue. Use action 'send_message' for bot replies and 'escalate' for level 2 escalations.")]
    public async Task SendOutboundMessage(
        [Description("The ticket ID this message belongs to")]
        string ticketId,
        [Description("The correlation ID from the original inbound message")]
        string correlationId,
        [Description("The customer ID from the original inbound message")]
        string customerId,
        [Description("The action to perform: 'send_message' or 'escalate'")]
        string action,
        [Description("The message payload to send to the user")]
        string payload,
        [Description("The agent that sends this message")]
        string targetAgent,
        CancellationToken ct = default)
    {
        await _outboundQueueService.SendAsync(new OutboundMessage(
            TicketId: ticketId,
            CorrelationId: correlationId,
            CustomerId: customerId,
            TargetAgent: targetAgent,
            Action: action,
            Payload: payload
        ), ct);
    }
}