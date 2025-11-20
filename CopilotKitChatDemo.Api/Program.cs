// CopilotKitChatDemo.Api/Program.cs
// Minimal ASP.NET Core bootstrapping for the CopilotKit runtime endpoint.

using CopilotKitChatDemo.Api.Options;
using CopilotKitChatDemo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Allow the Next.js dev server to call this API.
const string CorsPolicy = "AllowFrontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddControllers();
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection(OpenAIOptions.SectionName));
builder.Services.AddScoped<ICopilotService, CopilotService>();

var app = builder.Build();

app.UseRouting();
app.UseCors(CorsPolicy);
app.MapControllers();

app.Run();
