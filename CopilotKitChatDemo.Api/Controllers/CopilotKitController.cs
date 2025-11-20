// CopilotKitChatDemo.Api/Controllers/CopilotKitController.cs
// Exposes the /copilotkit endpoint that CopilotKit calls instead of the default Node runtime.
// It forwards chat messages to OpenAI using the official .NET SDK and streams the tokens back
// as Server-Sent Events (SSE), which CopilotKit can consume.

using System.Text;
using System.Text.Json;
using CopilotKitChatDemo.Api.Models;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Chat;

namespace CopilotKitChatDemo.Api.Controllers;

[ApiController]
[Route("copilotkit")]
public class CopilotKitController : ControllerBase
{
    private readonly OpenAIClient _client;
    private readonly string _model;

    public CopilotKitController()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4.1-mini";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Missing OPENAI_API_KEY. Set it as an environment variable before running the API.");
        }

        _client = new OpenAIClient(apiKey);
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] CopilotKitRequest request)
    {
        if (request?.Messages is null || request.Messages.Count == 0)
        {
            return BadRequest(new { error = "The request must include chat messages." });
        }

        // Translate incoming CopilotKit messages to OpenAI SDK chat messages.
        var chatMessages = request.Messages.Select(msg => msg.Role.ToLowerInvariant() switch
        {
            "system" => new SystemChatMessage(msg.Content),
            "assistant" => new AssistantChatMessage(msg.Content),
            _ => new UserChatMessage(msg.Content),
        }).ToList();

        var chatClient = _client.GetChatClient(_model);

        // Stream the response as SSE so the Copilot UI can render tokens as they arrive.
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");

        await foreach (var update in chatClient.CompleteChatStreamingAsync(chatMessages))
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

            // CopilotKit expects JSON payloads; we wrap each token chunk and flush it.
            var payload = JsonSerializer.Serialize(new
            {
                type = "response-text",
                content = delta,
            });

            await Response.WriteAsync($"data: {payload}\n\n");
            await Response.Body.FlushAsync();
        }

        // Signal completion to the SSE consumer.
        await Response.WriteAsync("data: [DONE]\n\n");
        await Response.Body.FlushAsync();

        return new EmptyResult();
    }
}
