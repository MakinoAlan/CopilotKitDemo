// CopilotKitChatDemo.Api/Models/CopilotKitRequest.cs
// Minimal shape for CopilotKit chat payload. The frontend sends the same
// structure it would normally post to the @copilotkit/backend runtime.
using System.Text.Json.Serialization;
using System.Text.Json;

namespace CopilotKitChatDemo.Api.Models;

public record CopilotKitMessage(
    [property: JsonPropertyName("role")] string Role,
    // CopilotKit may send either a string or an array of content parts; keep as JsonElement and normalize later.
    [property: JsonPropertyName("content")] JsonElement Content
);

public record CopilotKitRequest(
    [property: JsonPropertyName("messages")] List<CopilotKitMessage> Messages
);
