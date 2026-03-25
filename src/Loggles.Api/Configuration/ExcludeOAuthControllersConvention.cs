using Loggles.Api.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Loggles.Api.Configuration;

/// <summary>
/// Removes OAuth and WellKnown controllers from the application model
/// when OAuth is disabled, preventing MCP clients from discovering OAuth endpoints.
/// </summary>
public sealed class ExcludeOAuthControllersConvention : IApplicationModelConvention
{
    private static readonly HashSet<Type> OAuthControllerTypes =
    [
        typeof(OAuthController),
        typeof(WellKnownController)
    ];

    public void Apply(ApplicationModel application)
    {
        for (var i = application.Controllers.Count - 1; i >= 0; i--)
        {
            if (OAuthControllerTypes.Contains(application.Controllers[i].ControllerType))
            {
                application.Controllers.RemoveAt(i);
            }
        }
    }
}
