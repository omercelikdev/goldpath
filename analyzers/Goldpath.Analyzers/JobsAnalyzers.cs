using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Goldpath.Analyzers;

/// <summary>Shared matching for the jobs rules.</summary>
internal static class JobsMatching
{
    internal static bool IsGoldpathJobMethod(SyntaxNodeAnalysisContext context, string methodName, out MethodDeclarationSyntax? method)
    {
        method = context.Node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null || method.Identifier.ValueText != methodName)
        {
            return false;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken);
        var job = context.Compilation.GetTypeByMetadataName("Goldpath.IGoldpathJob");
        return symbol?.ContainingType is { } type && job is not null
            && type.AllInterfaces.Contains(job, SymbolEqualityComparer.Default);
    }
}

/// <summary>
/// GP1301: a transaction opened inside <c>ExecuteChunkAsync</c> — checkpoint atomicity is
/// the RUNNER's job; a chunk-scoped transaction can commit work the checkpoint never saw.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChunkTransactionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.ChunkOwnTransaction);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            var invocation = (InvocationExpressionSyntax)ctx.Node;
            var name = invocation.Expression is MemberAccessExpressionSyntax member
                ? member.Name.Identifier.ValueText
                : null;
            if (name is not ("BeginTransaction" or "BeginTransactionAsync"))
            {
                return;
            }

            if (JobsMatching.IsGoldpathJobMethod(ctx, "ExecuteChunkAsync", out _))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.ChunkOwnTransaction, invocation.GetLocation()));
            }
        }, SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// GP1302: <c>AddJob&lt;T&gt;()</c> whose configuration never sets <c>Deadline</c> — every
/// scenario card's job has an SLA; without one, predicted-overrun alerts have nothing to
/// compare against.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JobDeadlineAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.JobWithoutDeadline);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            var invocation = (InvocationExpressionSyntax)ctx.Node;
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "AddJob" } generic })
            {
                return;
            }

            if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is not IMethodSymbol method
                || method.ContainingType.ToDisplayString() != "Goldpath.GoldpathJobsOptions")
            {
                return;
            }

            // A configure lambda that assigns Deadline anywhere satisfies the rule.
            var assignsDeadline = invocation.ArgumentList.Arguments.Count > 0
                && invocation.ArgumentList.Arguments[0].Expression.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Any(a => a.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Deadline" }
                        || a.Left is IdentifierNameSyntax { Identifier.ValueText: "Deadline" });
            if (!assignsDeadline)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.JobWithoutDeadline,
                    invocation.GetLocation(),
                    generic.TypeArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "job"));
            }
        }, SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// GP1303: <c>PlanAsync</c> materializing items (<c>ToList/ToArray/ToListAsync/ToArrayAsync</c>)
/// — plans should COUNT and emit range payloads; each chunk loads its own slice.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PlanMaterializationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.PlanMaterializesItems);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            var invocation = (InvocationExpressionSyntax)ctx.Node;
            var name = invocation.Expression is MemberAccessExpressionSyntax member
                ? member.Name.Identifier.ValueText
                : null;
            if (name is not ("ToList" or "ToListAsync" or "ToArray" or "ToArrayAsync"))
            {
                return;
            }

            if (JobsMatching.IsGoldpathJobMethod(ctx, "PlanAsync", out _))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.PlanMaterializesItems, invocation.GetLocation(), name));
            }
        }, SyntaxKind.InvocationExpression);
    }
}
