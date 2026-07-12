using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Goldpath.Analyzers;

/// <summary>Shared matching for the archival rules.</summary>
internal static class ArchivalMatching
{
    internal static bool IsArchivalRegistration(SyntaxNodeAnalysisContext context, string methodName, out GenericNameSyntax? generic)
    {
        generic = null;
        if (context.Node is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax g } member
            || g.Identifier.ValueText != methodName)
        {
            return false;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || method.ContainingType.ToDisplayString() != "Goldpath.GoldpathArchivalOptions")
        {
            return false;
        }

        generic = g;
        return true;
    }

    internal static bool CarriesClassifiedData(ITypeSymbol root, Compilation compilation)
    {
        var attribute = compilation.GetTypeByMetadataName("Goldpath.GoldpathPersonalDataAttribute");
        if (attribute is null)
        {
            return false;
        }

        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        return Walk(root);

        bool Walk(ITypeSymbol type)
        {
            if (!seen.Add(type))
            {
                return false;
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute)))
                {
                    return true;
                }

                // Follow the owned graph one level per navigation: element types of
                // collections and non-framework reference types.
                var target = property.Type is INamedTypeSymbol { TypeArguments.Length: 1 } named
                    && property.Type.AllInterfaces.Any(i => i.MetadataName == "IEnumerable`1")
                        ? named.TypeArguments[0]
                        : property.Type;
                if (target.TypeKind == TypeKind.Class
                    && target.SpecialType == SpecialType.None
                    && target.ContainingNamespace?.ToDisplayString().StartsWith("System", System.StringComparison.Ordinal) != true
                    && Walk(target))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// GP1401: an archive whose graph carries <c>[GoldpathPersonalData]</c> in a compilation
/// WITHOUT the DataProtection module — erasure would be impossible, and an archive that
/// cannot honor an erasure request is a liability, not a record.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArchiveWithoutDataProtectionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.ArchiveWithoutDataProtection);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            if (!ArchivalMatching.IsArchivalRegistration(ctx, "AddArchive", out var generic))
            {
                return;
            }

            if (ctx.Compilation.GetTypeByMetadataName("Goldpath.GoldpathDataProtectionExtensions") is not null)
            {
                return;   // the module is referenced — erasure has its catalog
            }

            var rootType = ctx.SemanticModel.GetTypeInfo(generic!.TypeArgumentList.Arguments[0], ctx.CancellationToken).Type
                ?? ctx.SemanticModel.GetSymbolInfo(generic.TypeArgumentList.Arguments[0], ctx.CancellationToken).Symbol as ITypeSymbol;
            if (rootType is not null && ArchivalMatching.CarriesClassifiedData(rootType, ctx.Compilation))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ArchiveWithoutDataProtection, ctx.Node.GetLocation(), rootType.Name));
            }
        }, SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// GP1402: <c>AddRowRetention</c> without a <c>Where</c> guard — age is rarely the only
/// truth; the "safe to purge" predicate must be explicit (never purge unsummarized detail).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RowRetentionGuardAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.RowRetentionWithoutGuard);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            if (!ArchivalMatching.IsArchivalRegistration(ctx, "AddRowRetention", out var generic))
            {
                return;
            }

            var invocation = (InvocationExpressionSyntax)ctx.Node;
            var hasGuard = invocation.ArgumentList.Arguments.Count > 0
                && invocation.ArgumentList.Arguments[0].Expression.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Where" });
            if (!hasGuard)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.RowRetentionWithoutGuard, invocation.GetLocation(),
                    generic!.TypeArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "rows"));
            }
        }, SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// GP1403: <c>AddArchive</c> whose configuration never calls <c>DueWhen</c> — archiving by
/// insert-age alone usually means the lifecycle was never modeled (the builder will throw
/// at runtime; this rule says it at BUILD time).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArchiveLifecycleAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.ArchiveWithoutLifecycle);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            if (!ArchivalMatching.IsArchivalRegistration(ctx, "AddArchive", out var generic))
            {
                return;
            }

            var invocation = (InvocationExpressionSyntax)ctx.Node;
            var hasDueWhen = invocation.ArgumentList.Arguments.Count > 0
                && invocation.ArgumentList.Arguments[0].Expression.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "DueWhen" });
            if (!hasDueWhen)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ArchiveWithoutLifecycle, invocation.GetLocation(),
                    generic!.TypeArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "aggregate"));
            }
        }, SyntaxKind.InvocationExpression);
    }
}
