/**
 * Performance Problem Simulator - Dashboard JavaScript
 * 
 * Educational Note:
 * This file handles real-time metrics visualization using SignalR.
 * SignalR provides WebSocket communication with automatic fallback
 * to Server-Sent Events or Long Polling if WebSockets aren't available.
 */

// ==========================================================================
// Configuration & State
// ==========================================================================

const CONFIG = {
    maxDataPoints: 60,  // 1 minute of data at 1-second intervals
    maxLatencyDataPoints: 600, // 60 seconds at 100ms intervals
    latencyProbeIntervalMs: 100,
    latencyTimeoutMs: 30000,
    reconnectDelayMs: 2000,
    apiBaseUrl: '/api'
};

const state = {
    connection: null,
    charts: {},
    metricsHistory: {
        timestamps: [],
        cpu: [],
        memory: [],
        threads: [],
        queue: []
    },
    latencyHistory: {
        timestamps: [],
        values: [],
        isTimeout: [],
        isError: []
    },
    latencyStats: {
        timeoutCount: 0
    },
    clientProbeInterval: null,
    activeSimulations: new Map(),
    lastProcessId: null
};

// ==========================================================================
// UTC Time Formatting
// ==========================================================================

/**
 * Formats a Date object as UTC time string (HH:MM:SS)
 * All times in the dashboard use UTC to match Azure diagnostics data.
 * @param {Date} date - The date to format
 * @returns {string} UTC time string in HH:MM:SS format
 */
function formatUtcTime(date) {
    if (!date || !(date instanceof Date)) return '';
    const hours = date.getUTCHours().toString().padStart(2, '0');
    const minutes = date.getUTCMinutes().toString().padStart(2, '0');
    const seconds = date.getUTCSeconds().toString().padStart(2, '0');
    return `${hours}:${minutes}:${seconds}`;
}

/**
 * Gets the current UTC time as a formatted string
 * @returns {string} Current UTC time in HH:MM:SS format
 */
function getCurrentUtcTime() {
    return formatUtcTime(new Date());
}

// ==========================================================================
// SignalR Connection
// ==========================================================================

/**
 * Initialize SignalR connection to the metrics hub.
 * 
 * Educational Note:
 * SignalR automatically negotiates the best transport (WebSocket, SSE, Long Polling).
 * We use withAutomaticReconnect() to handle temporary disconnections gracefully.
 */
async function initializeSignalR() {
    state.connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/metrics')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // Retry with backoff
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // Handle connection state changes
    state.connection.onreconnecting(error => {
        updateConnectionStatus('connecting', 'Reconnecting...');
        logEvent('warning', 'Connection lost. Attempting to reconnect...');
    });

    state.connection.onreconnected(connectionId => {
        updateConnectionStatus('connected', 'Connected');
        logEvent('success', 'Reconnected to server');
    });

    state.connection.onclose(error => {
        updateConnectionStatus('disconnected', 'Disconnected');
        logEvent('error', 'Connection closed. Refresh page to reconnect.');
    });

    // Register message handlers
    // Note: SignalR uses camelCase for method names by default
    state.connection.on('ReceiveMetrics', handleMetricsUpdate);
    state.connection.on('receiveMetrics', handleMetricsUpdate);
    state.connection.on('SimulationStarted', handleSimulationStarted);
    state.connection.on('simulationStarted', handleSimulationStarted);
    state.connection.on('SimulationCompleted', handleSimulationCompleted);
    state.connection.on('simulationCompleted', handleSimulationCompleted);
    state.connection.on('ReceiveLatency', handleLatencyUpdate);
    state.connection.on('receiveLatency', handleLatencyUpdate);

    // Start connection
    try {
        updateConnectionStatus('connecting', 'Connecting...');
        await state.connection.start();
        updateConnectionStatus('connected', 'Connected');
        logEvent('success', 'Connected to metrics hub');
        
        // Start client-side probe as backup/additional measurement
        startClientProbe();
    } catch (err) {
        updateConnectionStatus('disconnected', 'Failed to connect');
        logEvent('error', `Connection failed: ${err.message}`);
        // Try again after delay
        setTimeout(initializeSignalR, CONFIG.reconnectDelayMs);
    }
}

function updateConnectionStatus(status, text) {
    const indicator = document.getElementById('connectionIndicator');
    const textEl = document.getElementById('connectionText');
    
    indicator.className = `indicator ${status}`;
    textEl.textContent = text;
}

// ==========================================================================
// Metrics Handling
// ==========================================================================

/**
 * Handle incoming metrics snapshot from SignalR.
 * Updates all dashboard elements with the latest data.
 */
function handleMetricsUpdate(snapshot) {
    // Check for application restart (process ID change)
    if (snapshot.processId) {
        if (state.lastProcessId !== null && state.lastProcessId !== snapshot.processId) {
            logEvent('danger', `🔄 APPLICATION RESTARTED! Process ID changed from ${state.lastProcessId} to ${snapshot.processId}. This may indicate an unexpected crash (OOM, StackOverflow, etc.)`);
        }
        state.lastProcessId = snapshot.processId;
    }

    // Update metric cards
    updateMetricCard('cpu', snapshot.cpuPercent, '%', 100);
    // Use actual available memory from server for dynamic thresholds
    const memoryMax = snapshot.totalAvailableMemoryMb || 1000;
    updateMetricCard('memory', snapshot.workingSetMb, 'MB', memoryMax);
    updateMetricCard('threads', snapshot.threadPoolThreads, 'threads', 100);
    updateMetricCard('queue', snapshot.threadPoolQueueLength, 'pending', 100);

    // Update total memory display
    const totalMemoryEl = document.getElementById('memoryTotal');
    if (totalMemoryEl && snapshot.totalAvailableMemoryMb) {
        const totalFormatted = snapshot.totalAvailableMemoryMb >= 1024 
            ? (snapshot.totalAvailableMemoryMb / 1024).toFixed(1) + ' GB'
            : Math.round(snapshot.totalAvailableMemoryMb) + ' MB';
        totalMemoryEl.textContent = `of ${totalFormatted}`;
    }

    // Update history for charts
    const timestamp = new Date(snapshot.timestamp);
    addToHistory(timestamp, snapshot);
    
    // Update charts
    updateCharts();
    
    // Update last update time
    document.getElementById('lastUpdate').textContent = formatUtcTime(timestamp) + ' UTC';
}

function updateMetricCard(type, value, unit, maxForBar) {
    const valueEl = document.getElementById(`${type}Value`);
    const barEl = document.getElementById(`${type}Bar`);
    const card = valueEl.closest('.metric-card');
    
    // Format value
    const displayValue = typeof value === 'number' ? 
        (value < 10 ? value.toFixed(1) : Math.round(value)) : '--';
    valueEl.textContent = displayValue;
    
    // Update bar
    const barPercent = Math.min(100, (value / maxForBar) * 100);
    barEl.style.width = `${barPercent}%`;
    
    // Warning states based on percentage of max
    card.classList.remove('warning', 'danger');
    if (type === 'cpu' || type === 'memory') {
        // Use barPercent for threshold comparison (value as % of max)
        if (barPercent > 80) card.classList.add('danger');
        else if (barPercent > 60) card.classList.add('warning');
    }
    if (type === 'queue' && value > 10) {
        card.classList.add('warning');
    }
}

function addToHistory(timestamp, snapshot) {
    const history = state.metricsHistory;
    
    history.timestamps.push(timestamp);
    history.cpu.push(snapshot.cpuPercent);
    history.memory.push(snapshot.workingSetMb);
    history.threads.push(snapshot.threadPoolThreads);
    history.queue.push(snapshot.threadPoolQueueLength);
    
    // Trim to max data points
    while (history.timestamps.length > CONFIG.maxDataPoints) {
        history.timestamps.shift();
        history.cpu.shift();
        history.memory.shift();
        history.threads.shift();
        history.queue.shift();
    }
}

// ==========================================================================
// Charts
// ==========================================================================

function initializeCharts() {
    // Resource chart (CPU + Memory)
    const resourceCtx = document.getElementById('resourceChart').getContext('2d');
    state.charts.resource = new Chart(resourceCtx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'CPU %',
                    data: [],
                    borderColor: '#0078d4',
                    backgroundColor: 'rgba(0, 120, 212, 0.1)',
                    tension: 0.3,
                    fill: true,
                    yAxisID: 'y'
                },
                {
                    label: 'Memory MB',
                    data: [],
                    borderColor: '#107c10',
                    backgroundColor: 'rgba(16, 124, 16, 0.1)',
                    tension: 0.3,
                    fill: true,
                    yAxisID: 'y1'
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'index',
                intersect: false
            },
            scales: {
                x: {
                    display: true,
                    ticks: {
                        maxTicksLimit: 10,
                        callback: (value, index) => {
                            const date = state.metricsHistory.timestamps[index];
                            return date ? formatUtcTime(date) : '';
                        }
                    }
                },
                y: {
                    type: 'linear',
                    display: true,
                    position: 'left',
                    min: 0,
                    max: 100,
                    title: { display: true, text: 'CPU %' }
                },
                y1: {
                    type: 'linear',
                    display: true,
                    position: 'right',
                    min: 0,
                    title: { display: true, text: 'Memory MB' },
                    grid: { drawOnChartArea: false }
                }
            },
            plugins: {
                legend: { position: 'top' }
            }
        }
    });

    // Thread chart (Threads + Queue)
    const threadCtx = document.getElementById('threadChart').getContext('2d');
    state.charts.threads = new Chart(threadCtx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'Active Threads',
                    data: [],
                    borderColor: '#8764b8',
                    backgroundColor: 'rgba(135, 100, 184, 0.1)',
                    tension: 0.3,
                    fill: true,
                    yAxisID: 'y'
                },
                {
                    label: 'Queue Length',
                    data: [],
                    borderColor: '#ffb900',
                    backgroundColor: 'rgba(255, 185, 0, 0.1)',
                    tension: 0.3,
                    fill: true,
                    yAxisID: 'y1'
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'index',
                intersect: false
            },
            scales: {
                x: {
                    display: true,
                    ticks: {
                        maxTicksLimit: 10,
                        callback: (value, index) => {
                            const date = state.metricsHistory.timestamps[index];
                            return date ? formatUtcTime(date) : '';
                        }
                    }
                },
                y: {
                    type: 'linear',
                    display: true,
                    position: 'left',
                    min: 0,
                    title: { display: true, text: 'Threads' }
                },
                y1: {
                    type: 'linear',
                    display: true,
                    position: 'right',
                    min: 0,
                    title: { display: true, text: 'Queue' },
                    grid: { drawOnChartArea: false }
                }
            },
            plugins: {
                legend: { position: 'top' }
            }
        }
    });

    // Latency chart
    const latencyCtx = document.getElementById('latencyChart').getContext('2d');
    state.charts.latency = new Chart(latencyCtx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'Latency (ms)',
                    data: [],
                    borderColor: '#0078d4',
                    backgroundColor: 'rgba(0, 120, 212, 0.1)',
                    tension: 0.2,
                    fill: true,
                    pointRadius: 0, // Hide points for performance with many data points
                    borderWidth: 1.5
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false, // Disable animation for better performance
            interaction: {
                mode: 'index',
                intersect: false
            },
            scales: {
                x: {
                    display: true,
                    ticks: {
                        maxTicksLimit: 10,
                        callback: (value, index) => {
                            const date = state.latencyHistory.timestamps[index];
                            return date ? formatUtcTime(date) : '';
                        }
                    }
                },
                y: {
                    type: 'logarithmic',
                    display: true,
                    position: 'left',
                    min: 1,
                    title: { display: true, text: 'Latency (ms) - Log Scale' },
                    ticks: {
                        callback: (value) => {
                            if (value === 1 || value === 10 || value === 100 || value === 1000 || value === 10000 || value === 30000) {
                                return value >= 1000 ? `${value/1000}s` : `${value}ms`;
                            }
                            return '';
                        }
                    }
                }
            },
            plugins: {
                legend: { display: false },
                tooltip: {
                    callbacks: {
                        label: (context) => {
                            const index = context.dataIndex;
                            const latency = context.raw;
                            const isTimeout = state.latencyHistory.isTimeout[index];
                            const isError = state.latencyHistory.isError[index];
                            
                            if (isTimeout) return `Timeout: ${latency}ms (30s)`;
                            if (isError) return `Error: ${latency}ms`;
                            return `Latency: ${latency}ms`;
                        }
                    }
                }
            }
        }
    });
}

function updateCharts() {
    const history = state.metricsHistory;
    const labels = history.timestamps.map(t => formatUtcTime(t));
    
    // Update resource chart
    state.charts.resource.data.labels = labels;
    state.charts.resource.data.datasets[0].data = history.cpu;
    state.charts.resource.data.datasets[1].data = history.memory;
    state.charts.resource.update('none'); // 'none' prevents animation on updates
    
    // Update thread chart
    state.charts.threads.data.labels = labels;
    state.charts.threads.data.datasets[0].data = history.threads;
    state.charts.threads.data.datasets[1].data = history.queue;
    state.charts.threads.update('none');
}

// ==========================================================================
// Latency Monitoring
// ==========================================================================

/**
 * Handle incoming latency measurement from server-side probe.
 * This shows the impact of thread pool starvation on request processing time.
 */
function handleLatencyUpdate(measurement) {
    // Only log errors or significant latency, not every update
    if (measurement.isError || measurement.isTimeout || measurement.latencyMs > 500) {
        console.log('Latency update:', measurement);
    }
    
    const timestamp = new Date(measurement.timestamp);
    const latencyMs = measurement.latencyMs;
    const isTimeout = measurement.isTimeout;
    const isError = measurement.isError;

    addLatencyToHistory(timestamp, latencyMs, isTimeout, isError);
    updateLatencyDisplay(latencyMs, isTimeout, isError);
    updateLatencyChart();
}

/**
 * Add latency measurement to history.
 */
function addLatencyToHistory(timestamp, latencyMs, isTimeout, isError) {
    const history = state.latencyHistory;
    
    history.timestamps.push(timestamp);
    history.values.push(latencyMs);
    history.isTimeout.push(isTimeout);
    history.isError.push(isError);
    
    // Track timeout count
    if (isTimeout) {
        state.latencyStats.timeoutCount++;
    }
    
    // Trim to max data points (60 seconds at 100ms = 600 points)
    while (history.timestamps.length > CONFIG.maxLatencyDataPoints) {
        history.timestamps.shift();
        const wasTimeout = history.isTimeout.shift();
        history.values.shift();
        history.isError.shift();
        
        // Adjust timeout count when old timeouts scroll out
        if (wasTimeout) {
            state.latencyStats.timeoutCount = Math.max(0, state.latencyStats.timeoutCount - 1);
        }
    }
}

/**
 * Update the latency stat displays (if present).
 */
function updateLatencyDisplay(currentLatency, isTimeout, isError) {
    const history = state.latencyHistory;
    
    // Current latency with color coding (check if element exists)
    const currentEl = document.getElementById('latencyCurrent');
    if (currentEl) {
        currentEl.textContent = formatLatency(currentLatency);
        currentEl.className = `latency-value ${getLatencyClass(currentLatency, isTimeout)}`;
    }
    
    // Calculate average
    const avgEl = document.getElementById('latencyAverage');
    if (avgEl && history.values.length > 0) {
        const avg = history.values.reduce((a, b) => a + b, 0) / history.values.length;
        avgEl.textContent = formatLatency(avg);
    }
    
    // Calculate max
    const maxEl = document.getElementById('latencyMax');
    if (maxEl && history.values.length > 0) {
        const max = Math.max(...history.values);
        maxEl.textContent = formatLatency(max);
    }
    
    // Update timeout count
    const timeoutsEl = document.getElementById('latencyTimeouts');
    if (timeoutsEl) {
        timeoutsEl.textContent = state.latencyStats.timeoutCount;
    }
}

/**
 * Format latency value for display.
 */
function formatLatency(ms) {
    if (ms >= 10000) {
        return (ms / 1000).toFixed(1) + 's';
    } else if (ms >= 1000) {
        return (ms / 1000).toFixed(2) + 's';
    } else if (ms >= 100) {
        return Math.round(ms);
    } else {
        return ms.toFixed(1);
    }
}

/**
 * Get CSS class based on latency value.
 */
function getLatencyClass(ms, isTimeout) {
    if (isTimeout) return 'timeout';
    if (ms > 500) return 'danger';
    if (ms > 50) return 'warning';
    return 'good';
}

/**
 * Update the latency chart.
 */
function updateLatencyChart() {
    if (!state.charts.latency) return;
    
    const history = state.latencyHistory;
    
    // Create gradient based on latency values
    const ctx = state.charts.latency.ctx;
    const gradient = ctx.createLinearGradient(0, 0, 0, 400);
    gradient.addColorStop(0, 'rgba(209, 52, 56, 0.3)');   // Red at top (high latency)
    gradient.addColorStop(0.5, 'rgba(255, 185, 0, 0.2)'); // Yellow in middle
    gradient.addColorStop(1, 'rgba(16, 124, 16, 0.1)');   // Green at bottom (low latency)
    
    // Map data points to colors based on latency
    const pointColors = history.values.map((v, i) => {
        if (history.isTimeout[i]) return '#d13438';
        if (v > 500) return '#d13438';
        if (v > 50) return '#ffb900';
        return '#107c10';
    });
    
    state.charts.latency.data.labels = history.timestamps.map(t => formatUtcTime(t));
    state.charts.latency.data.datasets[0].data = history.values;
    state.charts.latency.data.datasets[0].backgroundColor = gradient;
    state.charts.latency.data.datasets[0].borderColor = pointColors;
    state.charts.latency.data.datasets[0].segment = {
        borderColor: ctx => {
            const index = ctx.p0DataIndex;
            const latency = history.values[index];
            const isTimeout = history.isTimeout[index];
            if (isTimeout) return '#d13438';
            if (latency > 500) return '#d13438';
            if (latency > 50) return '#ffb900';
            return '#107c10';
        }
    };
    state.charts.latency.update('none');
}

/**
 * Start client-side probe as a backup/additional measurement.
 * This runs independently in the browser and won't be affected by server thread pool issues.
 */
function startClientProbe() {
    // Client probe at same interval as server for comparison
    state.clientProbeInterval = setInterval(async () => {
        try {
            const start = performance.now();
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), CONFIG.latencyTimeoutMs);
            
            try {
                await fetch(`${CONFIG.apiBaseUrl}/health/probe`, {
                    signal: controller.signal
                });
                clearTimeout(timeoutId);
                
                const latency = performance.now() - start;
                // Client probe data could be shown separately or compared
                // For now, we rely on server-side probe which is more accurate for thread pool measurement
            } catch (e) {
                clearTimeout(timeoutId);
                if (e.name === 'AbortError') {
                    // Timeout - handled by server probe
                }
            }
        } catch (err) {
            // Network error - server probe will detect this
        }
    }, CONFIG.latencyProbeIntervalMs);
}

/**
 * Stop client-side probe.
 */
function stopClientProbe() {
    if (state.clientProbeInterval) {
        clearInterval(state.clientProbeInterval);
        state.clientProbeInterval = null;
    }
}

// ==========================================================================
// Simulation Controls
// ==========================================================================

async function triggerCpuStress() {
    const duration = parseInt(document.getElementById('cpuDuration').value) || 10;
    
    try {
        logEvent('info', `Triggering CPU stress for ${duration} seconds...`);
        const response = await fetch(`${CONFIG.apiBaseUrl}/cpu/trigger-high-cpu`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ durationSeconds: duration })
        });
        
        if (response.ok) {
            const result = await response.json();
            addActiveSimulation(result.simulationId, 'cpu', 'CPU Stress');
            logEvent('success', `CPU stress started: ${result.simulationId}`);
        } else {
            const error = await response.json();
            logEvent('error', `Failed: ${error.detail || 'Unknown error'}`);
        }
    } catch (err) {
        logEvent('error', `Request failed: ${err.message}`);
    }
}

async function allocateMemory() {
    const sizeMb = parseInt(document.getElementById('memorySize').value) || 100;
    
    try {
        logEvent('info', `Allocating ${sizeMb} MB of memory...`);
        const response = await fetch(`${CONFIG.apiBaseUrl}/memory/allocate-memory`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sizeMegabytes: sizeMb })
        });
        
        if (response.ok) {
            const result = await response.json();
            const actualSizeMb = result.actualParameters?.sizeMegabytes ?? sizeMb;
            addActiveSimulation(result.simulationId, 'memory', `Memory ${actualSizeMb}MB`);
            logEvent('success', `Memory allocated: ${result.simulationId} (${actualSizeMb} MB)`);
        } else {
            const error = await response.json();
            logEvent('error', `Failed: ${error.detail || 'Unknown error'}`);
        }
    } catch (err) {
        logEvent('error', `Request failed: ${err.message}`);
    }
}

async function releaseMemory() {
    try {
        logEvent('info', 'Releasing all allocated memory...');
        const response = await fetch(`${CONFIG.apiBaseUrl}/memory/release-memory`, {
            method: 'POST'
        });
        
        if (response.ok) {
            const result = await response.json();
            // Remove all memory simulations from active list
            state.activeSimulations.forEach((value, key) => {
                if (value.type === 'memory') {
                    state.activeSimulations.delete(key);
                }
            });
            updateActiveSimulationsUI();
            const releasedMb = result.releasedMegabytes ?? (result.releasedBytes / 1024 / 1024);
            logEvent('success', `Released ${result.releasedBlockCount ?? 0} blocks (${releasedMb.toFixed(1)} MB)`);
        } else {
            const error = await response.json();
            logEvent('error', `Failed: ${error.detail || 'Unknown error'}`);
        }
    } catch (err) {
        logEvent('error', `Request failed: ${err.message}`);
    }
}

async function triggerThreadBlock() {
    const delayMs = parseInt(document.getElementById('threadDelay').value) || 5000;
    const concurrent = parseInt(document.getElementById('threadConcurrent').value) || 100;
    
    try {
        logEvent('info', `Triggering thread blocking: ${concurrent} requests, ${delayMs}ms delay...`);
        const response = await fetch(`${CONFIG.apiBaseUrl}/threadblock/trigger-sync-over-async`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                delayMilliseconds: delayMs,
                concurrentRequests: concurrent
            })
        });
        
        if (response.ok) {
            const result = await response.json();
            addActiveSimulation(result.simulationId, 'threadblock', 'Thread Block');
            logEvent('success', `Thread blocking started: ${result.simulationId}`);
        } else {
            const error = await response.json();
            logEvent('error', `Failed: ${error.detail || 'Unknown error'}`);
        }
    } catch (err) {
        logEvent('error', `Request failed: ${err.message}`);
    }
}

/**
 * Triggers an application crash.
 * WARNING: This will terminate the application!
 */
async function triggerCrash() {
    const crashType = document.getElementById('crashType').value;
    const delaySeconds = parseInt(document.getElementById('crashDelay').value) || 0;
    
    // Confirmation dialog - always synchronous for Azure Crash Monitoring
    const confirmed = confirm(
        `⚠️ WARNING: This will CRASH the application!\n\n` +
        `Crash Type: ${crashType}\n` +
        `Delay: ${delaySeconds} seconds\n\n` +
        `The application will terminate and Azure will auto-restart it.\n` +
        `✓ Azure Crash Monitoring will capture this crash.\n` +
        `\nAre you sure you want to proceed?`
    );
    
    if (!confirmed) {
        logEvent('info', 'Crash cancelled by user');
        return;
    }
    
    try {
        logEvent('danger', `💥 CRASH: ${crashType}${delaySeconds > 0 ? ` in ${delaySeconds}s` : ''} - Connection will be lost!`);
        
        const response = await fetch(`${CONFIG.apiBaseUrl}/crash/trigger`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                crashType: crashType,
                delaySeconds: delaySeconds,
                synchronous: true,
                message: `Crash triggered from dashboard at ${new Date().toISOString()}`
            })
        });
        
        // If synchronous, we shouldn't get here (process crashed)
        if (response.ok) {
            const result = await response.json();
            logEvent('danger', `💀 ${result.message}`);
            
            // Show countdown for async crashes
            if (!synchronous && delaySeconds > 0) {
                let countdown = delaySeconds;
                const countdownInterval = setInterval(() => {
                    countdown--;
                    if (countdown > 0) {
                        logEvent('warning', `💥 Crash in ${countdown}...`);
                    } else {
                        logEvent('danger', '💥 CRASHING NOW!');
                        clearInterval(countdownInterval);
                    }
                }, 1000);
            }
        } else {
            const error = await response.json();
            logEvent('error', `Failed: ${error.message || 'Unknown error'}`);
        }
    } catch (err) {
        // For synchronous crashes, a network error is expected (connection lost)
        if (synchronous) {
            logEvent('danger', '💥 Application crashed! Connection lost. Waiting for restart...');
        } else {
            logEvent('error', `Request failed: ${err.message}`);
        }
    }
}

// ==========================================================================
// Active Simulations UI
// ==========================================================================

function handleSimulationStarted(simulationType, simulationId) {
    addActiveSimulation(simulationId, simulationType.toLowerCase(), simulationType);
    logEvent('info', `Simulation started: ${simulationType} (${simulationId})`);
}

function handleSimulationCompleted(simulationType, simulationId) {
    removeActiveSimulation(simulationId);
    logEvent('success', `Simulation completed: ${simulationType} (${simulationId})`);
}

function addActiveSimulation(id, type, label) {
    state.activeSimulations.set(id, { type, label, startTime: new Date() });
    updateActiveSimulationsUI();
}

function removeActiveSimulation(id) {
    state.activeSimulations.delete(id);
    updateActiveSimulationsUI();
}

function updateActiveSimulationsUI() {
    const container = document.getElementById('simulationsList');
    
    if (state.activeSimulations.size === 0) {
        container.innerHTML = '<p class="no-simulations">No active simulations</p>';
        return;
    }
    
    container.innerHTML = Array.from(state.activeSimulations.entries())
        .map(([id, sim]) => `
            <div class="simulation-badge ${sim.type}">
                <span class="spinner"></span>
                <span>${sim.label}</span>
            </div>
        `).join('');
}

// ==========================================================================
// Event Log
// ==========================================================================

function logEvent(level, message) {
    const log = document.getElementById('eventLog');
    const time = getCurrentUtcTime();
    
    const entry = document.createElement('div');
    entry.className = `log-entry ${level}`;
    entry.innerHTML = `<span class="log-time">${time} UTC</span>${message}`;
    
    log.insertBefore(entry, log.firstChild);
    
    // Limit log entries
    while (log.children.length > 50) {
        log.removeChild(log.lastChild);
    }
}

// ==========================================================================
// Initialization
// ==========================================================================

document.addEventListener('DOMContentLoaded', () => {
    // Initialize charts first
    initializeCharts();
    
    // Start SignalR connection
    initializeSignalR();
    
    // Wire up button handlers
    document.getElementById('btnTriggerCpu').addEventListener('click', triggerCpuStress);
    document.getElementById('btnAllocateMemory').addEventListener('click', allocateMemory);
    document.getElementById('btnReleaseMemory').addEventListener('click', releaseMemory);
    document.getElementById('btnTriggerThreadBlock').addEventListener('click', triggerThreadBlock);
    document.getElementById('btnTriggerCrash').addEventListener('click', triggerCrash);
    
    // Wire up Reset All button
    const btnResetAll = document.getElementById('btnResetAll');
    if (btnResetAll) {
        btnResetAll.addEventListener('click', resetAll);
    }
    
    // Wire up side panel toggle
    initializeSidePanel();
    
    logEvent('info', 'Dashboard initialized');
});

// ==========================================================================
// Side Panel Management
// ==========================================================================

function initializeSidePanel() {
    const btnToggle = document.getElementById('btnTogglePanel');
    const btnClose = document.getElementById('btnClosePanel');
    const sidePanel = document.getElementById('sidePanel');
    
    if (btnToggle) {
        btnToggle.addEventListener('click', toggleSidePanel);
    }
    
    if (btnClose) {
        btnClose.addEventListener('click', closeSidePanel);
    }
    
    // Close panel when clicking outside (on main content)
    document.addEventListener('click', (e) => {
        if (sidePanel.classList.contains('open')) {
            // Check if click is outside the panel and not on the toggle button
            if (!sidePanel.contains(e.target) && !btnToggle.contains(e.target)) {
                closeSidePanel();
            }
        }
    });
    
    // Close panel on Escape key
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && sidePanel.classList.contains('open')) {
            closeSidePanel();
        }
    });
}

function toggleSidePanel() {
    const sidePanel = document.getElementById('sidePanel');
    const btnToggle = document.getElementById('btnTogglePanel');
    
    if (sidePanel.classList.contains('open')) {
        closeSidePanel();
    } else {
        sidePanel.classList.add('open');
        btnToggle.classList.add('active');
    }
}

function closeSidePanel() {
    const sidePanel = document.getElementById('sidePanel');
    const btnToggle = document.getElementById('btnTogglePanel');
    
    sidePanel.classList.remove('open');
    btnToggle.classList.remove('active');
}

async function resetAll() {
    if (!confirm('Reset all active simulations and release all memory?')) {
        return;
    }
    
    try {
        const response = await fetch(`${CONFIG.apiBaseUrl}/admin/reset-all`, {
            method: 'POST'
        });
        
        if (response.ok) {
            const result = await response.json();
            logEvent('success', `🔄 Reset complete: ${result.memoryBlocksReleased} memory blocks released`);
        } else {
            logEvent('error', 'Reset failed');
        }
    } catch (error) {
        logEvent('error', `Reset error: ${error.message}`);
    }
}
