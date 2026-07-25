using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Goldpath;

/// <summary>
/// The `Strategy = None` scheme: authenticates NOTHING (the internal-service posture is
/// unchanged), it exists so the ops policies have a scheme to answer through — a guarded
/// admin route refuses with 401/403 instead of crashing 500 on "no authenticationScheme
/// was specified" (audit A4).
/// </summary>
internal sealed class GoldpathNoneAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme name registered on the None path.</summary>
    internal const string SchemeName = "goldpath-none";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}
