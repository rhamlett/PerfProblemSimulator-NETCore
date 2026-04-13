using Microsoft.Extensions.Options;
using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Checks the UI_LANGUAGE setting on startup and generates a translated locale file
/// if needed. Runs once during application startup before the first request is served.
/// </summary>
/// <remarks>
/// <para>
/// <strong>FLOW:</strong>
/// <list type="number">
/// <item>Read UI_LANGUAGE environment variable (default: "en")</item>
/// <item>If "en", do nothing — English is the source language</item>
/// <item>Call TranslationService.EnsureTranslationAsync() to check/generate the locale file</item>
/// <item>If translation fails (no API key, API error), log a warning — app runs in English</item>
/// </list>
/// </para>
/// </remarks>
public class TranslationStartupService(
    ITranslationService translationService,
    IOptions<ProblemSimulatorOptions> options,
    IWebHostEnvironment environment,
    ILogger<TranslationStartupService> logger) : IHostedService
{
    private readonly ITranslationService _translationService = translationService;
    private readonly ProblemSimulatorOptions _options = options.Value;
    private readonly IWebHostEnvironment _environment = environment;
    private readonly ILogger<TranslationStartupService> _logger = logger;

    /// <summary>
    /// HTML documents in wwwroot that should be translated at startup.
    /// </summary>
    private static readonly string[] TranslatableDocuments =
    [
        "documentation.html",
        "azure-monitoring-guide.html",
        "azure-load-testing.html",
        "azure-deployment.html"
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var uiLanguage = _options.UiLanguage;

        if (uiLanguage == "en")
        {
            _logger.LogInformation("UI language is English (default), no translation needed");
            return;
        }

        // Validate ISO 639-1 code (2-3 lowercase letters)
        if (uiLanguage.Length < 2 || uiLanguage.Length > 3 || !uiLanguage.All(char.IsLetter))
        {
            _logger.LogWarning(
                "Invalid UI_LANGUAGE value '{Language}'. Expected an ISO 639-1 code (e.g., 'es', 'fr', 'ja'). Defaulting to English.",
                uiLanguage);
            return;
        }

        _logger.LogInformation("UI language set to '{Language}', checking for translations...", uiLanguage);

        // Translate dashboard UI strings (en.json → {lang}.json)
        var success = await _translationService.EnsureTranslationAsync(uiLanguage, cancellationToken);

        if (success)
        {
            _logger.LogInformation("UI translation for '{Language}' is ready", uiLanguage);
        }
        else
        {
            _logger.LogWarning(
                "Failed to ensure UI string translation for '{Language}'. " +
                "The dashboard will fall back to English.",
                uiLanguage);
        }

        // Translate HTML documentation pages (with inter-document delay to avoid rate limiting)
        var docSuccessCount = 0;
        var isFirstDoc = true;
        foreach (var docFile in TranslatableDocuments)
        {
            var sourcePath = Path.Combine(_environment.WebRootPath, docFile);
            if (!File.Exists(sourcePath))
            {
                _logger.LogDebug("Document {File} not found, skipping translation", docFile);
                continue;
            }

            // Pause between documents to stay within API rate limits
            if (!isFirstDoc)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            isFirstDoc = false;

            var docSuccess = await _translationService.EnsureDocumentTranslationAsync(
                sourcePath, uiLanguage, cancellationToken);

            if (docSuccess)
                docSuccessCount++;
            else
                _logger.LogWarning("Failed to translate document {File} to '{Language}'", docFile, uiLanguage);
        }

        _logger.LogInformation(
            "Document translation complete: {Count}/{Total} pages translated to '{Language}'",
            docSuccessCount, TranslatableDocuments.Length, uiLanguage);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
