namespace McpServer.Api.AgentStep.Dto;

public record CreateAgentStepRequest(
    int AgentRunId,
    string AgentType,
    string InputData
);