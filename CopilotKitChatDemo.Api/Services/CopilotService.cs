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
    private const string DefaultModel = "gpt-4o";

    private readonly OpenAIClient _client;
    private readonly string _model;
    private readonly ILogger<CopilotService> _logger;
    private readonly McpClient _mcpClient;

    public CopilotService(IOptions<OpenAIOptions> openAiOptions, ILogger<CopilotService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _mcpClient = new McpClient(); // In a real app, inject this via DI

        var configuredApiKey = openAiOptions?.Value?.ApiKey
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

        // Inject system prompt to enforce tool usage
        if (!chatMessages.Any(m => m is SystemChatMessage))
        {
            chatMessages.Insert(0, new SystemChatMessage("You are a helpful assistant. You have access to tools. You MUST use the tools when the user asks for information that can be retrieved by them (like weather). Do not just say you will do it; actually call the tool."));
        }

        var chatClient = _client.GetChatClient(_model);
        var tools = _mcpClient.GetTools().ToList();
        var options = new ChatCompletionOptions();
        foreach (var tool in tools)
        {
            options.Tools.Add(tool);
        }

        bool keepIterating = true;
        while (keepIterating)
        {
            keepIterating = false;
            var completionUpdates = chatClient.CompleteChatStreamingAsync(chatMessages, options, cancellationToken);
            
            var toolCallBuffer = new Dictionary<int, (string Name, StringBuilder Arguments)>();
            var currentToolCallIndex = -1;

            await foreach (var update in completionUpdates)
            {
                // Handle Tool Calls
                if (update.ToolCallUpdates.Count > 0)
                {
                    _logger.LogInformation("Received ToolCallUpdates: {Count}", update.ToolCallUpdates.Count);
                    foreach (var toolUpdate in update.ToolCallUpdates)
                    {
                        if (toolUpdate.Index >= 0)
                        {
                            currentToolCallIndex = toolUpdate.Index;
                            if (!toolCallBuffer.ContainsKey(currentToolCallIndex))
                            {
                                toolCallBuffer[currentToolCallIndex] = (toolUpdate.FunctionName, new StringBuilder());
                                _logger.LogInformation("New Tool Call: Index={Index}, Name={Name}", currentToolCallIndex, toolUpdate.FunctionName);
                            }
                        }
                        
                        if (toolUpdate.FunctionArgumentsUpdate != null && currentToolCallIndex >= 0)
                        {
                            toolCallBuffer[currentToolCallIndex].Arguments.Append(toolUpdate.FunctionArgumentsUpdate);
                        }
                    }
                    continue;
                }

                // Handle Content
                if (update.ContentUpdate.Count > 0)
                {
                    var deltaBuilder = new StringBuilder();
                    foreach (var contentPart in update.ContentUpdate)
                    {
                        if (!string.IsNullOrEmpty(contentPart.Text))
                        {
                            deltaBuilder.Append(contentPart.Text);
                        }
                    }

                    var delta = deltaBuilder.ToString();
                    if (!string.IsNullOrEmpty(delta))
                    {
                        _logger.LogInformation("Received Content: {Content}", delta);
                        var payloads = JsonSerializer.Serialize(new
                        {
                            type = "response-text",
                            content = delta,
                        });
                        yield return new StreamingChunk(payloads);
                    }
                }
            }

            // Process accumulated tool calls
            if (toolCallBuffer.Count > 0)
            {
                var toolCalls = new List<ChatToolCall>();
                foreach (var kvp in toolCallBuffer)
                {
                    var toolCallId = Guid.NewGuid().ToString(); // OpenAI .NET SDK might not expose ID in streaming easily, generating one or need to capture it if available. 
                    // Actually, for the purpose of the conversation history, we need to append the Assistant's tool call message.
                    // But since we are streaming, we might need to reconstruct the Assistant message.
                    
                    var functionName = kvp.Value.Name;
                    var arguments = kvp.Value.Arguments.ToString();
                    
                    // Execute Tool
                    string result = string.Empty;
                    try 
                    {
                        result = await _mcpClient.ExecuteToolAsync(functionName, arguments);
                    }
                    catch (Exception ex)
                    {
                        result = JsonSerializer.Serialize(new { error = ex.Message });
                    }

                    // Send intermediate result to client if needed, or just feed back to LLM.
                    // CopilotKit expects us to handle the loop.
                    
                    // 1. Add Assistant Message with Tool Calls to history
                    chatMessages.Add(new AssistantChatMessage(new[] { ChatToolCall.CreateFunctionToolCall(toolCallId, functionName, BinaryData.FromString(arguments)) }));

                    // 2. Add Tool Message to history
                    chatMessages.Add(new ToolChatMessage(toolCallId, result));
                }
                
                // Loop back to get the next response from the LLM based on the tool outputs
                keepIterating = true;
            }
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

        // Inject system prompt to enforce tool usage
        if (!chatMessages.Any(m => m is SystemChatMessage))
        {
            chatMessages.Insert(0, new SystemChatMessage("You are a helpful assistant. You have access to tools. Use them to retrieve information when necessary. Once you have the information, provide a detailed natural language response to the user. Do not attempt to display any UI cards."));
        }

        var chatClient = _client.GetChatClient(_model);
        var tools = _mcpClient.GetTools().ToList();
        var options = new ChatCompletionOptions();
        foreach (var tool in tools)
        {
            options.Tools.Add(tool);
        }

        string assistantText = string.Empty;
        bool keepIterating = true;
        int maxIterations = 10; // Safety break
        int iteration = 0;

        // Track new messages generated during this turn
        var newMessages = new List<object>();

        while (keepIterating && iteration < maxIterations)
        {
            iteration++;
            keepIterating = false;
            
            OpenAI.Chat.ChatCompletion completion;
            try
            {
                completion = await chatClient.CompleteChatAsync(chatMessages, options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI GraphQL generate request failed.");
                return BuildAssistantErrorGraphQLResponse($"OpenAI request failed: {ex.Message}");
            }

            if (completion.ToolCalls != null && completion.ToolCalls.Count > 0)
            {
                _logger.LogInformation("GraphQL: Received {Count} tool calls.", completion.ToolCalls.Count);
                chatMessages.Add(new AssistantChatMessage(completion.ToolCalls));

                // Add ActionExecutionMessageOutput for tool calls
                foreach (var tc in completion.ToolCalls)
                {
                    var argsObject = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.FunctionArguments.ToString());
                    newMessages.Add(new
                    {
                        __typename = "ActionExecutionMessageOutput",
                        id = tc.Id,
                        createdAt = DateTimeOffset.UtcNow,
                        name = tc.FunctionName,
                        arguments = argsObject, // Send as dictionary
                        scope = "server", // Executed on server
                        role = "assistant",
                        parentMessageId = (string?)null,
                        status = new { __typename = "SuccessMessageStatus", code = "Success" }
                    });
                }

                foreach (var toolCall in completion.ToolCalls)
                {
                    string result = string.Empty;
                    try
                    {
                        _logger.LogInformation("GraphQL: Executing tool {Name} with args {Args}. ID={Id}", toolCall.FunctionName, toolCall.FunctionArguments, toolCall.Id);
                        result = await _mcpClient.ExecuteToolAsync(toolCall.FunctionName, toolCall.FunctionArguments.ToString());
                        _logger.LogInformation("GraphQL: Tool result: {Result}", result);
                    }
                    catch (Exception ex)
                    {
                        result = JsonSerializer.Serialize(new { error = ex.Message });
                        _logger.LogError(ex, "GraphQL: Tool execution failed.");
                    }

                    chatMessages.Add(new ToolChatMessage(toolCall.Id, result));

                    // Add ResultMessageOutput for tool results
                    newMessages.Add(new
                    {
                        __typename = "ResultMessageOutput",
                        id = Guid.NewGuid().ToString(),
                        createdAt = DateTimeOffset.UtcNow,
                        actionExecutionId = toolCall.Id,
                        actionName = toolCall.FunctionName,
                        result = result,
                        role = "tool", // or "function"
                        parentMessageId = (string?)null,
                        status = new { __typename = "SuccessMessageStatus", code = "Success" }
                    });
                }
                keepIterating = true;
            }
            else
            {
                assistantText = completion.Content[0].Text ?? string.Empty;
                _logger.LogInformation("GraphQL: Received text response: {Text}", assistantText);
                
                // Add TextMessageOutput for final response
                newMessages.Add(new
                {
                    __typename = "TextMessageOutput",
                    id = Guid.NewGuid().ToString(),
                    createdAt = DateTimeOffset.UtcNow,
                    content = new[] { assistantText },
                    role = "assistant",
                    parentMessageId = (string?)null,
                    status = new { __typename = "SuccessMessageStatus", code = "Success" }
                });
            }
        }

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
                    messages = newMessages.ToArray(),
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
