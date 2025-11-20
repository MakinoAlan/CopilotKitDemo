using System.Net;
using System.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CopilotKitChatDemo.Api.Models;
using CopilotKitChatDemo.Api.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace CopilotKitChatDemo.Api.Services;

public class CopilotService : ICopilotService
{
    private const string DefaultModel = "gpt-4.1-mini";

    private readonly OpenAIClient _client;
    private readonly string _model;
    private readonly ILogger<CopilotService> _logger;

    public CopilotService(IOptions<OpenAIOptions> openAiOptions, ILogger<CopilotService> logger, IConfiguration configuration)
    {
        _logger = logger;
        var configuredApiKey = openAiOptions.Value.ApiKey
                               ?? configuration["OPENAI_API_KEY"]
                               ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        _model = openAiOptions.Value.Model
                 ?? configuration["OPENAI_MODEL"]
                 ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
                 ?? DefaultModel;

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            throw new InvalidOperationException(
                "Missing OPENAI_API_KEY. Set it as an environment variable before running the API.");
        }

        _client = new OpenAIClient(configuredApiKey);
    }

    public Task<object?> HandleGraphQLAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("query", out var queryElement))
        {
            return Task.FromResult<object?>(null);
        }

        var query = queryElement.GetString() ?? string.Empty;
        if (query.Contains("availableAgents", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<object?>(new
            {
                data = new
                {
                    availableAgents = new
                    {
                        agents = Array.Empty<object>(),
                    }
                }
            });
        }

        if (query.Contains("loadAgentState", StringComparison.OrdinalIgnoreCase))
        {
            var variables = payload.TryGetProperty("variables", out var varsElement) ? varsElement : default;
            var threadId = variables.ValueKind == JsonValueKind.Object &&
                           variables.TryGetProperty("data", out var dataElement) &&
                           dataElement.TryGetProperty("threadId", out var threadIdElement)
                ? threadIdElement.GetString() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();

            return Task.FromResult<object?>(new
            {
                data = new
                {
                    loadAgentState = new
                    {
                        threadId,
                        threadExists = false,
                        state = string.Empty,
                        messages = string.Empty,
                    }
                }
            });
        }

        if (query.Contains("generateCopilotResponse", StringComparison.OrdinalIgnoreCase))
        {
            return HandleGraphQLGenerateAsync(payload, cancellationToken);
        }

        return Task.FromResult<object?>(null);
    }

    public List<CopilotKitMessage> ExtractMessages(JsonElement payload)
    {
        var results = new List<CopilotKitMessage>();

        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("messages", out var messagesElement) ||
            messagesElement.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var msgElement in messagesElement.EnumerateArray())
        {
            if (msgElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var role = msgElement.TryGetProperty("role", out var roleElement)
                ? roleElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(role))
            {
                role = "user";
            }

            var content = msgElement.TryGetProperty("content", out var contentElement)
                ? contentElement
                : default;

            results.Add(new CopilotKitMessage(role!, content));
        }

        return results;
    }

    public async IAsyncEnumerable<StreamingChunk> StreamChatAsync(IEnumerable<CopilotKitMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatMessages = messages.Select(msg =>
        {
            var content = NormalizeContent(msg.Content);
            return msg.Role.ToLowerInvariant() switch
            {
                "system" => (ChatMessage)new SystemChatMessage(content),
                "assistant" => new AssistantChatMessage(content),
                _ => new UserChatMessage(content),
            };
        }).ToList();

        var chatClient = _client.GetChatClient(_model);

        await foreach (var update in chatClient.CompleteChatStreamingAsync(chatMessages, options: null, cancellationToken))
        {
            if (update.ContentUpdate is null)
            {
                continue;
            }

            var deltaBuilder = new StringBuilder();

            foreach (var contentPart in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(contentPart.Text))
                {
                    deltaBuilder.Append(contentPart.Text);
                }
            }

            var delta = deltaBuilder.ToString();
            if (string.IsNullOrEmpty(delta))
            {
                continue;
            }

            var payloads = JsonSerializer.Serialize(new
            {
                type = "response-text",
                content = delta,
            });

            yield return new StreamingChunk(payloads);
        }
    }

    private async Task<object?> HandleGraphQLGenerateAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var variables = payload.TryGetProperty("variables", out var varsElement) ? varsElement : default;
        var dataElement = variables.ValueKind == JsonValueKind.Object && variables.TryGetProperty("data", out var d)
            ? d
            : default;
        var requestedThreadId = dataElement.ValueKind == JsonValueKind.Object &&
                                dataElement.TryGetProperty("threadId", out var threadIdElement)
            ? threadIdElement.GetString()
            : null;

        var gqlMessages = new List<CopilotKitMessage>();
        if (dataElement.ValueKind == JsonValueKind.Object && dataElement.TryGetProperty("messages", out var msgsElement) && msgsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var msg in msgsElement.EnumerateArray())
            {
                if (msg.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (msg.TryGetProperty("textMessage", out var textMessageElement) && textMessageElement.ValueKind == JsonValueKind.Object)
                {
                    var role = textMessageElement.TryGetProperty("role", out var roleElement)
                        ? roleElement.GetString() ?? "user"
                        : "user";

                    if (textMessageElement.TryGetProperty("content", out var textContent) &&
                        textContent.ValueKind != JsonValueKind.Undefined)
                    {
                        gqlMessages.Add(new CopilotKitMessage(role, textContent));
                        continue;
                    }
                }

                var fallbackRole = msg.TryGetProperty("role", out var roleElementFallback)
                    ? roleElementFallback.GetString() ?? "user"
                    : "user";
                if (msg.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind != JsonValueKind.Undefined)
                {
                    gqlMessages.Add(new CopilotKitMessage(fallbackRole, contentElement));
                }
            }
        }

        if (gqlMessages.Count == 0)
        {
            throw new InvalidOperationException("Missing messages in generateCopilotResponse variables.");
        }

        var chatMessages = gqlMessages.Select(msg =>
        {
            var content = NormalizeContent(msg.Content);
            return msg.Role.ToLowerInvariant() switch
            {
                "system" => (ChatMessage)new SystemChatMessage(content),
                "assistant" => new AssistantChatMessage(content),
                _ => new UserChatMessage(content),
            };
        }).ToList();

        var chatClient = _client.GetChatClient(_model);
        OpenAI.Chat.ChatCompletion completion;
        try
        {
            completion = await chatClient.CompleteChatAsync(chatMessages, options: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI GraphQL generate request failed.");
            return BuildAssistantErrorGraphQLResponse($"OpenAI request failed: {ex.Message}");
        }
        var assistantText = completion.Content[0].Text ?? string.Empty;

        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid().ToString();
        var threadId = requestedThreadId ?? Guid.NewGuid().ToString();

        return new
        {
            data = new
            {
                generateCopilotResponse = new
                {
                    __typename = "CopilotResponse",
                    threadId,
                    runId = (string?)null,
                    extensions = (object?)null,
                    status = new
                    {
                        __typename = "SuccessResponseStatus",
                        code = "Success",
                    },
                    messages = new object[]
                    {
                        new
                        {
                            __typename = "TextMessageOutput",
                            id = messageId,
                            createdAt = now,
                            content = new[] { assistantText },
                            role = "assistant",
                            parentMessageId = (string?)null,
                            status = new
                            {
                                __typename = "SuccessMessageStatus",
                                code = "Success",
                            }
                        }
                    },
                    metaEvents = Array.Empty<object>()
                }
            }
        };
    }

    private static object BuildAssistantErrorGraphQLResponse(string message)
    {
        return new
        {
            data = new
            {
                generateCopilotResponse = new
                {
                    __typename = "CopilotResponse",
                    threadId = Guid.NewGuid().ToString(),
                    runId = (string?)null,
                    extensions = (object?)null,
                    status = new
                    {
                        __typename = "SuccessResponseStatus",
                        code = "Success",
                    },
                    messages = new object[]
                    {
                        new
                        {
                            __typename = "TextMessageOutput",
                            id = Guid.NewGuid().ToString(),
                            createdAt = DateTimeOffset.UtcNow,
                            content = new[] { message },
                            role = "assistant",
                            parentMessageId = (string?)null,
                            status = new
                            {
                                __typename = "SuccessMessageStatus",
                                code = "Success",
                            }
                        }
                    },
                    metaEvents = Array.Empty<object>()
                }
            }
        };
    }

    private static string NormalizeContent(JsonElement contentElement)
    {
        return contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(
                contentElement.EnumerateArray()
                    .Select(part => part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text)
                        ? text.GetString() ?? string.Empty
                        : part.GetString() ?? string.Empty)),
            JsonValueKind.Object => contentElement.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : contentElement.ToString(),
            _ => contentElement.ToString(),
        };
    }
}

public record StreamingChunk(string Payload, bool IsError = false, int? StatusCode = null, string? ErrorMessage = null);
