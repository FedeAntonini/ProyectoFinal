namespace McpServer.Api.AgentStep.Dto;
public record UpdateAgentStepRequest(
    string Status,
    string OutputData
);