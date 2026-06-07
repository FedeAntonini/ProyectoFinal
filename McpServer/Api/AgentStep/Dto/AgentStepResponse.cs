namespace McpServer.Api.AgentStep.Dto;
public record AgentStepResponse(
    int Id,
    int AgentRunId,
    string AgentType,
    string InputData,
    string OutputData,
    string Status,
    DateTime CreatedAt
);