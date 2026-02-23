using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Provides client-side configuration settings.
/// </summary>
/// <remarks>
/// This controller exposes non-sensitive configuration to the frontend,
/// allowing dynamic customization via Azure App Service environment variables.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly ProblemSimulatorOptions _options;

    public ConfigController(IOptions<ProblemSimulatorOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Gets client-side configuration settings.
    /// </summary>
    /// <returns>Configuration object with app title and other UI settings.</returns>
    /// <response code="200">Returns the configuration settings.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ClientConfig), StatusCodes.Status200OK)]
    public ActionResult<ClientConfig> GetConfig()
    {
        // PAGE_FOOTER is read directly from environment variable
        var pageFooter = Environment.GetEnvironmentVariable("PAGE_FOOTER") ?? "";
        
        return Ok(new ClientConfig
        {
            AppTitle = _options.AppTitle,
            PageFooter = pageFooter
        });
    }
}

/// <summary>
/// Configuration settings exposed to the client.
/// </summary>
public class ClientConfig
{
    /// <summary>
    /// The title displayed in the dashboard header.
    /// </summary>
    public required string AppTitle { get; init; }

    /// <summary>
    /// Custom HTML content for the page footer. Empty string if not configured.
    /// </summary>
    public string PageFooter { get; init; } = "";
}
