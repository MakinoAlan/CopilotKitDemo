using System.Text.Json;
using CopilotKitChatDemo.Api.Models;

namespace CopilotKitChatDemo.Api.Services;

public interface ICopilotService
{
    Task<object?> HandleGraphQLAsync(JsonElement payload, CancellationToken cancellationToken = default);

    List<CopilotKitMessage> ExtractMessages(JsonElement payload);

    IAsyncEnumerable<StreamingChunk> StreamChatAsync(IEnumerable<CopilotKitMessage> messages, CancellationToken cancellationToken = default);
}
