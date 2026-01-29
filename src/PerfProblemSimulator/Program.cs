using PerfProblemSimulator.Hubs;
using PerfProblemSimulator.Middleware;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

// =============================================================================
// Performance Problem Simulator - Application Entry Point
// =============================================================================
// This application intentionally creates performance problems for educational
// purposes. It allows users to trigger and observe:
// - High CPU usage (spin loops)
// - Memory pressure (large allocations)
// - Thread pool starvation (sync-over-async anti-patterns)
//
// WARNING: This application should ONLY be used in controlled environments
// for learning and demonstration purposes. Do not deploy to production
// without setting DISABLE_PROBLEM_ENDPOINTS=true.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------------
// Bind the ProblemSimulator section from appsettings.json to strongly-typed options.
// This allows services to inject IOptions<ProblemSimulatorOptions> to access config values.
builder.Services.Configure<ProblemSimulatorOptions>(
    builder.Configuration.GetSection(ProblemSimulatorOptions.SectionName));

// -----------------------------------------------------------------------------
// Core Services
// -----------------------------------------------------------------------------
// Add MVC controllers with JSON formatting
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names (REST API convention)
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;

        // Serialize enums as strings for better readability in API responses
        // Educational Note: This makes the JSON output more human-readable.
        // Without this, SimulationType.Cpu would serialize as 0 instead of "Cpu".
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        // Use camelCase for dictionary keys as well
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Add SignalR for real-time dashboard updates
// Educational Note: SignalR provides WebSocket-based real-time communication,
// which is essential for showing live metrics on the dashboard.
builder.Services.AddSignalR();

// Add API documentation with Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Performance Problem Simulator API",
        Version = "v1",
        Description = """
            An educational tool for demonstrating and diagnosing Azure App Service performance problems.
            
            ⚠️ WARNING: This API intentionally creates performance problems. Use only in controlled environments.
            
            ## Simulation Types
            - **CPU**: Triggers high CPU usage through parallel spin loops
            - **Memory**: Allocates and holds memory to create memory pressure
            - **ThreadBlock**: Simulates thread pool starvation via sync-over-async patterns
            
            ## Safety Features
            - All operations have configurable limits (duration, memory size, etc.)
            - Problem endpoints can be disabled via DISABLE_PROBLEM_ENDPOINTS environment variable
            - Health endpoints remain responsive even under stress
            """
    });
});

// -----------------------------------------------------------------------------
// Application Services
// -----------------------------------------------------------------------------
// SimulationTracker - Singleton service that tracks all active simulations
// Educational Note: Singleton lifetime ensures all parts of the application
// see the same simulation state. The ConcurrentDictionary inside provides
// thread-safe access without explicit locking.
builder.Services.AddSingleton<ISimulationTracker, SimulationTracker>();

// CpuStressService - Transient service for triggering CPU stress simulations
// Educational Note: Transient lifetime means a new instance is created for each
// request. This is appropriate because the service doesn't maintain state between
// requests (state is managed by the singleton SimulationTracker).
builder.Services.AddTransient<ICpuStressService, CpuStressService>();

// MemoryPressureService - Singleton service for memory allocation simulations
// Educational Note: Singleton lifetime is required here because the service
// maintains a list of allocated memory blocks that must persist across requests.
// The allocated memory must remain referenced to demonstrate memory pressure.
builder.Services.AddSingleton<IMemoryPressureService, MemoryPressureService>();

// ThreadBlockService - Transient service for sync-over-async thread starvation
// Educational Note: Transient lifetime is appropriate because each request gets
// its own simulation, and state is tracked by the singleton SimulationTracker.
builder.Services.AddTransient<IThreadBlockService, ThreadBlockService>();

// MetricsCollector - Singleton service for collecting system metrics
// Educational Note: This service runs on a DEDICATED THREAD (not the thread pool)
// so it remains responsive even during thread pool starvation scenarios.
// This is critical for FR-013 - health endpoints must work under stress.
builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();

// MetricsBroadcastService - Hosted service that broadcasts metrics to SignalR clients
// Educational Note: IHostedService provides proper startup/shutdown lifecycle management.
// This bridges the MetricsCollector (which fires events) with SignalR (which pushes to clients).
builder.Services.AddHostedService<MetricsBroadcastService>();

// -----------------------------------------------------------------------------
// CORS Configuration
// -----------------------------------------------------------------------------
// Allow any origin for development. In production, you would restrict this
// to specific domains. CORS is needed when the SPA is served from a different
// origin during development.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// -----------------------------------------------------------------------------
// Middleware Pipeline
// -----------------------------------------------------------------------------
// The order of middleware is important:
// 1. Error handling (wraps everything)
// 2. CORS (must be before routing)
// 3. Static files (for SPA)
// 4. Problem endpoint guard (before routing to block disabled endpoints)
// 5. Routing
// 6. Endpoints

// Development-only middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Performance Problem Simulator API";
    });
}

// CORS must be called before UseRouting
app.UseCors();

// Serve static files from wwwroot (for the SPA dashboard)
app.UseDefaultFiles(); // Enables default document (index.html)
app.UseStaticFiles();

// Problem endpoint guard - blocks trigger/allocate/release endpoints when disabled
// Educational Note: This middleware demonstrates the "kill switch" pattern for
// safely deploying code that has potentially dangerous functionality.
app.UseProblemEndpointGuard();

// HTTPS redirection (commented out for local development convenience)
// app.UseHttpsRedirection();

// Map controller routes
app.MapControllers();

// Map SignalR hub for real-time metrics
// Educational Note: The hub path "/hubs/metrics" is where the SignalR client connects.
// SignalR automatically handles WebSocket connections with fallback to SSE or Long Polling.
app.MapHub<MetricsHub>("/hubs/metrics");

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Performance Problem Simulator starting...");
logger.LogInformation(
    "Problem endpoints are {Status}",
    Environment.GetEnvironmentVariable("DISABLE_PROBLEM_ENDPOINTS")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
        ? "DISABLED"
        : "ENABLED");

app.Run();

// =============================================================================
// Make Program class accessible for integration testing
// =============================================================================
// The WebApplicationFactory<T> used in integration tests needs access to the
// Program class. Since top-level statements generate an implicit Program class
// that is internal, we need to make it public for the test project to access.
// =============================================================================
public partial class Program { }
