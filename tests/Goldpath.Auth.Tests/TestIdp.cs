using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Goldpath.Tests;

/// <summary>
/// In-proc IdP stub: signs REAL JWTs with a symmetric key and points JwtBearer's validation
/// parameters at it — token validation runs the genuine library path (signature, lifetime,
/// audience), no authority round-trip.
/// </summary>
internal static class TestIdp
{
    public const string Issuer = "https://idp.test";
    public const string Audience = "goldpath-api";

    private static readonly SymmetricSecurityKey s_key =
        new("goldpath-test-signing-key-32-bytes-min!!"u8.ToArray()) { KeyId = "test-key" };

    private static readonly SymmetricSecurityKey s_wrongKey =
        new("another-signing-key-32-bytes-min!!!"u8.ToArray()) { KeyId = "wrong-key" };

    public static void WireValidation(IServiceCollection services)
        => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
        {
            jwt.Authority = null;
            jwt.RequireHttpsMetadata = false;
            jwt.TokenValidationParameters.ValidIssuer = Issuer;
            jwt.TokenValidationParameters.IssuerSigningKey = s_key;
        });

    public static string Token(
        string subject = "user-1",
        string? tenant = null,
        string? role = null,
        string audience = Audience,
        TimeSpan? lifetime = null,
        bool wrongKey = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject) };
        if (tenant is not null)
        {
            claims.Add(new Claim("goldpath_tenant", tenant));
        }

        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var now = DateTime.UtcNow;
        var effectiveLifetime = lifetime ?? TimeSpan.FromMinutes(5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.Add(effectiveLifetime < TimeSpan.Zero ? effectiveLifetime * 2 : TimeSpan.Zero),
            Expires = now.Add(effectiveLifetime),
            SigningCredentials = new SigningCredentials(
                wrongKey ? s_wrongKey : s_key, SecurityAlgorithms.HmacSha256),
        });
    }
}
