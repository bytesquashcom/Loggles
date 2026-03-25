namespace Loggles.Api.Configuration;

public sealed class AuthOptions
{
    /// <summary>
    /// Static API key for Bearer token validation.
    /// If null or empty, authentication is disabled and all endpoints are publicly accessible.
    /// Set via the LOGGLES__AUTH__APIKEY environment variable or Auth:ApiKey in appsettings.
    /// </summary>
    public string? ApiKey { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);
}
