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

- 🔥 **CPU stress** - Creates parallel spin loops that consume all CPU cores
- 📊 **Memory pressure** - Allocates and pins memory blocks to increase working set
- 🧵 **Thread pool starvation** - Uses sync-over-async anti-patterns to block thread pool threads

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
  "durationSeconds": 30
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

### Admin Operations

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/admin/reset-all` | POST | Release all memory and reset state |
| `/api/admin/stats` | GET | Get current simulation statistics |

## 🔧 Configuration

Configuration is managed through `appsettings.json`:

```json
{
  "ProblemSimulator": {
    "MaxCpuDurationSeconds": 300,
    "MaxMemoryAllocationMb": 1024,
    "MaxThreadBlockDelayMs": 30000,
    "MaxConcurrentBlockingRequests": 200,
    "MetricsCollectionIntervalMs": 1000,
    "EnableRequestLogging": true
  }
}
```

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
  --runtime "DOTNETCORE:8.0"

# Deploy
cd src/PerfProblemSimulator
dotnet publish -c Release
az webapp deploy \
  --resource-group rg-perf-simulator \
  --name your-unique-app-name \
  --src-path bin/Release/net8.0/publish
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

See [azure-monitoring-guide.md](docs/azure-monitoring-guide.md) for detailed instructions.

## 📐 Architecture

```
src/PerfProblemSimulator/
├── Controllers/          # API endpoints
│   ├── CpuController.cs
│   ├── MemoryController.cs
│   ├── ThreadBlockController.cs
│   ├── MetricsController.cs
│   ├── HealthController.cs
│   └── AdminController.cs
├── Services/             # Business logic
│   ├── CpuStressService.cs
│   ├── MemoryPressureService.cs
│   ├── ThreadBlockService.cs
│   ├── SimulationTracker.cs
│   ├── MetricsCollector.cs
│   ├── MetricsBroadcastService.cs
│   └── LatencyProbeService.cs
├── Hubs/                 # SignalR for real-time updates
│   └── MetricsHub.cs
├── Models/               # Data transfer objects
├── Middleware/           # Request pipeline
│   └── ProblemEndpointGuard.cs
└── wwwroot/              # SPA dashboard
    ├── index.html
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
