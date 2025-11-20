namespace CopilotKitChatDemo.Api.Options;

/// <summary>
/// Configuration settings for OpenAI integration.
/// </summary>
public class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public string? ApiKey { get; set; }

    public string? Model { get; set; }
}
