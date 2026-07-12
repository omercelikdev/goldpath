using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

public class NotificationAnalyzerTests
{
    // Hermetic stubs: rules match Goldpath.IGoldpathNotifier / Goldpath.GoldpathNotificationOptions and the
    // SMTP client types by metadata name.
    private const string ModuleStubs = """
        namespace Goldpath
        {
            public interface IGoldpathNotifier { }
            public sealed class GoldpathNotificationTemplateBuilder
            {
                public GoldpathNotificationTemplateBuilder Channel(string name, System.Action<object> configure) => this;
                public GoldpathNotificationTemplateBuilder DeleteBodyAfter(System.TimeSpan period) => this;
            }
            public sealed class GoldpathNotificationOptions
            {
                public GoldpathNotificationOptions AddTemplate(string key, System.Action<GoldpathNotificationTemplateBuilder> configure) => this;
            }
        }
        namespace MailKit.Net.Smtp
        {
            public sealed class SmtpClient : System.IDisposable
            {
                public void Dispose() { }
            }
        }
        """;

    private static Task Verify<TAnalyzer>(string source, params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source + "\n" + ModuleStubs,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    [Fact]
    public Task GOLDPATH1601_flags_direct_smtp_when_the_module_is_referenced()
        => Verify<NotificationBypassAnalyzer>("""
            public static class Mailer
            {
                public static void Send()
                {
                    using var client = {|#0:new MailKit.Net.Smtp.SmtpClient()|};
                }
            }
            """,
            new DiagnosticResult(Descriptors.NotificationBypass).WithLocation(0).WithArguments("MailKit.Net.Smtp.SmtpClient"));

    [Fact]
    public Task GOLDPATH1601_is_silent_without_the_module()
    {
        // No Goldpath.IGoldpathNotifier in the compilation: direct SMTP is the app's own business.
        var test = new CSharpAnalyzerTest<NotificationBypassAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace MailKit.Net.Smtp
                {
                    public sealed class SmtpClient { }
                }
                public static class Mailer
                {
                    public static object Send() => new MailKit.Net.Smtp.SmtpClient();
                }
                """,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        return test.RunAsync();
    }

    [Fact]
    public Task GOLDPATH1602_flags_a_template_without_retention()
        => Verify<NotificationRetentionAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathNotificationOptions options)
                    => {|#0:options.AddTemplate("policy-renewal", t => t.Channel("email", c => { }))|};
            }
            """,
            new DiagnosticResult(Descriptors.NotificationTemplateWithoutRetention).WithLocation(0).WithArguments("policy-renewal"));

    [Fact]
    public Task GOLDPATH1602_is_silent_when_retention_is_declared()
        => Verify<NotificationRetentionAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathNotificationOptions options)
                    => options.AddTemplate("policy-renewal", t => t
                        .Channel("email", c => { })
                        .DeleteBodyAfter(System.TimeSpan.FromDays(90)));
            }
            """);
}
