using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Goldpath.Analyzers;

/// <summary>Shared matching for the bulk rules.</summary>
internal static class BulkMatching
{
    internal static bool IsAddBatch(SyntaxNodeAnalysisContext context, out GenericNameSyntax? generic)
    {
        generic = null;
        if (context.Node is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax g }
            || g.Identifier.ValueText != "AddBatch")
        {
            return false;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || method.ContainingType.ToDisplayString() != "Goldpath.GoldpathBulkOptions")
        {
            return false;
        }

        generic = g;
        return true;
    }

    internal static bool ConfigureCalls(InvocationExpressionSyntax addBatch, string methodName)
        => addBatch.ArgumentList.Arguments.Count > 1
            && addBatch.ArgumentList.Arguments[1].Expression.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } && name == methodName);

    internal static string RowTypeName(GenericNameSyntax generic)
        => generic.TypeArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "rows";
}

/// <summary>
/// GP1501: <c>AddBatch</c> whose configuration never calls <c>MaxRows</c> — an unbounded
/// intake is a denial-of-service invitation (the builder will throw at runtime; this rule
/// says it at BUILD time).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BulkCeilingAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.BulkBatchWithoutCeiling);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            if (!BulkMatching.IsAddBatch(ctx, out var generic))
            {
                return;
            }

            if (!BulkMatching.ConfigureCalls((InvocationExpressionSyntax)ctx.Node, "MaxRows"))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.BulkBatchWithoutCeiling, ctx.Node.GetLocation(), BulkMatching.RowTypeName(generic!)));
            }
        }, SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// GP1502: a row handler calling <c>SaveChanges</c>/<c>SaveChangesAsync</c> — the engine
/// writes row state BATCHED per chunk (MDM constraint 4); per-row saves fight the
/// checkpoint semantics and wreck the intake budget.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BulkHandlerSaveAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.BulkHandlerSavesPerRow);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            if (ctx.Node is not InvocationExpressionSyntax invocation
                || invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SaveChanges" or "SaveChangesAsync" })
            {
                return;
            }

            var declaringClass = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (declaringClass is null
                || ctx.SemanticModel.GetDeclaredSymbol(declaringClass, ctx.CancellationToken) is not INamedTypeSymbol type)
            {
                return;
            }

            var handlerInterface = ctx.Compilation.GetTypeByMetadataName("Goldpath.IGoldpathBulkRowHandler`1");
            if (handlerInterface is null || !type.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, handlerInterface)))
            {
                return;
            }

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptors.BulkHandlerSavesPerRow, invocation.GetLocation(), type.Name));
        }, SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// GP1503: <c>AddBatch</c> using <c>AutoApprove</c> — legitimate for imports and
/// reference data, but skipping the gate must be a VISIBLE review decision.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BulkAutoApproveAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.BulkAutoApprove);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            if (!BulkMatching.IsAddBatch(ctx, out var generic))
            {
                return;
            }

            if (BulkMatching.ConfigureCalls((InvocationExpressionSyntax)ctx.Node, "AutoApprove"))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.BulkAutoApprove, ctx.Node.GetLocation(), BulkMatching.RowTypeName(generic!)));
            }
        }, SyntaxKind.InvocationExpression);
    }
}
