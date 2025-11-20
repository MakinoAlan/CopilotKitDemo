// CopilotKitChatDemo.Api/Program.cs
// Minimal ASP.NET Core bootstrapping for the CopilotKit runtime endpoint.

var builder = WebApplication.CreateBuilder(args);

// Allow the Next.js dev server to call this API.
const string CorsPolicy = "AllowFrontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors(CorsPolicy);
app.MapControllers();

app.Run();
