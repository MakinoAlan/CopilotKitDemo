// CopilotKitChatDemo.Api/Controllers/CopilotKitController.cs
// Exposes the /copilotkit endpoint that CopilotKit calls instead of the default Node runtime.
// It forwards chat messages to OpenAI using the official .NET SDK and streams the tokens back
// as Server-Sent Events (SSE), which CopilotKit can consume.

using System.Text.Json;
using CopilotKitChatDemo.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace CopilotKitChatDemo.Api.Controllers;

[ApiController]
[Route("copilotkit")]
public class CopilotKitController : ControllerBase
{
    private readonly ICopilotService _copilotService;
    private readonly ILogger<CopilotKitController> _logger;

    public CopilotKitController(ICopilotService copilotService, ILogger<CopilotKitController> logger)
    {
        _copilotService = copilotService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var graphQlResponse = await _copilotService.HandleGraphQLAsync(payload, cancellationToken);
        if (graphQlResponse is not null)
        {
            return Ok(graphQlResponse);
        }

        var messages = _copilotService.ExtractMessages(payload);
        if (messages.Count == 0)
        {
            return BadRequest(new { error = "The request must include chat messages." });
        }

        // Stream the response as SSE so the Copilot UI can render tokens as they arrive.
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");

        try
        {
            await foreach (var update in _copilotService.StreamChatAsync(messages, cancellationToken))
            {
                await Response.WriteAsync($"data: {update.Payload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI streaming request failed.");
            var errorPayload = JsonSerializer.Serialize(new
            {
                type = "error",
                content = "OpenAI request failed: " + ex.Message,
            });

            await Response.WriteAsync($"data: {errorPayload}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return StatusCode((int)HttpStatusCode.TooManyRequests, new { error = ex.Message });
        }

        // Signal completion to the SSE consumer.
        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        return new EmptyResult();
    }
}
