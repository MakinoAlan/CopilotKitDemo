using System.Text.Json;
using OpenAI.Chat;

namespace CopilotKitChatDemo.Api.Services;

public class McpClient
{
    // In a real implementation, this would connect to an MCP server via Stdio or SSE.
    // For this demo, we will register local tools and simulate MCP capabilities.

    private readonly List<ChatTool> _tools = new();
    private readonly Dictionary<string, Func<string, Task<string>>> _toolExecutors = new();

    public McpClient()
    {
        // Register a default weather tool to simulate an MCP tool
        RegisterTool(
            ChatTool.CreateFunctionTool(
                "get_weather",
                "Get the current weather for a location",
                BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        location = new
                        {
                            type = "string",
                            description = "The city and state, e.g. San Francisco, CA"
                        }
                    },
                    required = new[] { "location" }
                })
            ),
            async (argumentsJson) =>
            {
                var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
                var location = args.GetProperty("location").GetString();
                
                // Mock response
                await Task.Delay(100); // Simulate network
                return JsonSerializer.Serialize(new
                {
                    location = location,
                    temperature = 72,
                    conditions = "Sunny",
                    wind_speed = "10 mph",
                    humidity = "45%"
                });
            }
        );
    }

    public void RegisterTool(ChatTool tool, Func<string, Task<string>> executor)
    {
        _tools.Add(tool);
        _toolExecutors[tool.FunctionName] = executor;
    }

    public IEnumerable<ChatTool> GetTools()
    {
        return _tools;
    }

    public async Task<string> ExecuteToolAsync(string functionName, string arguments)
    {
        if (_toolExecutors.TryGetValue(functionName, out var executor))
        {
            return await executor(arguments);
        }
        throw new InvalidOperationException($"Tool {functionName} not found.");
    }
}
