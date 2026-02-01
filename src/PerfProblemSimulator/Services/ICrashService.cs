using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service interface for triggering application crashes.
/// </summary>
public interface ICrashService
{
    /// <summary>
    /// Triggers a crash of the specified type.
    /// </summary>
    /// <param name="crashType">The type of crash to trigger.</param>
    /// <param name="delaySeconds">Optional delay before crash (ignored if synchronous).</param>
    /// <param name="message">Optional message for certain crash types.</param>
    /// <param name="synchronous">If true, crashes immediately (no response sent).</param>
    /// <remarks>
    /// <strong>WARNING:</strong> This method will terminate the application process!
    /// </remarks>
    void TriggerCrash(CrashType crashType, int delaySeconds = 0, string? message = null, bool synchronous = false);

    /// <summary>
    /// Gets a description of what each crash type does.
    /// </summary>
    Dictionary<CrashType, string> GetCrashTypeDescriptions();
}
