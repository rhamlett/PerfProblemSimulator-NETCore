using Microsoft.Extensions.Options;
using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Middleware that serves translated HTML documents when available.
/// </summary>
/// <remarks>
/// <para>
/// For a request to "documentation.html" when UI_LANGUAGE is "es",
/// this middleware checks if "documentation.es.html" exists in wwwroot.
/// If it does, the request path is rewritten to serve the translated version.
/// If not, the original English file is served as-is.
/// </para>
/// <para>
/// This middleware runs before UseStaticFiles so the rewritten path
/// is picked up by the static file handler.
/// </para>
/// </remarks>
public class TranslatedHtmlMiddleware(
    RequestDelegate next,
    IWebHostEnvironment environment,
    IOptions<ProblemSimulatorOptions> options)
{
    private readonly RequestDelegate _next = next;
    private readonly string _webRootPath = environment.WebRootPath;
    private readonly string _uiLanguage = options.Value.UiLanguage;

    public async Task InvokeAsync(HttpContext context)
    {
        // Only rewrite if language is not English
        if (!_uiLanguage.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            var requestPath = context.Request.Path.Value ?? "";

            // Only intercept .html file requests (not API, hubs, etc.)
            if (requestPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                // Build the translated file name: documentation.html → documentation.es.html
                var relativePath = requestPath.TrimStart('/');
                var nameWithoutExt = Path.GetFileNameWithoutExtension(relativePath);
                var dir = Path.GetDirectoryName(relativePath) ?? "";
                var translatedFileName = $"{nameWithoutExt}.{_uiLanguage}.html";
                var translatedRelativePath = string.IsNullOrEmpty(dir)
                    ? translatedFileName
                    : Path.Combine(dir, translatedFileName);

                var translatedFullPath = Path.Combine(_webRootPath, translatedRelativePath);

                if (File.Exists(translatedFullPath))
                {
                    // Rewrite the request path to serve the translated file
                    context.Request.Path = "/" + translatedRelativePath.Replace('\\', '/');
                }
            }
        }

        await _next(context);
    }
}
