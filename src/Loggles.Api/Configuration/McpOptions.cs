namespace Loggles.Api.Configuration;

public sealed class McpOptions
{
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "http";
    public bool OAuthEnabled { get; set; }
}
