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
    activeSimulations: new Map()
};

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
    state.connection.on('ReceiveMetrics', handleMetricsUpdate);
    state.connection.on('SimulationStarted', handleSimulationStarted);
    state.connection.on('SimulationCompleted', handleSimulationCompleted);

    // Start connection
    try {
        updateConnectionStatus('connecting', 'Connecting...');
        await state.connection.start();
        updateConnectionStatus('connected', 'Connected');
        logEvent('success', 'Connected to metrics hub');
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
    // Update metric cards
    updateMetricCard('cpu', snapshot.cpuPercent, '%', 100);
    updateMetricCard('memory', snapshot.workingSetMb, 'MB', 1000);
    updateMetricCard('threads', snapshot.threadPoolThreads, 'threads', 100);
    updateMetricCard('queue', snapshot.threadPoolQueueLength, 'pending', 100);

    // Update history for charts
    const timestamp = new Date(snapshot.timestamp);
    addToHistory(timestamp, snapshot);
    
    // Update charts
    updateCharts();
    
    // Update last update time
    document.getElementById('lastUpdate').textContent = timestamp.toLocaleTimeString();
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
    
    // Warning states
    card.classList.remove('warning', 'danger');
    if (type === 'cpu' || type === 'memory') {
        if (value > 80) card.classList.add('danger');
        else if (value > 60) card.classList.add('warning');
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
                            return date ? date.toLocaleTimeString() : '';
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
                            return date ? date.toLocaleTimeString() : '';
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
}

function updateCharts() {
    const history = state.metricsHistory;
    const labels = history.timestamps.map(t => t.toLocaleTimeString());
    
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
            body: JSON.stringify({ sizeInMegabytes: sizeMb })
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
    const time = new Date().toLocaleTimeString();
    
    const entry = document.createElement('div');
    entry.className = `log-entry ${level}`;
    entry.innerHTML = `<span class="log-time">${time}</span>${message}`;
    
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
    
    logEvent('info', 'Dashboard initialized');
});
