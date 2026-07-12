using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Goldpath.Analyzers;

/// <summary>
/// GP1801: TWO DbContexts in the same assembly call the same Goldpath model contribution
/// and neither (or only the first) owns it — double DDL ownership of shared tables
/// (migrations RFC D3: one table set, ONE owner). Honest limitation: Roslyn sees ONE
/// compilation, so cross-project double-ownership (api + worker) is guarded by the
/// template/CLI generating the exclusion, not by this rule.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SharedTablesOwnershipAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] SharedContributions =
        ["AddGoldpathJobs", "AddGoldpathArchiveModel", "AddGoldpathBulk", "AddGoldpathNotification", "AddGoldpathCampaign"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.SharedTablesDoubleOwnership);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            // Collect every OWNING map, decide at compilation end — concurrency-safe and
            // DETERMINISTIC (the alphabetically-first context is called the owner; every
            // other owning map is the finding).
            var owningMaps = new System.Collections.Concurrent.ConcurrentBag<(string Contribution, string Context, Location Location)>();

            compilationContext.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.Node is not InvocationExpressionSyntax invocation
                    || invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name }
                    || !SharedContributions.Contains(name))
                {
                    return;
                }

                // excludeFromMigrations: true = a non-owner map — always fine. Matched BY
                // NAME (or as the lone positional argument of today's single-flag shape):
                // "any true literal anywhere" would go blind the moment a contribution
                // grows a second bool (review-agent finding on PR #1 — accepted).
                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    if (!argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression))
                    {
                        continue;
                    }

                    var argumentName = argument.NameColon?.Name.Identifier.ValueText;
                    if (argumentName == "excludeFromMigrations"
                        || (argumentName is null && invocation.ArgumentList.Arguments.Count == 1))
                    {
                        return;
                    }
                }

                var declaringClass = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                if (declaringClass is null)
                {
                    return;
                }

                owningMaps.Add((name, declaringClass.Identifier.ValueText, invocation.GetLocation()));
            }, SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                foreach (var group in owningMaps.GroupBy(m => m.Contribution))
                {
                    var owner = group.Select(m => m.Context).OrderBy(c => c, System.StringComparer.Ordinal).First();
                    foreach (var map in group.Where(m => m.Context != owner))
                    {
                        endContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.SharedTablesDoubleOwnership, map.Location,
                            map.Context, map.Contribution, owner));
                    }
                }
            });
        });
    }
}
