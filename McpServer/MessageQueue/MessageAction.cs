namespace McpServer.MessageQueue;

public static class InboundAction
{
    public const string NewTicket = "new_ticket";
    public const string UserMessage = "user_message";
    public const string TicketParaEnrutar = "ticket_para_enrutar";
    public const string TicketParaEjecutar = "ticket_para_ejecutar";
    public const string NotificarResolucion = "notificar_resolucion";
    public const string AgenteEnrutador = "agente_enrutador";
}

public static class OutboundAction
{
    public const string LlmRequest = "llm_request";
}