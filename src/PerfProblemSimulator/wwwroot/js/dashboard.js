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
    maxDataPoints: 240,  // 1 minute of data at 250ms intervals
    maxLatencyDataPoints: 600, // 60 seconds at 100ms effective intervals (hybrid probing)
    // latencyProbeIntervalMs is loaded from server config (default 200ms).
    // Hybrid probing: Server probes at this interval, client probes at same interval
    // but offset by half, achieving ~100ms effective sample rate.
    latencyProbeIntervalMs: 200,
    latencyTimeoutMs: 30000,
    reconnectDelayMs: 2000,
    apiBaseUrl: '/api'
};

// Probe visualization history (24-dot indicator)
const probeHistory = [];
const MAX_PROBE_DOTS = 24;

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
    slowRequestHistory: {
        timestamps: [],
        values: [],
        scenarios: []
    },
    latencyStats: {
        timeoutCount: 0
    },
    clientProbeInterval: null,
    activeSimulations: new Map(),
    lastProcessId: null,
    isIdle: false  // Tracks whether the server is in idle state
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

    // Configure timeouts to detect server unresponsiveness faster
    // serverTimeoutInMilliseconds: How long client waits for server response before disconnecting
    // Must be at least 2x the server's KeepAliveInterval (15s), so we use 45s
    state.connection.serverTimeoutInMilliseconds = 45000;
    
    // keepAliveIntervalInMilliseconds: How often client sends ping to server
    state.connection.keepAliveIntervalInMilliseconds = 15000;

    // Handle connection state changes
    state.connection.onreconnecting(error => {
        updateConnectionStatus('connecting', 'Reconnecting...');
        logEvent('system', 'Connection lost. Attempting to reconnect...');
    });

    state.connection.onreconnected(async connectionId => {
        updateConnectionStatus('connected', 'Connected');
        logEvent('system', 'Reconnected to server');
        
        // Wake up server and sync idle state after reconnect
        // This is important because the server may have gone idle while disconnected
        try {
            await state.connection.invoke('WakeUp');
        } catch (err) {
            console.warn('Failed to invoke WakeUp on reconnect:', err);
        }
    });

    state.connection.onclose(error => {
        updateConnectionStatus('disconnected', 'Disconnected');
        logEvent('system', 'Connection closed. Attempting to reconnect...');
        // Auto-reconnect after close (handles cases where withAutomaticReconnect gives up)
        setTimeout(initializeSignalR, CONFIG.reconnectDelayMs);
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
    state.connection.on('ReceiveSlowRequestLatency', handleSlowRequestLatency);
    state.connection.on('receiveSlowRequestLatency', handleSlowRequestLatency);
    state.connection.on('ReceiveLoadTestStats', handleLoadTestStats);
    state.connection.on('receiveLoadTestStats', handleLoadTestStats);
    state.connection.on('ReceiveIdleState', handleIdleState);
    state.connection.on('receiveIdleState', handleIdleState);

    // Start connection
    try {
        updateConnectionStatus('connecting', 'Connecting...');
        await state.connection.start();
        updateConnectionStatus('connected', 'Connected');
        logEvent('system', 'Connected to metrics hub');
        
        // Start client-side probe as backup/additional measurement
        startClientProbe();
    } catch (err) {
        updateConnectionStatus('disconnected', 'Failed to connect');
        logEvent('system', `Connection failed: ${err.message}`);
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
            logEvent('crash', `APPLICATION RESTARTED! Process ID changed from ${state.lastProcessId} to ${snapshot.processId}. This may indicate an unexpected crash (OOM, StackOverflow, etc.)`, { icon: '🔄' });
            // Clear all active simulations since the app restarted
            clearAllActiveSimulations();
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
                    fill: 'origin',
                    yAxisID: 'y',
                    pointRadius: 0,
                    pointHoverRadius: 0,
                    borderWidth: 1
                },
                {
                    label: 'Memory MB',
                    data: [],
                    borderColor: '#107c10',
                    backgroundColor: 'rgba(16, 124, 16, 0.1)',
                    tension: 0.3,
                    fill: 'origin',
                    yAxisID: 'y1',
                    pointRadius: 0,
                    pointHoverRadius: 0,
                    borderWidth: 1
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
                    backgroundColor: 'rgba(135, 100, 184, 0.3)',
                    tension: 0.3,
                    fill: 'origin',
                    yAxisID: 'y',
                    pointRadius: 0,
                    pointHoverRadius: 0,
                    borderWidth: 1
                },
                {
                    label: 'Queue Length',
                    data: [],
                    borderColor: '#ffb900',
                    backgroundColor: 'rgba(255, 185, 0, 0.3)',
                    tension: 0.3,
                    fill: 'origin',
                    yAxisID: 'y1',
                    pointRadius: 0,
                    pointHoverRadius: 0,
                    borderWidth: 1
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
                    fill: 'origin',
                    pointRadius: 0, // Hide points for performance with many data points
                    pointHoverRadius: 0, // Disable hover circles to prevent visual artifacts
                    borderWidth: 1
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
                        maxTicksLimit: 6,
                        maxRotation: 0,
                        minRotation: 0,
                        font: { size: 10 },
                        callback: (value, index) => {
                            const date = state.latencyHistory.timestamps[index];
                            return date ? formatUtcTime(date) : '';
                        }
                    }
                },
                y: {
                    display: true,
                    position: 'left',
                    beginAtZero: true,
                    grace: '5%',
                    title: { display: true, text: 'Latency (ms)', font: { size: 10 } },
                    ticks: {
                        font: { size: 10 },
                        callback: (value) => {
                            if (value >= 1000) {
                                return (value / 1000).toFixed(1) + 's';
                            }
                            return value + 'ms';
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
                            
                            if (isTimeout) return `Critical (>30s): ${latency}ms`;
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
    // Log significant latency events to the dashboard log
    if (measurement.isError) {
        logEvent('system', `Health Probe Error: ${measurement.errorMessage || 'Unknown error'} (${formatLatency(measurement.latencyMs)})`);
    } else if (measurement.isTimeout) {
        logEvent('system', `Health Probe Critical (>30s): ${formatLatency(measurement.latencyMs)}`);
    } else if (measurement.latencyMs > 10000) {
        // Log extremely high latency (starvation) - yellow warning
        logEvent('warning', `High Latency Probe: ${formatLatency(measurement.latencyMs)}`);
    }
    
    const timestamp = new Date(measurement.timestamp);
    const latencyMs = measurement.latencyMs;
    const isTimeout = measurement.isTimeout;
    const isError = measurement.isError;

    addLatencyToHistory(timestamp, latencyMs, isTimeout, isError);
    updateLatencyDisplay(latencyMs, isTimeout, isError);
    updateLatencyChart();
    
    // Update probe visualization dots
    updateProbeVisualization(latencyMs);
}

/**
 * Handle incoming slow request latency from server.
 * This shows the actual duration of slow requests (20+ seconds).
 */
function handleSlowRequestLatency(data) {
    const timestamp = new Date(data.timestamp);
    const latencyMs = data.latencyMs;
    const scenario = data.scenario;
    const expectedMs = data.expectedDurationMs || 0;
    const isError = data.isError;
    const errorMessage = data.errorMessage;
    
    // Calculate Queue Time (Total - Expected)
    // If negative (processing was faster than expected?), clamp to 0
    const queueTimeMs = Math.max(0, latencyMs - expectedMs);
    
    // Flag as timeout if total time exceeds threshold (30s)
    const isTimeout = latencyMs >= CONFIG.latencyTimeoutMs;
    
    // Add to slow request history
    const history = state.slowRequestHistory;
    history.timestamps.push(timestamp);
    history.values.push(latencyMs);
    history.scenarios.push(scenario);
    
    // Trim to max data points
    while (history.timestamps.length > 100) {
        history.timestamps.shift();
        history.values.shift();
        history.scenarios.shift();
    }
    
    // Add as a special latency point on the chart (it will show as a large spike)
    addLatencyToHistory(timestamp, latencyMs, isTimeout, isError);
    updateLatencyDisplay(latencyMs, isTimeout, isError);
    updateLatencyChart();
    
    // Log the slow request completion with Queue Time breakdown
    const durationSec = (latencyMs / 1000).toFixed(1);
    const queueSec = (queueTimeMs / 1000).toFixed(1);
    
    if (isError) {
        let msg = `Slow request #${data.requestNumber} FAILED: ${durationSec}s (${scenario}) - ${errorMessage}`;
        if (queueTimeMs > 100) {
            msg += ` [Queue Time: ${queueSec}s]`;
        }
        logEvent('slowrequest', msg);
    } else if (isTimeout) {
        // Request completed but exceeded the 30s critical threshold
        let msg = `Slow request #${data.requestNumber} completed: ${durationSec}s (${scenario}) [Queue Time: ${queueSec}s] ⚠️ CRITICAL (>30s)`;
        logEvent('slowrequest', msg);
    } else {
        let msg = `Slow request #${data.requestNumber} completed: ${durationSec}s (${scenario}) [Queue Time: ${queueSec}s]`;
        logEvent('slowrequest', msg);
    }
}

/**
 * Handle incoming load test statistics from server.
 * Broadcast every 60 seconds while /api/loadtest endpoint is receiving traffic.
 */
function handleLoadTestStats(data) {
    const completed = data.requestsCompleted;
    const avgMs = data.avgResponseTimeMs;
    const maxMs = data.maxResponseTimeMs;
    const rps = data.requestsPerSecond;
    const exceptions = data.exceptionCount;
    
    // Calculate error rate percentage
    const errorRate = completed > 0 ? (exceptions / completed) * 100 : 0;
    
    // Build detailed stats message matching screenshot format
    let msg = `Load test period stats (60s): ${completed.toLocaleString()} requests, ` +
              `${avgMs.toFixed(1)} avg ms, ${maxMs.toFixed(0)} max ms, ` +
              `${rps.toFixed(2)} RPS, ${errorRate.toFixed(1)}% errors`;
    
    logEvent('loadtest', msg);
}

/**
 * Handle idle state change notification from server.
 * When the server goes idle, health probes stop. When it wakes up, they resume.
 */
function handleIdleState(data) {
    const wasIdle = state.isIdle;
    state.isIdle = data.isIdle;
    
    // Log the state change
    if (data.isIdle && !wasIdle) {
        // Going idle
        logEvent('idle', data.message || 'Application going idle, no health probes being sent. There will be gaps in diagnostics and logs.');
        stopClientProbe();
    } else if (!data.isIdle && wasIdle) {
        // Waking up (client knew we were idle)
        logEvent('system', data.message || 'App waking up from idle state. There may be gaps in diagnostics and logs.');
        startClientProbe();
    } else if (!data.isIdle && !wasIdle && data.message && data.message.toLowerCase().includes('waking up')) {
        // Server was idle but client didn't know (e.g., after reconnect)
        // The server's message indicates it just woke up
        logEvent('system', data.message);
        startClientProbe();
    }
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
        avgEl.className = `latency-value ${getLatencyClass(avg, false)}`;
    }
    
    // Calculate max
    const maxEl = document.getElementById('latencyMax');
    if (maxEl && history.values.length > 0) {
        const max = Math.max(...history.values);
        maxEl.textContent = formatLatency(max);
        
        // precise timeout check for max value could be complex, 
        // but high latency > 1s will be red anyway which is sufficient
        maxEl.className = `latency-value ${getLatencyClass(max, false)}`;
    }
    
    // Update timeout count
    const timeoutsEl = document.getElementById('latencyTimeouts');
    if (timeoutsEl) {
        timeoutsEl.textContent = state.latencyStats.timeoutCount;
        if (state.latencyStats.timeoutCount > 0) {
            timeoutsEl.className = 'latency-value timeout';
        } else {
            timeoutsEl.className = 'latency-value';
        }
    }
}

/**
 * Format latency value for display with dynamic units.
 */
function formatLatency(ms) {
    if (ms >= 10000) {
        return (ms / 1000).toFixed(1) + 's';
    } else if (ms >= 1000) {
        return (ms / 1000).toFixed(2) + 's';
    } else {
        return ms.toFixed(1) + 'ms';
    }
}

/**
 * Get CSS class based on latency value.
 */
function getLatencyClass(ms, isTimeout) {
    if (isTimeout) return 'timeout';
    if (ms > 1000) return 'danger';
    if (ms > 150) return 'warning';
    return 'good';
}

/**
 * Updates probe visualization dots (24-dot history indicator)
 * Shows visual history of recent latency measurements next to the title
 */
function updateProbeVisualization(latency) {
    let status = 'good';
    if (latency >= 30000) status = 'failed';
    else if (latency >= 1000) status = 'slow';
    else if (latency >= 150) status = 'degraded';

    probeHistory.push(status);
    if (probeHistory.length > MAX_PROBE_DOTS) {
        probeHistory.shift();
    }

    const vizEl = document.getElementById('probe-visualization');
    if (vizEl) {
        vizEl.innerHTML = probeHistory.map(s =>
            `<span class="probe-dot-inline ${s === 'good' ? '' : s}"></span>`
        ).join('');
    }
}

/**
 * Update the latency chart.
 */
function updateLatencyChart() {
    if (!state.charts.latency) return;
    
    const history = state.latencyHistory;
    
    // Create gradient based on chart's actual dimensions
    const ctx = state.charts.latency.ctx;
    const chartArea = state.charts.latency.chartArea;
    const gradientHeight = chartArea ? (chartArea.bottom - chartArea.top) : 200;
    const gradient = ctx.createLinearGradient(0, 0, 0, gradientHeight);
    gradient.addColorStop(0, 'rgba(209, 52, 56, 0.3)');   // Red at top (high latency)
    gradient.addColorStop(0.5, 'rgba(255, 185, 0, 0.2)'); // Yellow in middle
    gradient.addColorStop(1, 'rgba(16, 124, 16, 0.1)');   // Green at bottom (low latency)
    
    // Map data points to colors based on latency
    const pointColors = history.values.map((v, i) => {
        if (history.isTimeout[i]) return '#d13438';
        if (v > 1000) return '#d13438';
        if (v > 150) return '#ffb900';
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
            if (latency > 1000) return '#d13438';
            if (latency > 150) return '#ffb900';
            return '#107c10';
        }
    };
    state.charts.latency.update('none');
}

/**
 * Start client-side probe as a backup/additional measurement.
 * This runs independently in the browser and won't be affected by server thread pool issues.
 * 
 * Hybrid Probing Strategy:
 * - Server probes at CONFIG.latencyProbeIntervalMs (e.g., 200ms)
 * - Client probes at same interval, but offset by half (e.g., start at 100ms, then 200ms intervals)
 * - Combined effect: ~100ms effective sample rate with half the server-side overhead
 */
function startClientProbe() {
    // Don't start probes if server is idle
    if (state.isIdle) return;
    
    // Idempotency check: Don't start if already running
    if (state.isClientProbeRunning) return;

    // Use a flag to track running state
    state.isClientProbeRunning = true;
    
    const runProbe = async () => {
        if (!state.isClientProbeRunning) return;
        
        // Stop if server went idle
        if (state.isIdle) {
            stopClientProbe();
            return;
        }

        try {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), CONFIG.latencyTimeoutMs);
            
            try {
                await fetch(`${CONFIG.apiBaseUrl}/health/probe`, {
                    signal: controller.signal
                });
                clearTimeout(timeoutId);
            } catch (e) {
                clearTimeout(timeoutId);
                // Ignore errors/aborts
            }
        } catch (err) {
            // Ignore network errors
        }

        // Schedule next probe ONLY after this one completes (Closed Loop)
        if (state.isClientProbeRunning) {
            state.clientProbeTimeout = setTimeout(runProbe, CONFIG.latencyProbeIntervalMs);
        }
    };

    // Start with half-interval offset to interleave with server probes
    // Server probes at: 0ms, 200ms, 400ms, ...
    // Client probes at: 100ms, 300ms, 500ms, ... (offset by half)
    const offsetMs = Math.floor(CONFIG.latencyProbeIntervalMs / 2);
    state.clientProbeTimeout = setTimeout(runProbe, offsetMs);
}

/**
 * Stop client-side probe.
 */
function stopClientProbe() {
    state.isClientProbeRunning = false;
    if (state.clientProbeTimeout) {
        clearTimeout(state.clientProbeTimeout);
        state.clientProbeTimeout = null;
    }
}

// ==========================================================================
// Simulation Controls
// ==========================================================================

async function triggerCpuStress() {
    const duration = parseInt(document.getElementById('cpuDuration').value) || 30;
    const level = document.getElementById('cpuLevel').value || 'high';
    
    try {
        logEvent('cpu', `Triggering CPU stress for ${duration} seconds (${level})...`);
        const response = await fetch(`${CONFIG.apiBaseUrl}/cpu/trigger-high-cpu`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                durationSeconds: duration,
                level: level
            })
        });
        
        if (response.ok) {
            const result = await response.json();
            const displayLevel = level.charAt(0).toUpperCase() + level.slice(1);
            addActiveSimulation(result.simulationId, 'cpu', `CPU Stress (${displayLevel})`);
            logEvent('cpu', withSimulationId(`CPU stress started (${displayLevel})`, result.simulationId));
        } else {
            const error = await response.json();
            logEvent('cpu', `Failed: ${error.detail || 'Unknown error'}`);
        }
    } catch (err) {
        logEvent('cpu', `Request failed: ${err.message}`);
    }
}

/**
 * Stops all active CPU stress simulations.
 */
async function stopCpuStress() {
    try {
        logEvent('cpu', 'Stopping CPU stress simulations...');
        const response = await fetch(`${CONFIG.apiBaseUrl}/cpu/stop`, {
            method: 'POST'
        });
        
        if (response.ok) {
            const result = await response.json();
            logEvent('cpu', result.message || 'CPU stress stopped');
            // Remove CPU simulations from active list
            removeSimulationsByType('cpu');
        } else {
            const error = await response.json();
            logEvent('cpu', `Stop request: ${error.detail || 'May have already stopped'}`);
        }
    } catch (err) {
        logEvent('cpu', `Stop request: ${err.message || 'May have already stopped'}`);
    }
}

async function allocateMemory() {
    const sizeMb = parseInt(document.getElementById('memorySize').value) || 100;
    
    try {
        logEvent('memory', `Allocating ${sizeMb} MB of memory...`);
        const response = await fetch(`${CONFIG.apiBaseUrl}/memory/allocate-memory`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sizeMegabytes: sizeMb })
        });
        
        if (response.ok) {
            const result = await response.json();
            const actualSizeMb = result.actualParameters?.sizeMegabytes ?? sizeMb;
            addActiveSimulation(result.simulationId, 'memory', `Memory ${actualSizeMb}MB`);
            logEvent('memory', withSimulationId(`Memory allocated (${actualSizeMb} MB)`, result.simulationId));
        } else {
            const error = await response.json();
            logEvent('memory', `Failed: ${error.detail || 'Unknown error'}`);
        }
    } catch (err) {
        logEvent('memory', `Request failed: ${err.message}`);
    }
}

async function releaseMemory() {
    try {
        logEvent('memory', 'Releasing all allocated memory...');
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
            logEvent('memory', `Released ${result.releasedBlockCount ?? 0} blocks (${releasedMb.toFixed(1)} MB)`);
        } else {
            const error = await response.json();
            logEvent('memory', `Failed: ${error.detail || 'Unknown error'}`);
        }
    } catch (err) {
        logEvent('memory', `Request failed: ${err.message}`);
    }
}

async function triggerThreadBlock() {
    const delaySeconds = parseFloat(document.getElementById('threadDelay').value) || 10;
    const delayMs = Math.round(delaySeconds * 1000);
    const concurrent = parseInt(document.getElementById('threadConcurrent').value) || 100;
    
    try {
        logEvent('threads', `Triggering thread blocking: ${concurrent} requests, ${delaySeconds}s delay...`);
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
            logEvent('threads', withSimulationId(`Thread blocking started`, result.simulationId));
        } else {
            const error = await response.json();
            logEvent('threads', `Failed: ${error.detail || 'Unknown error'}`);
        }
    } catch (err) {
        logEvent('threads', `Request failed: ${err.message}`);
    }
}

/**
 * Triggers an application crash.
 * WARNING: This will terminate the application!
 */
async function triggerCrash() {
    const crashType = document.getElementById('crashType').value;
    // Delay option removed from UI - default to 0 (immediate crash)
    const delayElement = document.getElementById('crashDelay');
    const delaySeconds = delayElement ? parseInt(delayElement.value) || 0 : 0;
    
    // Confirmation dialog - always synchronous for Azure Crash Monitoring
    const confirmed = confirm(
        `⚠️ WARNING: This will CRASH the application!\n\n` +
        `Crash Type: ${crashType}\n` +
        `\nThe application will terminate and Azure will auto-restart it.\n` +
        `✓ Azure Crash Monitoring will capture this crash.\n` +
        `\nAre you sure you want to proceed?`
    );
    
    if (!confirmed) {
        logEvent('crash', 'Crash cancelled by user');
        return;
    }
    
    try {
        logEvent('crash', `CRASH: ${crashType}${delaySeconds > 0 ? ` in ${delaySeconds}s` : ''} - Connection will be lost!`);
        
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
            logEvent('crash', `💀 ${result.message}`);
            
            // Show countdown for async crashes
            if (!synchronous && delaySeconds > 0) {
                let countdown = delaySeconds;
                const countdownInterval = setInterval(() => {
                    countdown--;
                    if (countdown > 0) {
                        logEvent('crash', `Crash in ${countdown}...`);
                    } else {
                        logEvent('crash', 'CRASHING NOW!');
                        clearInterval(countdownInterval);
                    }
                }, 1000);
            }
        } else {
            const error = await response.json();
            logEvent('crash', `Failed: ${error.message || 'Unknown error'}`);
        }
    } catch (err) {
        // For synchronous crashes, a network error is expected (connection lost)
        if (synchronous) {
            logEvent('crash', 'Application crashed! Connection lost. Waiting for restart...');
        } else {
            logEvent('crash', `Request failed: ${err.message}`);
        }
    }
}

// ==========================================================================
// Slow Request Simulator
// ==========================================================================

/**
 * Starts the slow request simulator.
 * Generates requests with sync-over-async patterns for CLR Profiler analysis.
 */
async function startSlowRequests() {
    const durationSeconds = parseInt(document.getElementById('slowRequestDuration').value) || 25;
    const intervalSeconds = parseInt(document.getElementById('slowRequestInterval').value) || 2;
    const maxRequests = parseInt(document.getElementById('slowRequestMax').value) || 10;
    
    const statusDiv = document.getElementById('slowRequestStatus');
    const startBtn = document.getElementById('btnStartSlowRequests');
    const stopBtn = document.getElementById('btnStopSlowRequests');
    
    try {
        logEvent('slowrequest', `Starting slow request simulator: ${durationSeconds}s requests, ${intervalSeconds}s interval, max ${maxRequests}`);
        
        startBtn.disabled = true;
        stopBtn.disabled = false;
        
        const response = await fetch(`${CONFIG.apiBaseUrl}/slowrequest/start`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                requestDurationSeconds: durationSeconds,
                intervalSeconds: intervalSeconds,
                maxRequests: maxRequests
            })
        });
        
        if (response.ok) {
            const result = await response.json();
            // Note: Don't log result.message - "Starting slow request simulator" already logged above
            statusDiv.textContent = `Running: ${durationSeconds}s requests every ${intervalSeconds}s (max ${maxRequests})`;
            statusDiv.classList.add('active');
            
            // Start polling for status
            pollSlowRequestStatus();
        } else {
            const error = await response.json();
            logEvent('slowrequest', `Failed to start: ${error.message || error.title || 'Unknown error'}`);
            startBtn.disabled = false;
            stopBtn.disabled = true;
            statusDiv.classList.remove('active');
        }
    } catch (err) {
        logEvent('slowrequest', `Request failed: ${err.message}`);
        startBtn.disabled = false;
        stopBtn.disabled = true;
        statusDiv.classList.remove('active');
    }
}

/**
 * Stops the slow request simulator.
 */
async function stopSlowRequests() {
    const statusDiv = document.getElementById('slowRequestStatus');
    const startBtn = document.getElementById('btnStartSlowRequests');
    const stopBtn = document.getElementById('btnStopSlowRequests');
    
    try {
        logEvent('slowrequest', 'Stopping slow request simulator...');
        
        const response = await fetch(`${CONFIG.apiBaseUrl}/slowrequest/stop`, {
            method: 'POST'
        });
        
        if (response.ok) {
            const result = await response.json();
            logEvent('slowrequest', `${result.message}`);
        } else {
            const error = await response.json();
            logEvent('slowrequest', `Stop request: ${error.message || 'May have already stopped'}`);
        }
    } catch (err) {
        logEvent('slowrequest', `Request failed: ${err.message}`);
    } finally {
        startBtn.disabled = false;
        stopBtn.disabled = true;
        statusDiv.textContent = '';
        statusDiv.classList.remove('active');
    }
}

/**
 * Polls the slow request status and updates UI.
 */
async function pollSlowRequestStatus() {
    const statusDiv = document.getElementById('slowRequestStatus');
    const startBtn = document.getElementById('btnStartSlowRequests');
    const stopBtn = document.getElementById('btnStopSlowRequests');
    
    try {
        const response = await fetch(`${CONFIG.apiBaseUrl}/slowrequest/status`);
        if (response.ok) {
            const status = await response.json();
            
            if (status.isRunning) {
                statusDiv.textContent = `Running: ${status.requestsCompleted}/${status.requestsSent} completed, ${status.requestsInProgress} active`;
                statusDiv.classList.add('active');

                // Ensure overlay is active if running (in case page was refreshed)
                const overlay = document.getElementById('latencyOverlay');
                const msg = document.getElementById('latencySuspendedMsg');
                if (overlay && !overlay.classList.contains('active')) {
                     overlay.classList.add('active');
                }
                if (msg && msg.style.display === 'none') {
                    msg.style.display = 'block';
                }
                
                // Continue polling
                setTimeout(pollSlowRequestStatus, 1000);
            } else {
                // Simulation ended
                statusDiv.textContent = `Completed: ${status.requestsCompleted}/${status.requestsSent} requests`;
                setTimeout(() => {
                    statusDiv.classList.remove('active');
                    statusDiv.textContent = '';
                }, 3000);
                
                startBtn.disabled = false;
                stopBtn.disabled = true;
                
                // Hide overlay when simulation is confirmed done via polling
                const overlay = document.getElementById('latencyOverlay');
                const msg = document.getElementById('latencySuspendedMsg');
                if (overlay) overlay.classList.remove('active');
                if (msg) msg.style.display = 'none';

                if (status.requestsCompleted > 0) {
                    logEvent('slowrequest', `Slow request simulation completed: ${status.requestsCompleted} requests`);
                }
            }
        }
    } catch (err) {
        // Connection lost - probably a restart
        statusDiv.classList.remove('active');
        startBtn.disabled = false;
        stopBtn.disabled = true;
    }
}

// ==========================================================================
// Active Simulations UI
// ==========================================================================

/**
 * Maps simulation types to their log categories
 */
const SIMULATION_CATEGORY_MAP = {
    'cpu': 'cpu',
    'memory': 'memory',
    'threadblock': 'threads',
    'slowrequest': 'slowrequest',
    'crash': 'crash'
};

function handleSimulationStarted(simulationType, simulationId) {
    const simTypeLower = simulationType.toLowerCase();
    addActiveSimulation(simulationId, simTypeLower, simulationType);
    // Note: Don't log here - the API response handlers already log the start message

    // Handle SlowRequest specific UI
    if (simulationType === 'SlowRequest') {
        const overlay = document.getElementById('latencyOverlay');
        const statusOverlay = document.getElementById('slowRequestStatus');
        const msg = document.getElementById('latencySuspendedMsg');

        if (overlay) overlay.classList.add('active');
        if (statusOverlay) statusOverlay.classList.add('active');
        if (msg) msg.style.display = 'block';
        
        // Stop client probe to ensure clean profile
        stopClientProbe();
    }
}

function handleSimulationCompleted(simulationType, simulationId) {
    removeActiveSimulation(simulationId);
    const simTypeLower = simulationType.toLowerCase();
    const category = SIMULATION_CATEGORY_MAP[simTypeLower] || 'system';
    
    // Don't log SlowRequest completion here - the polling handler logs with request count
    if (simulationType !== 'SlowRequest') {
        logEvent(category, withSimulationId(`${simulationType} simulation completed`, simulationId));
    }

    // Handle SlowRequest specific UI
    if (simulationType === 'SlowRequest') {
        const overlay = document.getElementById('latencyOverlay');
        const statusOverlay = document.getElementById('slowRequestStatus');
        const msg = document.getElementById('latencySuspendedMsg');

        if (overlay) overlay.classList.remove('active');
        if (msg) msg.style.display = 'none';
        
        // Let the status overlay hang around for a few seconds via the polling loop instead of hiding immediately
        // The polling loop will handle the 'Running' -> 'Completed' text update.
    }
}

function addActiveSimulation(id, type, label) {
    state.activeSimulations.set(id, { type, label, startTime: new Date() });
    updateActiveSimulationsUI();
}

function removeActiveSimulation(id) {
    state.activeSimulations.delete(id);
    updateActiveSimulationsUI();
}

function removeSimulationsByType(type) {
    state.activeSimulations.forEach((value, key) => {
        if (value.type === type) {
            state.activeSimulations.delete(key);
        }
    });
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

/**
 * Category icons for event log entries
 */
const LOG_ICONS = {
    cpu: '🔥',
    memory: '📊',
    threads: '🧵',
    slowrequest: '🐌',
    crash: '💥',
    loadtest: '📈'
};

/**
 * Wraps a message with a simulation ID tooltip for correlation.
 * @param {string} message - The message to display
 * @param {string} simulationId - The full GUID simulation ID (shown in tooltip)
 * @returns {string} HTML string with message and tooltip
 */
function withSimulationId(message, simulationId) {
    if (!simulationId) return message;
    return `<span class="sim-msg" title="Simulation ID: ${simulationId}">${message}</span>`;
}

/**
 * Logs an event to the event log panel.
 * @param {string} levelOrCategory - Log level ('info','success','warning','error') or category ('cpu','memory','threads','slowrequest','crash','loadtest','system')
 * @param {string} message - The message to log
 * @param {Object} [options] - Optional settings
 * @param {string} [options.icon] - Override the default icon for this category
 */
function logEvent(levelOrCategory, message, options = {}) {
    const log = document.getElementById('eventLog');
    const time = getCurrentUtcTime();
    
    // Determine CSS class and icon based on category
    let cssClass = levelOrCategory;
    let icon = options.icon || LOG_ICONS[levelOrCategory] || '';
    
    const entry = document.createElement('div');
    entry.className = `log-entry ${cssClass}`;
    
    const iconHtml = icon ? `<span class="log-icon">${icon}</span>` : '';
    entry.innerHTML = `<span class="log-time">${time} UTC</span>${iconHtml}${message}`;
    
    log.insertBefore(entry, log.firstChild);
    
    // Limit log entries
    while (log.children.length > 50) {
        log.removeChild(log.lastChild);
    }
}

// ==========================================================================
// Initialization
// ==========================================================================

/**
 * Fetches and displays the Azure SKU info.
 */
async function fetchAzureSku() {
    try {
        const response = await fetch(`${CONFIG.apiBaseUrl}/admin/stats`);
        if (response.ok) {
            const data = await response.json();
            const skuElement = document.getElementById('skuDisplay');
            if (skuElement && data.processInfo && data.processInfo.azureSku) {
                skuElement.textContent = `SKU: ${data.processInfo.azureSku}`;
                skuElement.style.display = 'block';
            }
            
            // Log SKU/worker info on page load
            if (data.processInfo) {
                const sku = data.processInfo.azureSku;
                const computerName = data.processInfo.computerName;
                
                if (sku === 'Local' || !computerName) {
                    logEvent('system', 'Application is currently running on Local');
                } else {
                    logEvent('system', `Application is currently running on ${sku} SKU on worker ${computerName}`);
                }
            }
        }
    } catch (error) {
        console.error('Failed to fetch Azure SKU', error);
    }
}

/**
 * Fetches and displays the build timestamp.
 */
async function fetchBuildInfo() {
    try {
        const response = await fetch(`${CONFIG.apiBaseUrl}/health/build`);
        if (response.ok) {
            const data = await response.json();
            if (data.buildTimestamp) {
                // Parse ISO 8601 timestamp and format as date + time
                const buildDate = new Date(data.buildTimestamp);
                const formatted = buildDate.toISOString().replace('T', ' ').substring(0, 19) + ' UTC';
                
                // Update footer build time
                const buildTimeElement = document.getElementById('buildTime');
                if (buildTimeElement) buildTimeElement.textContent = formatted;
                
                // Update sidebar build time
                const sidebarBuildTime = document.getElementById('sidebarBuildTime');
                if (sidebarBuildTime) sidebarBuildTime.textContent = formatted;
            }
        }
    } catch (error) {
        console.error('Failed to fetch build info', error);
    }
}

/**
 * Fetches app configuration and updates the UI.
 * The PageFooter can be set via Azure App Service environment variable: PAGE_FOOTER
 * LatencyProbeIntervalMs configures hybrid probing rate.
 */
async function fetchAppConfig() {
    try {
        const response = await fetch(`${CONFIG.apiBaseUrl}/config`);
        if (response.ok) {
            const config = await response.json();
            
            // Update probe interval for hybrid probing (server sends its interval)
            if (config.latencyProbeIntervalMs && config.latencyProbeIntervalMs > 0) {
                CONFIG.latencyProbeIntervalMs = config.latencyProbeIntervalMs;
                console.log(`Latency probe interval set to ${CONFIG.latencyProbeIntervalMs}ms (hybrid effective: ~${Math.floor(CONFIG.latencyProbeIntervalMs / 2)}ms)`);
            }
            
            // Update page footer if configured
            const footerElement = document.getElementById('pageFooterContent');
            if (footerElement) {
                if (config.pageFooter && config.pageFooter.trim()) {
                    footerElement.innerHTML = config.pageFooter;
                } else {
                    // Hide the empty paragraph if no footer is configured
                    footerElement.style.display = 'none';
                }
            }
        }
    } catch (error) {
        console.error('Failed to fetch app config', error);
    }
}

document.addEventListener('DOMContentLoaded', async () => {
    // Initialize charts first
    initializeCharts();
    
    // Fetch app configuration BEFORE starting probes (sets probe interval for hybrid mode)
    await fetchAppConfig();
    
    // Fetch SKU info (non-blocking)
    fetchAzureSku();
    
    // Fetch build info (non-blocking)
    fetchBuildInfo();
    
    // Start SignalR connection (this starts client probes with correct interval)
    initializeSignalR();

    
    // Wire up button handlers
    document.getElementById('btnTriggerCpu').addEventListener('click', triggerCpuStress);
    document.getElementById('btnStopCpu').addEventListener('click', stopCpuStress);
    document.getElementById('btnAllocateMemory').addEventListener('click', allocateMemory);
    document.getElementById('btnReleaseMemory').addEventListener('click', releaseMemory);
    document.getElementById('btnTriggerThreadBlock').addEventListener('click', triggerThreadBlock);
    document.getElementById('btnTriggerCrash').addEventListener('click', triggerCrash);
    document.getElementById('btnStartSlowRequests').addEventListener('click', startSlowRequests);
    document.getElementById('btnStopSlowRequests').addEventListener('click', stopSlowRequests);
    
    // Initialize slow request Stop button as disabled
    document.getElementById('btnStopSlowRequests').disabled = true;
    
    // Wire up side panel toggle
    initializeSidePanel();
    
    logEvent('system', 'Dashboard initialized');
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

/**
 * Clear all active simulations from state and UI.
 */
function clearAllActiveSimulations() {
    state.activeSimulations.clear();
    updateActiveSimulationsUI();
}
