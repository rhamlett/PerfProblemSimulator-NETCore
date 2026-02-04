# Performance Problem Simulator

An educational Azure App Service application that **intentionally creates performance problems** for learning and demonstration purposes.

## 🎯 Purpose

This application is designed to help developers and DevOps engineers:

- **Learn** how to diagnose common performance issues in Azure App Service
- **Practice** using Azure monitoring and diagnostic tools
- **Demonstrate** performance anti-patterns in a controlled environment
- **Train** support teams on identifying and resolving performance problems

## ⚠️ Warning

**This application intentionally creates performance problems!**

- 🔥 **CPU stress** - Creates dedicated threads running spin loops to consume all CPU cores
- 📊 **Memory pressure** - Allocates and pins memory blocks to increase working set
- 🧵 **Thread pool starvation** - Uses sync-over-async anti-patterns to block thread pool threads
- � **Slow requests** - Generates long-running requests with sync-over-async patterns for CLR Profiler analysis
- �💥 **Application crashes** - Triggers fatal crashes for testing Azure Crash Monitoring and memory dumps

**Only deploy this application in isolated, non-production environments.**

## 🚀 Quick Start

### Run Locally

```bash
# Clone the repository
git clone https://github.com/your-org/perf-problem-simulator.git
cd perf-problem-simulator

# Restore and build
dotnet build

# Run the application
dotnet run --project src/PerfProblemSimulator

# Open in browser
# Dashboard: http://localhost:5000
# Swagger: http://localhost:5000/swagger
```

### Run Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## 📊 Dashboard

The application includes a real-time dashboard at the root URL that shows:

- **CPU usage** - Current processor utilization
- **Memory** - Working set and GC heap sizes
- **Thread pool** - Active threads and queue length
- **Request latency** - Real-time probe response time (shows impact of thread pool starvation)
- **Active simulations** - Currently running problem simulations

The dashboard uses SignalR for real-time updates and includes controls to trigger each type of simulation.

### Metric Color Indicators

The CPU and Memory metric tiles use dynamic color coding based on utilization percentage:

| Color | Utilization | Status |
|-------|-------------|--------|
| Black (default) | 0-60% | Normal |
| Yellow | 60-80% | Warning - elevated usage |
| Red | >80% | Danger - potential resource exhaustion |

**Note:** Memory thresholds are calculated dynamically based on the actual total available memory reported by the server, ensuring accurate warnings regardless of the machine's RAM configuration.

## 🔌 API Endpoints

### Health & Monitoring

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Basic health check |
| `/api/health/status` | GET | Detailed health with active simulations |
| `/api/health/probe` | GET | Lightweight probe for latency measurement |
| `/api/metrics/current` | GET | Latest metrics snapshot |
| `/api/metrics/health` | GET | Detailed health status with warnings |
| `/api/admin/stats` | GET | Simulation and resource statistics |

### CPU Stress Simulation

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/cpu/trigger-high-cpu` | POST | Trigger CPU stress |

**Request body:**
```json
{
  "durationSeconds": 30,
  "targetPercentage": 100
}
```

### Memory Pressure Simulation

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/memory/allocate-memory` | POST | Allocate memory block |
| `/api/memory/release-memory` | POST | Release all allocated memory |

**Request body (allocate):**
```json
{
  "sizeMegabytes": 100
}
```

### Thread Pool Starvation Simulation

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/threadblock/trigger-sync-over-async` | POST | Trigger thread blocking |

**Request body:**
```json
{
  "delayMilliseconds": 5000,
  "concurrentRequests": 100
}
```

### Slow Request Simulation

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/slowrequest/start` | POST | Start slow request simulation |
| `/api/slowrequest/stop` | POST | Stop slow request simulation |
| `/api/slowrequest/status` | GET | Get current simulation status |
| `/api/slowrequest/scenarios` | GET | Get scenario descriptions for CLR Profiler |

**Request body (start):**
```json
{
  "requestDurationSeconds": 25,
  "intervalSeconds": 2,
  "maxRequests": 10
}
```

The slow request simulator generates requests using three different sync-over-async patterns:
- **SimpleSyncOverAsync**: Blocking calls - look for `FetchDataSync_BLOCKING_HERE`, `ProcessDataSync_BLOCKING_HERE`, `SaveDataSync_BLOCKING_HERE` in traces
- **NestedSyncOverAsync**: Sync methods that block internally - look for `*_BLOCKS_INTERNALLY` methods
- **DatabasePattern**: Simulated database/HTTP blocking - look for `*Sync_SYNC_BLOCK` methods

### Admin Operations

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/admin/reset-all` | POST | Release all memory and reset state |
| `/api/admin/stats` | GET | Get current simulation statistics |

## ⏱️ Request Latency Monitor

The dashboard includes a **Request Latency Monitor** that demonstrates how thread pool starvation affects real-world request processing.

### How It Works

- A dedicated background thread (not from the thread pool) continuously probes `/api/health/probe`
- Latency is measured end-to-end: request sent → response received
- Results are broadcast via SignalR to the dashboard in real-time

### What You'll Observe

| Scenario | Latency (Queue + Processing) | Status | Explanation |
|----------|------------------------------|--------|-------------|
| Normal operation | < 150ms | 🟢 Good | Thread pool threads available |
| Mild starvation | 150ms - 1s | 🟡 Degraded | Requests beginning to queue |
| Severe starvation | > 1s | 🔴 Critical | Significant queuing delay |
| Timeout | 30s | 🔴 Timeout | No thread became available within timeout |

### Why This Matters

During thread pool starvation, CPU and memory metrics often look normal, but users experience severe latency. The latency monitor makes this invisible problem **visible** - you can watch response times spike from milliseconds to seconds when triggering the sync-over-async simulation.

## 🔧 Configuration

Configuration is managed through `appsettings.json`:

```json
{
  "ProblemSimulator": {
    "MetricsCollectionIntervalMs": 1000
  }
}
```

**Note:** This application is designed to be fully breakable for educational purposes. There are no safety limits on resource consumption — simulations can run until the application crashes or resources are exhausted.

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DISABLE_PROBLEM_ENDPOINTS` | Set to `true` to disable all problem-triggering endpoints | `false` |

## ☁️ Azure Deployment

### Using Azure CLI

```bash
# Login to Azure
az login

# Create resource group
az group create --name rg-perf-simulator --location eastus

# Create App Service plan
az appservice plan create \
  --name asp-perf-simulator \
  --resource-group rg-perf-simulator \
  --sku B1 \
  --is-linux

# Create Web App
az webapp create \
  --name your-unique-app-name \
  --resource-group rg-perf-simulator \
  --plan asp-perf-simulator \
  --runtime "DOTNETCORE:10.0"

# Deploy
cd src/PerfProblemSimulator
dotnet publish -c Release
az webapp deploy \
  --resource-group rg-perf-simulator \
  --name your-unique-app-name \
  --src-path bin/Release/net10.0/publish
```

### Safety Recommendation

After deployment, consider disabling problem endpoints:

```bash
az webapp config appsettings set \
  --resource-group rg-perf-simulator \
  --name your-unique-app-name \
  --settings DISABLE_PROBLEM_ENDPOINTS=true
```

## 🔍 Using Azure Diagnostics

This application is designed to work with Azure App Service diagnostics:

### Recommended Diagnostic Tools

1. **Diagnose and Solve Problems** - App Service blade for automated diagnosis
2. **Application Insights** - For detailed telemetry and performance monitoring
3. **Process Explorer** - For real-time process monitoring
4. **CPU Profiling** - Capture and analyze CPU traces
5. **Memory Dumps** - Analyze memory allocations

See [Azure Monitoring Guide](./docs/azure-monitoring-guide.md) for detailed instructions.

## 📐 Architecture

```
src/PerfProblemSimulator/
├── Controllers/          # API endpoints
│   ├── AdminController.cs
│   ├── CpuController.cs
│   ├── CrashController.cs
│   ├── HealthController.cs
│   ├── MemoryController.cs
│   ├── MetricsController.cs
│   ├── SlowRequestController.cs
│   └── ThreadBlockController.cs
├── Services/             # Business logic
│   ├── CpuStressService.cs
│   ├── CrashService.cs
│   ├── LatencyProbeService.cs
│   ├── MemoryPressureService.cs
│   ├── MetricsBroadcastService.cs
│   ├── MetricsCollector.cs
│   ├── SimulationTracker.cs
│   ├── SlowRequestService.cs
│   └── ThreadBlockService.cs
├── Hubs/                 # SignalR for real-time updates
│   ├── MetricsHub.cs
│   └── IMetricsClient.cs
├── Models/               # Data transfer objects
├── Middleware/           # Request pipeline
│   └── ProblemEndpointGuard.cs
└── wwwroot/              # SPA dashboard
    ├── index.html
    ├── documentation.html
    ├── azure-monitoring-guide.html
    ├── css/dashboard.css
    └── js/dashboard.js
```

## 🧪 Testing

The project includes comprehensive unit and integration tests:

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run specific test category
dotnet test --filter "Category=Unit"
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 🙏 Acknowledgments

- Designed for educational use in Azure App Service training
- Inspired by common performance anti-patterns encountered in production
- Built with .NET 10.0 and ASP.NET Core
