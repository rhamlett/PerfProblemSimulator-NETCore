namespace PerfProblemSimulator.Services;

/// <summary>
/// Service for translating UI strings and HTML documents.
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Ensures a translated locale file exists for the specified language.
    /// If the translation is missing or outdated (source changed), generates one
    /// by calling the configured translation API.
    /// </summary>
    /// <param name="targetLanguage">ISO 639-1 language code (e.g., "es", "fr", "ja")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if translation was successful or already cached; false if translation failed</returns>
    Task<bool> EnsureTranslationAsync(string targetLanguage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a translated HTML document exists for the specified language.
    /// Translates the full HTML body while preserving code blocks, scripts, styles,
    /// and technical terms. Cached as {filename}.{lang}.html in the same directory.
    /// </summary>
    /// <param name="sourceHtmlPath">Absolute path to the English HTML source file</param>
    /// <param name="targetLanguage">ISO 639-1 language code</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if translation was successful or already cached; false if translation failed</returns>
    Task<bool> EnsureDocumentTranslationAsync(string sourceHtmlPath, string targetLanguage, CancellationToken cancellationToken = default);
}
