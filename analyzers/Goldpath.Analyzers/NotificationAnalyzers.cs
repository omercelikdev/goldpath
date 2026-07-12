using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Goldpath.Analyzers;

/// <summary>
/// GP1601: constructing an SMTP client directly in a compilation that references
/// Goldpath.Notification — sending around the notifier is an EVIDENCE HOLE ("did the customer
/// get it?" becomes unanswerable). The module's own channel is exempt by definition.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NotificationBypassAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] SmtpClientTypes =
    [
        "MailKit.Net.Smtp.SmtpClient",
        "System.Net.Mail.SmtpClient",
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.NotificationBypass);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            if (compilationContext.Compilation.GetTypeByMetadataName("Goldpath.IGoldpathNotifier") is null)
            {
                return;   // the module is not referenced — direct SMTP is the app's own business
            }

            var smtpTypes = SmtpClientTypes
                .Select(compilationContext.Compilation.GetTypeByMetadataName)
                .Where(t => t is not null)
                .ToArray();
            if (smtpTypes.Length == 0)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(ctx =>
            {
                var creation = (ObjectCreationExpressionSyntax)ctx.Node;
                if (ctx.SemanticModel.GetTypeInfo(creation, ctx.CancellationToken).Type is not INamedTypeSymbol type)
                {
                    return;
                }

                if (smtpTypes.Any(smtp => SymbolEqualityComparer.Default.Equals(type, smtp)))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.NotificationBypass, creation.GetLocation(), type.ToDisplayString()));
                }
            }, SyntaxKind.ObjectCreationExpression);
        });
    }
}

/// <summary>
/// GP1602: <c>AddTemplate</c> whose configuration never calls <c>DeleteBodyAfter</c> —
/// rendered personal data kept forever should be a VISIBLE decision, not a default
/// nobody made.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NotificationRetentionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.NotificationTemplateWithoutRetention);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            if (ctx.Node is not InvocationExpressionSyntax invocation
                || invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AddTemplate" })
            {
                return;
            }

            if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is not IMethodSymbol method
                || method.ContainingType.ToDisplayString() != "Goldpath.GoldpathNotificationOptions")
            {
                return;
            }

            var hasRetention = invocation.ArgumentList.Arguments.Count > 1
                && invocation.ArgumentList.Arguments[1].Expression.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "DeleteBodyAfter" });
            if (!hasRetention)
            {
                var key = invocation.ArgumentList.Arguments.Count > 0
                    ? invocation.ArgumentList.Arguments[0].Expression.ToString().Trim('"')
                    : "template";
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.NotificationTemplateWithoutRetention, invocation.GetLocation(), key));
            }
        }, SyntaxKind.InvocationExpression);
    }
}
