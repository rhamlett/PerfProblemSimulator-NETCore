using PerfProblemSimulator.Hubs;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

#if NET8_0
using OpenApiInfo = Microsoft.OpenApi.Models.OpenApiInfo;
#else
using OpenApiInfo = Microsoft.OpenApi.OpenApiInfo;
#endif

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
// for learning and demonstration purposes. Set DISABLE_PROBLEM_ENDPOINTS=true
// to disable potentially destructive simulation endpoints.
// =============================================================================
//
// PORTING TO OTHER LANGUAGES:
// This file configures the application using three key patterns:
//
// 1. DEPENDENCY INJECTION (Service Registration):
//    - AddSingleton = one instance for app lifetime
//    - AddTransient = new instance per request
//    - AddScoped = one instance per HTTP request scope
//    PHP: Container libraries (PHP-DI, Pimple)
//    Node/Express: Typically manual factory functions or awilix
//    Java/Spring: @Component, @Service, @Scope annotations
//    Python/Flask: Flask extensions or dependency-injector library
//    Ruby/Rails: Built-in container or dry-container gem
//
// 2. MIDDLEWARE PIPELINE (Request Processing):
//    Order matters! Each middleware can short-circuit or pass to next.
//    PHP: PSR-15 middleware (Slim, Laravel)
//    Node/Express: app.use() - same concept, same critical ordering
//    Java/Spring: Filter chain, @Order annotation
//    Python/Flask: @app.before_request, Flask middleware
//    Ruby/Rails: Rack middleware stack, before_action callbacks
//
// 3. SIGNALR (Real-time WebSockets):
//    PHP: Ratchet/ReactPHP for WebSockets
//    Node/Express: Socket.IO (closest equivalent, very similar API)
//    Java/Spring: Spring WebSocket + STOMP
//    Python/Flask: Flask-SocketIO
//    Ruby/Rails: ActionCable (built-in Rails WebSocket support)
//
// RELATED FILES:
// - Services/*Service.cs: Business logic implementations
// - Controllers/*Controller.cs: HTTP endpoint handlers
// - Hubs/MetricsHub.cs: WebSocket real-time communication
// - Models/ProblemSimulatorOptions.cs: Configuration binding
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------------
// Bind the ProblemSimulator section from appsettings.json to strongly-typed options.
// This allows services to inject IOptions<ProblemSimulatorOptions> to access config values.
builder.Services.Configure<ProblemSimulatorOptions>(
    builder.Configuration.GetSection(ProblemSimulatorOptions.SectionName));

// Support HEALTH_PROBE_RATE environment variable as a friendly override for LatencyProbeIntervalMs
builder.Services.PostConfigure<ProblemSimulatorOptions>(options =>
{
    var healthProbeRate = Environment.GetEnvironmentVariable("HEALTH_PROBE_RATE");
    if (!string.IsNullOrEmpty(healthProbeRate) && int.TryParse(healthProbeRate, out var rateMs))
    {
        options.LatencyProbeIntervalMs = rateMs;
    }
});

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
builder.Services.AddSignalR(options =>
    {
        // Configure timeouts to prevent connections from hanging indefinitely
        // during simulated performance problems (thread starvation, crashes, etc.)
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);   // Disconnect if no message for 60s
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);       // Send keepalive every 15s
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);        // Handshake must complete in 15s
        
        // Enable detailed errors for debugging
        options.EnableDetailedErrors = true;
    })
    .AddJsonProtocol(options =>
    {
        // Use camelCase to match controller JSON serialization
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Add API documentation with Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
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
            - **SlowRequest**: Generates long-running blocking requests for CLR Profiler training
            - **Crash**: Triggers intentional crashes (OOM, StackOverflow) for Azure crash monitoring
            
            ## Safety Features
            - Problem endpoints can be disabled via DISABLE_PROBLEM_ENDPOINTS environment variable
            - Health endpoints remain responsive even under stress
            - Real-time dashboard shows metrics and active simulations
            """
    });
});

// -----------------------------------------------------------------------------
// Application Services
// -----------------------------------------------------------------------------
// IdleStateService - Singleton service for managing application idle state
// Educational Note: When no dashboard clients are connected and no load tests
// are running for the configured timeout (default 20 minutes), the app goes idle
// and stops sending health probes. This reduces unnecessary traffic to AppLens
// and Application Insights. Override timeout via IDLE_TIMEOUT_MINUTES env var.
builder.Services.AddSingleton<IIdleStateService, IdleStateService>();

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

// CrashService - Transient service for triggering intentional application crashes
// Educational Note: This service demonstrates various crash scenarios for learning
// how to use Azure crash monitoring and memory dump collection.
builder.Services.AddTransient<ICrashService, CrashService>();

// SlowRequestService - Singleton service for slow request simulation
// Educational Note: Singleton lifetime is required because the service maintains
// state about running simulations and spawns background threads. This service
// is designed to be used with CLR Profiler to demonstrate sync-over-async patterns.
builder.Services.AddHttpClient("SlowRequest")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Long timeout for slow requests
        ConnectTimeout = TimeSpan.FromSeconds(60)
    });
builder.Services.AddSingleton<ISlowRequestService, SlowRequestService>();

// LoadTestService - Singleton service for Azure Load Testing integration
// Educational Note: Singleton lifetime is required because the service maintains
// thread-safe counters for concurrent request tracking and lifetime statistics.
// This endpoint is designed to be targeted by Azure Load Testing or similar tools.
// Unlike other simulation endpoints, it degrades gracefully under load rather than
// causing immediate problems.
builder.Services.AddSingleton<ILoadTestService, LoadTestService>();

// FailedRequestService - Singleton service for generating HTTP 5xx errors
// Educational Note: Singleton lifetime is required because the service maintains
// state about running simulations. This service generates failed requests by calling
// the load test endpoint with 100% error probability, producing HTTP 500 errors that
// appear in AppLens and Application Insights for training on error diagnosis.
builder.Services.AddHttpClient("FailedRequest")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(60)
    });
builder.Services.AddSingleton<IFailedRequestService, FailedRequestService>();

// MetricsCollector - Singleton service for collecting system metrics
// Educational Note: This service runs on a DEDICATED THREAD (not the thread pool)
// so it remains responsive even during thread pool starvation scenarios.
// This is critical for FR-013 - health endpoints must work under stress.
builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();

// MetricsBroadcastService - Hosted service that broadcasts metrics to SignalR clients
// Educational Note: IHostedService provides proper startup/shutdown lifecycle management.
// This bridges the MetricsCollector (which fires events) with SignalR (which pushes to clients).
builder.Services.AddHostedService<MetricsBroadcastService>();

// LatencyProbeService - Hosted service that measures request latency on a dedicated thread
// Educational Note: This service demonstrates how thread pool starvation affects request
// processing time. It runs on a dedicated thread (not the thread pool) to ensure it can
// always measure latency, even during severe starvation conditions.
// All probes go through the Azure frontend (WEBSITE_HOSTNAME) when deployed.
builder.Services.AddHostedService<LatencyProbeService>();

// -----------------------------------------------------------------------------
// CORS Configuration
// -----------------------------------------------------------------------------
// Allow any origin. CORS is needed when the SPA is served from a different origin.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// -----------------------------------------------------------------------------
// Application Insights Telemetry
// -----------------------------------------------------------------------------
// Add Application Insights for Azure monitoring integration.
// The connection string is read from APPLICATIONINSIGHTS_CONNECTION_STRING env var
// or the ApplicationInsights:ConnectionString configuration setting.
// Educational Note: Application Insights v3 uses OpenTelemetry under the hood.
// Request tracking, dependency tracking, exception logging are automatic.
builder.Services.AddApplicationInsightsTelemetry();

// SimulationContext - Singleton service for tracking current simulation context
// Educational Note: This uses AsyncLocal<T> to flow the simulation ID across async calls.
// The context also uses TelemetryClient to track simulation events with the ID attached.
builder.Services.AddSingleton<ISimulationContext, SimulationContext>();

// -----------------------------------------------------------------------------
// Request Timeouts (Kestrel)
// -----------------------------------------------------------------------------
// Add request timeouts to match IIS behavior (web.config).
// This ensures slow requests timeout after 30 seconds, consistent with the UI threshold.
// Educational Note: In Azure App Service, the web.config requestTimeout handles this.
// For Kestrel, we use the RequestTimeouts middleware instead.
builder.Services.AddRequestTimeouts(options =>
{
    // Default 30-second timeout for all endpoints (matches web.config and UI threshold)
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
    };
    
    // Extended timeout for slow request simulation endpoint
    // This endpoint intentionally runs 20-35s requests, so it needs more time
    options.AddPolicy("SlowRequest", new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(120),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
    });
    
    // No timeout for health/admin endpoints - they must always respond
    options.AddPolicy("NoTimeout", new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = null // Disable timeout
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

// Swagger/OpenAPI documentation - enabled in all environments for this educational tool
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "Performance Problem Simulator API";
});

// CORS must be called before UseRouting
app.UseCors();

// Request timeouts middleware (Kestrel) - matches web.config requestTimeout for IIS
// Educational Note: This must be placed before UseRouting so timeouts apply to all requests.
app.UseRequestTimeouts();

// Serve static files from wwwroot (for the SPA dashboard)
app.UseDefaultFiles(); // Enables default document (index.html)
app.UseStaticFiles();

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
logger.LogWarning(
    "🚀 PerfProblemSimulator starting. This message should appear in AppTraces if App Insights logging is configured correctly.");
logger.LogInformation(
    "Problem endpoints are {Status}",
    Environment.GetEnvironmentVariable("DISABLE_PROBLEM_ENDPOINTS")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
        ? "DISABLED"
        : "ENABLED");

// Start the application
app.Run();

// =============================================================================
// Make Program class accessible for integration testing
// =============================================================================
// The WebApplicationFactory<T> used in integration tests needs access to the
// Program class. Since top-level statements generate an implicit Program class
// that is internal, we need to make it public for the test project to access.
// =============================================================================
public partial class Program;
