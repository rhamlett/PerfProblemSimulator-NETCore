using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Translates UI strings from English to a target language using Azure Cognitive Services Translator.
/// </summary>
/// <remarks>
/// <para>
/// <strong>HOW IT WORKS:</strong>
/// <list type="number">
/// <item>Reads the master en.json file from wwwroot/locales/</item>
/// <item>Computes a SHA256 hash of the English source</item>
/// <item>Checks if a cached translation file exists with a matching hash</item>
/// <item>If not, calls Azure Translator API to translate all strings</item>
/// <item>Writes the translated file to wwwroot/locales/{lang}.json</item>
/// </list>
/// </para>
/// <para>
/// <strong>NEVER-TRANSLATE TERMS:</strong>
/// Technical terms listed in no-translate.json are wrapped in
/// &lt;span class="notranslate"&gt; tags before sending to the API,
/// then the tags are stripped from the translated output.
/// </para>
/// </remarks>
public partial class TranslationService(
    IHttpClientFactory httpClientFactory,
    IWebHostEnvironment environment,
    IOptions<ProblemSimulatorOptions> options,
    ILogger<TranslationService> logger) : ITranslationService
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IWebHostEnvironment _environment = environment;
    private readonly ProblemSimulatorOptions _options = options.Value;
    private readonly ILogger<TranslationService> _logger = logger;

    /// <summary>
    /// Maximum number of text elements per Azure Translator API call.
    /// The API supports up to 1,000 elements per request.
    /// </summary>
    private const int MaxBatchSize = 100;

    /// <summary>
    /// Maximum total characters per Azure Translator API request.
    /// The API enforces a 50,000-character limit across all elements in a single request.
    /// </summary>
    private const int MaxBatchChars = 49_000; // Leave margin below the 50K hard limit

    public async Task<bool> EnsureTranslationAsync(string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage) || targetLanguage.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return true; // English is the source language, no translation needed
        }

        var localesPath = Path.Combine(_environment.WebRootPath, "locales");
        var enFilePath = Path.Combine(localesPath, "en.json");
        var targetFilePath = Path.Combine(localesPath, $"{targetLanguage}.json");

        // Read and parse the English source
        if (!File.Exists(enFilePath))
        {
            _logger.LogError("English source file not found: {Path}", enFilePath);
            return false;
        }

        var enContent = await File.ReadAllTextAsync(enFilePath, cancellationToken);
        var sourceHash = ComputeHash(enContent);

        // Check if a cached translation already exists with matching hash
        if (File.Exists(targetFilePath))
        {
            try
            {
                var existingContent = await File.ReadAllTextAsync(targetFilePath, cancellationToken);
                using var existingDoc = JsonDocument.Parse(existingContent);
                if (existingDoc.RootElement.TryGetProperty("_meta", out var meta) &&
                    meta.TryGetProperty("source_hash", out var hashProp) &&
                    hashProp.GetString() == sourceHash)
                {
                    _logger.LogInformation(
                        "Translation for {Language} is up to date (hash: {Hash})",
                        targetLanguage, sourceHash[..8]);
                    return true;
                }

                _logger.LogInformation(
                    "Translation for {Language} exists but source has changed, re-translating",
                    targetLanguage);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Existing translation file for {Language} is invalid, re-translating", targetLanguage);
            }
        }

        // Get translator configuration (resolved from appsettings.json + env var overrides)
        var translatorKey = _options.TranslatorApiKey;
        var translatorEndpoint = _options.TranslatorEndpoint;
        var translatorRegion = _options.TranslatorRegion;

        if (string.IsNullOrWhiteSpace(translatorKey))
        {
            _logger.LogWarning(
                "UI_LANGUAGE is set to '{Language}' but TranslatorApiKey is not configured. " +
                "Translation cannot proceed. Set TRANSLATOR_API_KEY environment variable or " +
                "TranslatorApiKey in appsettings.json to enable auto-translation.",
                targetLanguage);
            return false;
        }

        // Parse English strings (skip _meta)
        using var enDoc = JsonDocument.Parse(enContent);
        var sourceStrings = new Dictionary<string, string>();
        foreach (var prop in enDoc.RootElement.EnumerateObject())
        {
            if (prop.Name == "_meta") continue;
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                sourceStrings[prop.Name] = prop.Value.GetString()!;
            }
        }

        if (sourceStrings.Count == 0)
        {
            _logger.LogWarning("No translatable strings found in en.json");
            return false;
        }

        // Load no-translate terms
        var noTranslateTerms = await LoadNoTranslateTermsAsync(localesPath, cancellationToken);

        _logger.LogInformation(
            "Translating {Count} strings to {Language} ({NoTranslateCount} protected terms)...",
            sourceStrings.Count, targetLanguage, noTranslateTerms.Count);

        // Translate in batches
        var translatedStrings = new Dictionary<string, string>();
        var keys = sourceStrings.Keys.ToList();

        for (var i = 0; i < keys.Count; i += MaxBatchSize)
        {
            var batchKeys = keys.Skip(i).Take(MaxBatchSize).ToList();
            var batchTexts = batchKeys.Select(k => WrapNoTranslateTerms(sourceStrings[k], noTranslateTerms)).ToList();

            var translations = await TranslateBatchAsync(
                batchTexts, targetLanguage, translatorKey, translatorEndpoint, translatorRegion, cancellationToken);

            if (translations == null)
            {
                _logger.LogError("Translation API call failed for batch starting at index {Index}", i);
                return false;
            }

            for (var j = 0; j < batchKeys.Count; j++)
            {
                translatedStrings[batchKeys[j]] = StripNoTranslateTags(translations[j]);
            }
        }

        // Build output JSON
        var output = new Dictionary<string, object>
        {
            ["_meta"] = new Dictionary<string, string>
            {
                ["source_hash"] = sourceHash,
                ["source_lang"] = "en",
                ["target_lang"] = targetLanguage,
                ["generated"] = DateTime.UtcNow.ToString("o"),
                ["generator"] = "Azure Cognitive Services Translator"
            }
        };

        foreach (var kvp in translatedStrings)
        {
            output[kvp.Key] = kvp.Value;
        }

        var outputJson = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        await File.WriteAllTextAsync(targetFilePath, outputJson, cancellationToken);

        _logger.LogInformation(
            "Translation complete: {Count} strings written to {File}",
            translatedStrings.Count, targetFilePath);

        return true;
    }

    /// <summary>
    /// Calls the Azure Translator API to translate a batch of strings.
    /// Retries up to 3 times on 429 (rate limit) with exponential backoff.
    /// </summary>
    private async Task<List<string>?> TranslateBatchAsync(
        List<string> texts,
        string targetLanguage,
        string apiKey,
        string endpoint,
        string region,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Translator");

        var requestBody = texts.Select(t => new { Text = t }).ToList();
        var requestJson = JsonSerializer.Serialize(requestBody);

        var requestUrl = $"{endpoint}/translate?api-version=3.0&from=en&to={Uri.EscapeDataString(targetLanguage)}&textType=html";

        TimeSpan[] retryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

        for (var attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
                request.Headers.Add("Ocp-Apim-Subscription-Region", region);

                using var response = await client.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    var statusCode = (int)response.StatusCode;

                    // Retry on 429 (rate limit)
                    if (statusCode == 429 && attempt < retryDelays.Length)
                    {
                        var delay = retryDelays[attempt];
                        if (response.Headers.RetryAfter?.Delta is { } retryAfter)
                            delay = retryAfter;

                        _logger.LogWarning(
                            "Translator API rate limited (attempt {Attempt}/{Max}). Retrying in {Delay}s...",
                            attempt + 1, retryDelays.Length + 1, delay.TotalSeconds);

                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    _logger.LogError(
                        "Azure Translator API returned {StatusCode}: {Error}",
                        response.StatusCode, errorBody);
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);

                var results = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var translations = item.GetProperty("translations");
                    var firstTranslation = translations[0].GetProperty("text").GetString()!;
                    results.Add(firstTranslation);
                }

                return results;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to call Azure Translator API");
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads the no-translate terms list from no-translate.json.
    /// </summary>
    private static async Task<List<string>> LoadNoTranslateTermsAsync(string localesPath, CancellationToken cancellationToken)
    {
        var noTranslatePath = Path.Combine(localesPath, "no-translate.json");
        if (!File.Exists(noTranslatePath))
        {
            return [];
        }

        try
        {
            var content = await File.ReadAllTextAsync(noTranslatePath, cancellationToken);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("terms", out var termsArray))
            {
                return termsArray.EnumerateArray()
                    .Select(t => t.GetString()!)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .OrderByDescending(t => t.Length) // Longest first to avoid partial matches
                    .ToList();
            }
        }
        catch (Exception)
        {
            // Ignore malformed file
        }

        return [];
    }

    /// <summary>
    /// Wraps no-translate terms and {placeholder} tokens in notranslate spans.
    /// Terms are processed longest-first to avoid partial matches.
    /// Placeholders like {probeRate} are wrapped so the translator treats them as
    /// opaque tokens and translates the surrounding natural-language text.
    /// </summary>
    private static string WrapNoTranslateTerms(string text, List<string> terms)
    {
        // Wrap {placeholder} tokens first so the translator doesn't confuse them with
        // translatable content. Without this, strings like "Dashboard initialized
        // (probe rate: {probeRate}ms, idle timeout: {idleTimeout}m)" are treated as
        // code/template content and returned untranslated.
        text = PlaceholderRegex.Replace(text, "<span class=\"notranslate\">$0</span>");

        if (terms.Count == 0) return text;

        foreach (var term in terms)
        {
            // Use word-boundary-aware replacement to avoid breaking partial matches
            // The (?<![a-zA-Z]) and (?![a-zA-Z]) ensure we don't match inside other words
            var pattern = $@"(?<![a-zA-Z]){Regex.Escape(term)}(?![a-zA-Z])";
            text = Regex.Replace(text, pattern, $"<span class=\"notranslate\">{term}</span>");
        }

        return text;
    }

    /// <summary>Matches {placeholder} tokens used by the i18n system.</summary>
    private static readonly Regex PlaceholderRegex = new(
        @"\{[a-zA-Z_][a-zA-Z0-9_]*\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Strips notranslate span tags from translated text.
    /// Handles both straight quotes and HTML-entity quotes (&amp;quot;).
    /// </summary>
    private static string StripNoTranslateTags(string text)
    {
        // Use compiled regex instance (not source-generated) to strip notranslate spans
        return NotranslateSpanRegex.Replace(text, "$1");
    }

    private static readonly Regex NotranslateSpanRegex = new(
        @"<span\s+class\s*=\s*(?:""|&quot;|')notranslate(?:""|&quot;|')>(.*?)</span>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Computes a SHA256 hash of the input string (first 16 hex chars for brevity).
    /// </summary>
    private static string ComputeHash(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes)[..16];
    }

    /// <summary>
    /// Gets the path for a translated HTML document.
    /// For "documentation.html" with language "es", returns "documentation.es.html".
    /// </summary>
    private static string GetTranslatedHtmlPath(string sourceHtmlPath, string targetLanguage)
    {
        var dir = Path.GetDirectoryName(sourceHtmlPath)!;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(sourceHtmlPath);
        var ext = Path.GetExtension(sourceHtmlPath);
        return Path.Combine(dir, $"{nameWithoutExt}.{targetLanguage}{ext}");
    }

    public async Task<bool> EnsureDocumentTranslationAsync(
        string sourceHtmlPath, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage) || targetLanguage.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!File.Exists(sourceHtmlPath))
        {
            _logger.LogError("HTML source file not found: {Path}", sourceHtmlPath);
            return false;
        }

        var sourceContent = await File.ReadAllTextAsync(sourceHtmlPath, cancellationToken);
        var sourceHash = ComputeHash(sourceContent);
        var targetPath = GetTranslatedHtmlPath(sourceHtmlPath, targetLanguage);
        var sourceFileName = Path.GetFileName(sourceHtmlPath);

        // Check cache: look for hash comment at the top of the translated file
        if (File.Exists(targetPath))
        {
            var firstLine = (await File.ReadAllLinesAsync(targetPath, cancellationToken)).FirstOrDefault() ?? "";
            if (firstLine.Contains($"source_hash:{sourceHash}"))
            {
                _logger.LogInformation(
                    "Document translation for {File} ({Language}) is up to date (hash: {Hash})",
                    sourceFileName, targetLanguage, sourceHash[..8]);
                return true;
            }

            _logger.LogInformation(
                "Document translation for {File} ({Language}) exists but source changed, re-translating",
                sourceFileName, targetLanguage);
        }

        // Get translator configuration (resolved from appsettings.json + env var overrides)
        var translatorKey = _options.TranslatorApiKey;
        var translatorEndpoint = _options.TranslatorEndpoint;
        var translatorRegion = _options.TranslatorRegion;

        if (string.IsNullOrWhiteSpace(translatorKey))
        {
            _logger.LogWarning(
                "Cannot translate {File} to '{Language}' — TranslatorApiKey is not configured.",
                sourceFileName, targetLanguage);
            return false;
        }

        // Load no-translate terms
        var localesPath = Path.Combine(_environment.WebRootPath, "locales");
        var noTranslateTerms = await LoadNoTranslateTermsAsync(localesPath, cancellationToken);

        // Extract translatable text segments from the HTML
        var segments = ExtractTranslatableSegments(sourceContent);
        var translatableSegments = segments.Where(s => s.IsTranslatable && !string.IsNullOrWhiteSpace(s.Text)).ToList();

        if (translatableSegments.Count == 0)
        {
            _logger.LogWarning("No translatable text found in {File}", sourceFileName);
            return false;
        }

        _logger.LogInformation(
            "Translating document {File} to {Language}: {Count} text segments...",
            sourceFileName, targetLanguage, translatableSegments.Count);

        // Translate in batches using the text API.
        // Batches are split by both element count (MaxBatchSize) and total character count
        // (MaxBatchChars) because Azure enforces a 50,000-character limit per request.
        var batchIndex = 0;
        var i = 0;
        while (i < translatableSegments.Count)
        {
            var batch = new List<HtmlSegment>();
            var batchTexts = new List<string>();
            var batchCharCount = 0;

            while (i < translatableSegments.Count && batch.Count < MaxBatchSize)
            {
                var wrapped = WrapNoTranslateTerms(translatableSegments[i].Text, noTranslateTerms);
                if (batchCharCount + wrapped.Length > MaxBatchChars && batch.Count > 0)
                    break; // This segment would exceed the char limit — start a new batch

                batch.Add(translatableSegments[i]);
                batchTexts.Add(wrapped);
                batchCharCount += wrapped.Length;
                i++;
            }

            var translations = await TranslateBatchAsync(
                batchTexts, targetLanguage, translatorKey, translatorEndpoint, translatorRegion, cancellationToken);

            if (translations == null)
            {
                _logger.LogError("Document translation API call failed for {File} at batch {Index}",
                    sourceFileName, batchIndex);
                return false;
            }

            for (var j = 0; j < batch.Count; j++)
            {
                batch[j].TranslatedText = StripNoTranslateTags(translations[j]);
            }

            batchIndex++;

            // Pause between batches to avoid hitting the text API rate limit (429)
            if (i < translatableSegments.Count)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        // Reassemble the translated HTML
        var sb = new StringBuilder();
        sb.AppendLine($"<!-- source_hash:{sourceHash} lang:{targetLanguage} generated:{DateTime.UtcNow:o} -->");
        foreach (var segment in segments)
        {
            sb.Append(segment.IsTranslatable && segment.TranslatedText != null
                ? segment.TranslatedText
                : segment.Text);
        }

        await File.WriteAllTextAsync(targetPath, sb.ToString(), cancellationToken);

        _logger.LogInformation(
            "Document translation complete: {File} → {TargetFile} ({Count} segments)",
            sourceFileName, Path.GetFileName(targetPath), translatableSegments.Count);

        return true;
    }

    /// <summary>
    /// Splits an HTML document into a list of segments: translatable text and non-translatable
    /// markup (tags, code blocks, scripts, styles, SVGs). Preserves document structure exactly.
    /// </summary>
    private static List<HtmlSegment> ExtractTranslatableSegments(string html)
    {
        var segments = new List<HtmlSegment>();
        var parts = HtmlTagRegex().Split(html);

        // Track whether we're inside a no-translate element (code, pre, script, style, svg)
        var noTranslateDepth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var insideNoTranslate = false;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith('<'))
            {
                // This is an HTML tag — never translate it
                segments.Add(new HtmlSegment { Text = part, IsTranslatable = false });

                // Track entering/exiting no-translate elements
                var openMatch = NoTranslateElementOpenRegex().Match(part);
                if (openMatch.Success)
                {
                    var tagName = openMatch.Groups[1].Value.ToLowerInvariant();
                    noTranslateDepth[tagName] = noTranslateDepth.GetValueOrDefault(tagName) + 1;
                    insideNoTranslate = true;
                }
                else if (part.StartsWith("</", StringComparison.Ordinal))
                {
                    // Closing tag — extract tag name
                    var closingTag = part[2..].TrimEnd('>', ' ').ToLowerInvariant();
                    if (noTranslateDepth.ContainsKey(closingTag))
                    {
                        noTranslateDepth[closingTag]--;
                        if (noTranslateDepth[closingTag] <= 0)
                            noTranslateDepth.Remove(closingTag);
                        insideNoTranslate = noTranslateDepth.Values.Any(v => v > 0);
                    }
                }
            }
            else
            {
                // This is text content
                var shouldTranslate = !insideNoTranslate && part.Trim().Length > 0;
                segments.Add(new HtmlSegment { Text = part, IsTranslatable = shouldTranslate });
            }
        }

        return segments;
    }

    [GeneratedRegex(@"(<[^>]+>)", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"<(code|pre|script|style|svg)[\s>]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NoTranslateElementOpenRegex();

    /// <summary>
    /// Represents a segment of an HTML document — either translatable text or non-translatable markup.
    /// </summary>
    private class HtmlSegment
    {
        public required string Text { get; set; }
        public bool IsTranslatable { get; set; }
        public string? TranslatedText { get; set; }
    }
}
