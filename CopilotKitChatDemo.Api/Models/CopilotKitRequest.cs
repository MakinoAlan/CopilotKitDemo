// CopilotKitChatDemo.Api/Models/CopilotKitRequest.cs
// Minimal shape for CopilotKit chat payload. The frontend sends the same
// structure it would normally post to the @copilotkit/backend runtime.
using System.Text.Json.Serialization;

namespace CopilotKitChatDemo.Api.Models;

public record CopilotKitMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);

public record CopilotKitRequest(
    [property: JsonPropertyName("messages")] List<CopilotKitMessage> Messages
);
