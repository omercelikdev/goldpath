using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>How classified values are redacted (RFC decision D2 — composed redactors).</summary>
public enum GoldpathRedactionMode
{
    /// <summary>Replace the value with the fixed <c>***</c> token (default).</summary>
    Erase,

    /// <summary>
    /// HMAC pseudonymization: the value is replaced by a keyed hash — correlation across
    /// records survives, the value does not. Requires a key (<c>Goldpath:DataProtection:HmacKey</c>).
    /// </summary>
    Hmac,
}

/// <summary>Tuning surface — bound from <c>Goldpath:DataProtection</c>.</summary>
public sealed class GoldpathDataProtectionOptions
{
    /// <summary>Active redaction mode.</summary>
    public GoldpathRedactionMode Redactor { get; set; } = GoldpathRedactionMode.Erase;

    /// <summary>Base64 key for <see cref="GoldpathRedactionMode.Hmac"/> (44+ characters).</summary>
    public string? HmacKey { get; set; }

    /// <summary>Key identifier stamped into HMAC output; bump it on rotation.</summary>
    public int HmacKeyId { get; set; } = 1;

    internal GoldpathClassificationCatalog CatalogInstance { get; } = new();

    /// <summary>Switches to HMAC pseudonymization with the given key.</summary>
    public GoldpathDataProtectionOptions UseHmacRedaction(string base64Key, int keyId = 1)
    {
        Redactor = GoldpathRedactionMode.Hmac;
        HmacKey = base64Key;
        HmacKeyId = keyId;
        return this;
    }

    /// <summary>Declares classifications for members that cannot be annotated (D4).</summary>
    public GoldpathDataProtectionOptions Catalog(Action<GoldpathClassificationCatalog> configure)
    {
        configure(CatalogInstance);
        return this;
    }
}

/// <summary>
/// Registers Ring B data protection: one classification, every sink masked. Composes
/// Microsoft.Extensions.Compliance redaction for the Goldpath taxonomy and exposes the
/// <see cref="IGoldpathDataProtector"/> seam that classification-aware sinks (AuditTrail) consume.
/// </summary>
public static class GoldpathDataProtectionExtensions
{
    /// <summary>Adds classification, the redactor set, and the masking seam.</summary>
    public static TBuilder AddGoldpathDataProtection<TBuilder>(this TBuilder builder, Action<GoldpathDataProtectionOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathDataProtectionOptions();
        builder.Configuration.GetSection("Goldpath:DataProtection").Bind(options);
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        builder.Services.AddRedaction(redaction =>
        {
            if (options.Redactor == GoldpathRedactionMode.Hmac)
            {
                if (string.IsNullOrEmpty(options.HmacKey))
                {
                    throw new InvalidOperationException(
                        "Goldpath:DataProtection HMAC redaction requires a key (options.UseHmacRedaction or Goldpath:DataProtection:HmacKey).");
                }

                redaction.SetHmacRedactor(
                    hmac =>
                    {
                        hmac.Key = options.HmacKey;
                        hmac.KeyId = options.HmacKeyId;
                    },
                    GoldpathDataClassifications.Personal,
                    GoldpathDataClassifications.Sensitive);
            }
            else
            {
                redaction.SetRedactor<GoldpathErasingRedactor>(
                    GoldpathDataClassifications.Personal,
                    GoldpathDataClassifications.Sensitive);
            }
        });

        builder.Services.AddSingleton<IGoldpathDataProtector, GoldpathDataProtector>();
        return builder;
    }
}
