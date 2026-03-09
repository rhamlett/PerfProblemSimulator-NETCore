using System.Timers;
using Timer = System.Timers.Timer;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Implementation of idle state management to reduce unnecessary network traffic.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// Manages application idle state to prevent unnecessary health probe traffic when
/// the dashboard is not actively being used. This reduces noise in AppLens and
/// Application Insights diagnostics.
/// </para>
/// <para>
/// <strong>ALGORITHM:</strong>
/// <list type="number">
/// <item>Timer checks for inactivity every minute</item>
/// <item>If no activity for the configured timeout (default 20 min), go idle</item>
/// <item>Fire GoingIdle event to notify services to stop probing</item>
/// <item>When dashboard reconnects or activity occurs, wake up</item>
/// <item>Fire WakingUp event to notify services to resume probing</item>
/// </list>
/// </para>
/// <para>
/// <strong>THREAD SAFETY:</strong>
/// Uses volatile and lock-free operations where possible. The idle state flag
/// is protected by lock since it requires coordinated read-modify-write with events.
/// </para>
/// </remarks>
public class IdleStateService : IIdleStateService, IDisposable
{
    /// <summary>
    /// Default idle timeout in minutes.
    /// </summary>
    private const int DefaultIdleTimeoutMinutes = 20;

    /// <summary>
    /// Environment variable name for overriding the idle timeout.
    /// </summary>
    private const string IdleTimeoutEnvVar = "IDLE_TIMEOUT_MINUTES";

    private readonly ILogger<IdleStateService> _logger;
    private readonly Timer _idleCheckTimer;
    private readonly Lock _stateLock = new();
    
    private DateTime _lastActivityUtc;
    private bool _isIdle;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? GoingIdle;

    /// <inheritdoc />
    public event EventHandler? WakingUp;

    /// <inheritdoc />
    public bool IsIdle
    {
        get
        {
            lock (_stateLock)
            {
                return _isIdle;
            }
        }
    }

    /// <inheritdoc />
    public int IdleTimeoutMinutes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdleStateService"/> class.
    /// </summary>
    public IdleStateService(ILogger<IdleStateService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Read idle timeout from environment variable or use default
        IdleTimeoutMinutes = GetIdleTimeoutFromEnvironment();
        
        _lastActivityUtc = DateTime.UtcNow;
        _isIdle = false;

        // Check for idle state every minute
        _idleCheckTimer = new Timer(TimeSpan.FromMinutes(1).TotalMilliseconds)
        {
            AutoReset = true
        };
        _idleCheckTimer.Elapsed += OnIdleCheckTimerElapsed;
        _idleCheckTimer.Start();

        _logger.LogInformation(
            "Idle state service initialized. Timeout: {TimeoutMinutes} minutes",
            IdleTimeoutMinutes);
    }

    /// <inheritdoc />
    public void RecordActivity()
    {
        bool wasIdle;
        lock (_stateLock)
        {
            _lastActivityUtc = DateTime.UtcNow;
            wasIdle = _isIdle;

            // If we were idle, wake up
            if (_isIdle)
            {
                _isIdle = false;
            }
        }

        if (wasIdle)
        {
            _logger.LogInformation("App waking up from idle state due to recorded activity. There may be gaps in diagnostics and logs.");
            WakingUp?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public bool WakeUp()
    {
        bool wasIdle;
        lock (_stateLock)
        {
            wasIdle = _isIdle;
            if (_isIdle)
            {
                _isIdle = false;
                _lastActivityUtc = DateTime.UtcNow;
            }
        }

        if (wasIdle)
        {
            _logger.LogInformation("App waking up from idle state. There may be gaps in diagnostics and logs.");
            WakingUp?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // Not idle, but still record activity
        lock (_stateLock)
        {
            _lastActivityUtc = DateTime.UtcNow;
        }
        return false;
    }

    /// <summary>
    /// Timer callback to check if the application should go idle.
    /// </summary>
    private void OnIdleCheckTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        bool shouldGoIdle;
        lock (_stateLock)
        {
            if (_isIdle)
            {
                // Already idle
                return;
            }

            var timeSinceActivity = DateTime.UtcNow - _lastActivityUtc;
            shouldGoIdle = timeSinceActivity >= TimeSpan.FromMinutes(IdleTimeoutMinutes);

            if (shouldGoIdle)
            {
                _isIdle = true;
            }
        }

        if (shouldGoIdle)
        {
            _logger.LogWarning(
                "Application going idle, no health probes being sent. There will be gaps in diagnostics and logs.");
            GoingIdle?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Reads the idle timeout from the environment variable.
    /// </summary>
    private int GetIdleTimeoutFromEnvironment()
    {
        var envValue = Environment.GetEnvironmentVariable(IdleTimeoutEnvVar);
        
        if (string.IsNullOrEmpty(envValue))
        {
            return DefaultIdleTimeoutMinutes;
        }

        if (int.TryParse(envValue, out var timeoutMinutes) && timeoutMinutes > 0)
        {
            _logger.LogInformation(
                "Using custom idle timeout from {EnvVar}: {Minutes} minutes",
                IdleTimeoutEnvVar,
                timeoutMinutes);
            return timeoutMinutes;
        }

        _logger.LogWarning(
            "Invalid value '{Value}' for {EnvVar}, using default {Default} minutes",
            envValue,
            IdleTimeoutEnvVar,
            DefaultIdleTimeoutMinutes);
        return DefaultIdleTimeoutMinutes;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idleCheckTimer.Stop();
        _idleCheckTimer.Dispose();
    }
}
