# Quickstart: Performance Problem Simulator

**Feature**: 001-perf-problem-simulator  
**Date**: 2026-01-29

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- A code editor (VS Code, Visual Studio, Rider)
- (Optional) Azure CLI for deployment
- (Optional) Azure subscription for App Service deployment

## Local Development

### 1. Clone and Build

```powershell
# Navigate to repository root
cd NETCoreApp

# Restore dependencies and build
dotnet build src/PerfProblemSimulator/PerfProblemSimulator.csproj

# Run tests
dotnet test tests/PerfProblemSimulator.Tests/PerfProblemSimulator.Tests.csproj
```

### 2. Run Locally

```powershell
# Run the application
dotnet run --project src/PerfProblemSimulator/PerfProblemSimulator.csproj

# Or with hot reload during development
dotnet watch run --project src/PerfProblemSimulator/PerfProblemSimulator.csproj
```

The application will start at:
- **HTTPS**: https://localhost:5001
- **HTTP**: http://localhost:5000
- **Dashboard**: https://localhost:5001/ (opens index.html)

### 3. Verify It's Working

```powershell
# Check health endpoint
curl https://localhost:5001/api/health

# Get current metrics
curl https://localhost:5001/api/metrics
```

---

## Using the Simulator

### Trigger High CPU

```powershell
# Trigger 30-second CPU stress (default)
curl -X POST https://localhost:5001/api/trigger-high-cpu

# Trigger 60-second CPU stress
curl -X POST https://localhost:5001/api/trigger-high-cpu `
  -H "Content-Type: application/json" `
  -d '{"durationSeconds": 60}'
```

**What to observe:**
- CPU usage rises to ~100% in Task Manager / Azure Portal
- Dashboard shows CPU spike in real-time
- Other requests may slow down slightly

### Trigger Memory Pressure

```powershell
# Allocate 100 MB (default)
curl -X POST https://localhost:5001/api/allocate-memory

# Allocate 500 MB
curl -X POST https://localhost:5001/api/allocate-memory `
  -H "Content-Type: application/json" `
  -d '{"sizeMegabytes": 500}'

# Release all allocated memory
curl -X POST https://localhost:5001/api/release-memory
```

**What to observe:**
- Memory usage increases in Task Manager / Azure Portal
- Dashboard shows Working Set and GC Heap growth
- Memory returns toward baseline after release (GC timing varies)

### Trigger Thread Pool Starvation

```powershell
# Trigger sync-over-async blocking (default: 10 threads, 1 second each)
curl -X POST https://localhost:5001/api/trigger-sync-over-async

# More aggressive starvation
curl -X POST https://localhost:5001/api/trigger-sync-over-async `
  -H "Content-Type: application/json" `
  -d '{"delayMilliseconds": 5000, "concurrentRequests": 50}'
```

**What to observe:**
- Thread count climbs in metrics
- Queue depth increases
- Response times for ALL endpoints increase
- CPU and memory appear normal (the key diagnostic clue!)

### Reset Everything

```powershell
# Stop all simulations and release memory
curl -X POST https://localhost:5001/api/reset-all
```

---

## Environment Configuration

### Disable Problem Endpoints

Set the `DISABLE_PROBLEM_ENDPOINTS` environment variable to disable all problem-triggering endpoints (useful for preventing accidental triggers):

```powershell
# PowerShell
$env:DISABLE_PROBLEM_ENDPOINTS = "true"
dotnet run --project src/PerfProblemSimulator/PerfProblemSimulator.csproj

# Or in appsettings.json
{
  "DisableProblemEndpoints": true
}
```

When disabled, problem endpoints return HTTP 503 Service Unavailable.

### Application Settings

Key settings in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ProblemSimulator": {
    "MaxCpuDurationSeconds": 300,
    "MaxMemoryAllocationMb": 1024,
    "MaxThreadBlockDelayMs": 30000,
    "MaxConcurrentThreadBlocks": 200,
    "MetricsCollectionIntervalMs": 1000
  },
  "DisableProblemEndpoints": false
}
```

---

## Azure App Service Deployment

### Option 1: Azure CLI

```powershell
# Login to Azure
az login

# Create resource group
az group create --name rg-perf-simulator --location eastus

# Create App Service Plan (Linux)
az appservice plan create `
  --name plan-perf-simulator `
  --resource-group rg-perf-simulator `
  --sku B1 `
  --is-linux

# Create Web App
az webapp create `
  --name perf-problem-simulator `
  --resource-group rg-perf-simulator `
  --plan plan-perf-simulator `
  --runtime "DOTNET|8.0"

# Deploy
dotnet publish src/PerfProblemSimulator/PerfProblemSimulator.csproj -c Release -o ./publish
cd publish
zip -r ../deploy.zip .
cd ..
az webapp deployment source config-zip `
  --resource-group rg-perf-simulator `
  --name perf-problem-simulator `
  --src deploy.zip
```

### Option 2: Visual Studio Publish

1. Right-click project → Publish
2. Select Azure → Azure App Service (Linux or Windows)
3. Create new or select existing App Service
4. Click Publish

### Option 3: GitHub Actions

See `.github/workflows/deploy.yml` (to be created) for CI/CD pipeline.

### Post-Deployment Verification

```powershell
# Check health
curl https://perf-problem-simulator.azurewebsites.net/api/health

# Open dashboard
Start-Process "https://perf-problem-simulator.azurewebsites.net/"
```

---

## Observing Problems in Azure

### Azure Portal Metrics

1. Navigate to your App Service in Azure Portal
2. Select **Monitoring** → **Metrics**
3. Add metrics:
   - **CPU Percentage** - Shows CPU stress effect
   - **Memory working set** - Shows memory allocation effect
   - **Requests** and **Response Time** - Shows thread starvation effect

### Application Insights (Optional)

If enabled via Azure Portal:

1. Navigate to Application Insights resource
2. **Performance** blade shows request duration degradation
3. **Live Metrics** shows real-time CPU, memory, request rate
4. **Failures** shows timeouts during starvation

### Diagnostic Tools

1. In App Service → **Diagnose and solve problems**
2. Search for "High CPU" or "Memory" diagnostics
3. Run the diagnostic to see how Azure identifies the problems you created

---

## Troubleshooting

### Application won't start

```powershell
# Check for missing SDK
dotnet --list-sdks

# Verify .NET 8.0 is installed
# If not, download from https://dotnet.microsoft.com/download/dotnet/8.0
```

### Can't trigger problems (503 errors)

Check if problem endpoints are disabled:
```powershell
# Should return false for problems to work
curl https://localhost:5001/api/health
# Check DisableProblemEndpoints setting
```

### Dashboard not updating

- Verify WebSocket connection (check browser console)
- Ensure SignalR hub is accessible at `/hubs/metrics`
- Check for CORS issues if running frontend separately

### Memory doesn't return after release

- GC runs on its own schedule; force with `forceGarbageCollection: true`
- Large Object Heap is collected less frequently
- Some memory is retained by the runtime (normal)

---

## Next Steps

1. **Explore the code**: Read the inline comments explaining each anti-pattern
2. **Try Azure diagnostics**: Use Azure's diagnostic tools to identify the problems
3. **Load testing**: Use tools like `bombardier` or `k6` to generate concurrent load
4. **Application Insights**: Enable for deeper telemetry and distributed tracing

## Useful Commands

```powershell
# Watch thread pool in real-time
dotnet-counters monitor --name PerfProblemSimulator --counters System.Runtime

# Profile CPU usage
dotnet-trace collect --name PerfProblemSimulator --duration 00:00:30

# Analyze memory
dotnet-gcdump collect --name PerfProblemSimulator
```
