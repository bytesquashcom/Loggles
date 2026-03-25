namespace Loggles.Api.Configuration;

/// <summary>
/// CORS configuration. Env prefix: LOGGLES__CORS__
/// </summary>
public sealed class CorsOptions
{
    /// <summary>
    /// Comma-separated allowed origins, or "*" for any.
    /// Env: LOGGLES__CORS__ALLOWEDORIGINS
    /// </summary>
    public string AllowedOrigins { get; set; } = "*";

    public string[] GetOrigins() =>
        AllowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
