using MulticastGame.Server;

var builder = WebApplication.CreateBuilder(args);

// Allow connections from any machine on the LAN
builder.WebHost.UseUrls("http://0.0.0.0:7777");

// Add SignalR services
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 102400; // 100 KB
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
});

// Allow cross-origin requests (needed for Unity WebGL builds, harmless for desktop)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();

// Map the SignalR hub to /gameHub
app.MapHub<GameHub>("/gameHub");

// Simple health-check endpoint — visit http://<ip>:7777/health in browser to confirm server is up
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow,
    message = "MulticastGame SignalR server is running"
}));

Console.WriteLine("==============================================");
Console.WriteLine("  MulticastGame SignalR Server");
Console.WriteLine("  Listening on http://0.0.0.0:7777");
Console.WriteLine("  Hub endpoint: /gameHub");
Console.WriteLine("  Health check: http://localhost:7777/health");
Console.WriteLine("==============================================");
Console.WriteLine("  Waiting for Unity clients to connect...");
Console.WriteLine();

app.Run();