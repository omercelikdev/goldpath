using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>
/// The H2 auth floor, in ONE place: every admin mapper compiles THIS file (Goldpath.Sdk), so the fail-closed default cannot drift per package (review-agent finding on
/// PR #14 — accepted). Apply the ops policy by default; the opt-out is a visible,
/// warning-logged decision.
/// </summary>
public static class AdminSurfaceGuard
{
    /// <summary>
    /// Applies the ops floor to <paramref name="group"/>: <see cref="GoldpathPolicies.Ops"/>
    /// unless <paramref name="exposeUnsecured"/> — a visible, warning-logged opt-out.
    /// </summary>
    public static void Apply(IEndpointRouteBuilder endpoints, RouteGroupBuilder group, string prefix, bool exposeUnsecured)
    {
        if (exposeUnsecured)
        {
            endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Goldpath.AdminSurface")
                .LogWarning("{Prefix} is mapped WITHOUT the ops policy (exposeUnsecured: true) — acceptable only behind an authenticating boundary.", prefix);
        }
        else
        {
            group.RequireAuthorization(GoldpathPolicies.Ops);
        }
    }
}
