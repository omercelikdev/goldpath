using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Goldpath.Analyzers;

/// <summary>Shared shape matching for the two newest modules' rules.</summary>
internal static class LadderAndRailShapes
{
    /// <summary>The builder invocation's configure lambda, or null when it is not written inline.</summary>
    internal static SyntaxNode? ConfigureBody(InvocationExpressionSyntax invocation, int argumentIndex)
    {
        if (invocation.ArgumentList.Arguments.Count <= argumentIndex)
        {
            return null;
        }

        return invocation.ArgumentList.Arguments[argumentIndex].Expression switch
        {
            SimpleLambdaExpressionSyntax lambda => lambda.Body,
            ParenthesizedLambdaExpressionSyntax lambda => (SyntaxNode?)lambda.Body,
            AnonymousMethodExpressionSyntax method => method.Body,
            _ => null,
        };
    }

    /// <summary>True when the configure body calls <paramref name="methodName"/> anywhere inside it.</summary>
    internal static bool BodyCalls(SyntaxNode body, string methodName)
        => body.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } && name == methodName);

    /// <summary>The first string-literal argument (a ladder or rail name) for the message.</summary>
    internal static string FirstNameArgument(InvocationExpressionSyntax invocation, string fallback)
        => invocation.ArgumentList.Arguments.Count > 0
            && invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax { Token.Value: string name }
            ? name
            : fallback;

    /// <summary>True when the invoked method belongs to the given Goldpath options type.</summary>
    internal static bool DeclaredOn(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, string containingType)
        => context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method
           && method.ContainingType.ToDisplayString() == containingType;
}

/// <summary>
/// GP1901: an authority chain built only from bounded rungs. <c>Rung(role, upToInclusive, …)</c>
/// carries a ceiling; <c>TopRung(role, …)</c> is the one that covers everything above it. A
/// ladder without a top rung routes the largest amounts to the last CEILING — the rung that
/// was supposed to stop them.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApprovalLadderAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.ApprovalLadderWithoutTopRung);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            var invocation = (InvocationExpressionSyntax)ctx.Node;
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AddLadder" }
                || !LadderAndRailShapes.DeclaredOn(ctx, invocation, "Goldpath.GoldpathApprovalsOptions"))
            {
                return;
            }

            // A configure argument written elsewhere (a method group, a stored delegate) is
            // not readable here — the rule reports what it can see, never what it guesses.
            var body = LadderAndRailShapes.ConfigureBody(invocation, 1);
            if (body is null || LadderAndRailShapes.BodyCalls(body, "TopRung"))
            {
                return;
            }

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptors.ApprovalLadderWithoutTopRung,
                invocation.GetLocation(),
                LadderAndRailShapes.FirstNameArgument(invocation, "this ladder")));
        }, SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// GP1902: the module composed without its escalation sweep. Every rung's TimeSpan is
/// enforced by <c>AddGoldpathApprovalsJobs()</c> inside the jobs block, not by the engine's
/// read path — without it the deadlines are decoration and an unattended request waits at
/// the rung it reached. Goldpath's own packages are exempt.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApprovalsEscalationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.ApprovalsWithoutEscalationSweep);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.AssemblyName?.StartsWith("Goldpath.", StringComparison.Ordinal) is true)
            {
                return;
            }

            var state = new CompositionState();
            start.RegisterOperationAction(ctx =>
            {
                var name = ((IInvocationOperation)ctx.Operation).TargetMethod.Name;
                if (name == "AddGoldpathApprovals")
                {
                    state.Composed = true;
                    state.Location ??= ctx.Operation.Syntax.GetLocation();
                }
                else if (name == "AddGoldpathApprovalsJobs")
                {
                    state.SweepScheduled = true;
                }
            }, OperationKind.Invocation);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (state is { Composed: true, SweepScheduled: false } && state.Location is { } location)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.ApprovalsWithoutEscalationSweep, location));
                }
            });
        });
    }

    private sealed class CompositionState
    {
        public volatile bool Composed;
        public volatile bool SweepScheduled;
        public Location? Location;
    }
}

/// <summary>
/// GP2101: a rail with no row contract. <c>ValidateRow</c> is what puts a bad row in the
/// quarantine WITH ITS REASON; a rail without one applies everything the parser produced,
/// and its empty quarantine reads as a clean rail.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileRailContractAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.FileRailWithoutRowContract);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static ctx =>
        {
            var invocation = (InvocationExpressionSyntax)ctx.Node;
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "AddRail" } }
                || !LadderAndRailShapes.DeclaredOn(ctx, invocation, "Goldpath.GoldpathFileExchangeOptions"))
            {
                return;
            }

            var body = LadderAndRailShapes.ConfigureBody(invocation, 1);
            if (body is null || LadderAndRailShapes.BodyCalls(body, "ValidateRow"))
            {
                return;
            }

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptors.FileRailWithoutRowContract,
                invocation.GetLocation(),
                LadderAndRailShapes.FirstNameArgument(invocation, "this rail")));
        }, SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// GP2102: the module composed with no rail at all — ledger tables and a File rails screen
/// over something that can never ingest a file. Goldpath's own packages are exempt.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileExchangeRailPresenceAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.FileExchangeWithoutRail);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.AssemblyName?.StartsWith("Goldpath.", StringComparison.Ordinal) is true)
            {
                return;
            }

            var state = new CompositionState();
            start.RegisterOperationAction(ctx =>
            {
                var name = ((IInvocationOperation)ctx.Operation).TargetMethod.Name;
                if (name == "AddGoldpathFileExchange")
                {
                    state.Composed = true;
                    state.Location ??= ctx.Operation.Syntax.GetLocation();
                }
                else if (name == "AddRail")
                {
                    state.RailDeclared = true;
                }
            }, OperationKind.Invocation);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (state is { Composed: true, RailDeclared: false } && state.Location is { } location)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.FileExchangeWithoutRail, location));
                }
            });
        });
    }

    private sealed class CompositionState
    {
        public volatile bool Composed;
        public volatile bool RailDeclared;
        public Location? Location;
    }
}
