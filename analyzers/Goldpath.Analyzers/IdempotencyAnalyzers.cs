using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Goldpath.Analyzers;

/// <summary>
/// GP1001: Mediant commands without [Idempotent] in a compilation that composes the
/// idempotency layer (AddGoldpathIdempotency). Scoped to the COMPOSITION's compilation on
/// purpose — with compile-time composition the wiring call is the manifest's truth in code,
/// so command-only assemblies are exempt the same way entity-only assemblies are for the
/// model-wiring rules.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommandIdempotencyAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.CommandNotIdempotent);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var unmarked = new ConcurrentBag<INamedTypeSymbol>();
            var state = new WiringState();

            start.RegisterSymbolAction(ctx =>
            {
                var type = (INamedTypeSymbol)ctx.Symbol;
                if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract)
                {
                    return;
                }

                if (IdempotencyShapes.IsMediantCommand(type) && !IdempotencyShapes.HasIdempotentAttribute(type))
                {
                    unmarked.Add(type);
                }
            }, SymbolKind.NamedType);

            start.RegisterOperationAction(ctx =>
            {
                if (((IInvocationOperation)ctx.Operation).TargetMethod.Name == "AddGoldpathIdempotency")
                {
                    state.Wired = true;
                }
            }, OperationKind.Invocation);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (!state.Wired)
                {
                    return;
                }

                foreach (var type in unmarked)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.CommandNotIdempotent, type.Locations[0], type.Name));
                }
            });
        });
    }

    private sealed class WiringState
    {
        public volatile bool Wired;
    }
}

/// <summary>
/// GP1002: source-declared MassTransit consumers in a composition that builds the bus
/// (AddGoldpathMessaging) but never adds the EF inbox (AddGoldpathOutbox — the shipped
/// seam that superseded the separate inbox-filter idea). Consumer-library assemblies are
/// exempt: the rule holds the COMPOSITION accountable, where the inbox belongs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConsumerInboxAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.ConsumerWithoutInbox);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var consumers = new ConcurrentBag<INamedTypeSymbol>();
            var state = new WiringState();

            start.RegisterSymbolAction(ctx =>
            {
                var type = (INamedTypeSymbol)ctx.Symbol;
                if (type.TypeKind != TypeKind.Class || type.IsAbstract)
                {
                    return;
                }

                var isConsumer = type.AllInterfaces.Any(static i =>
                    i is { Name: "IConsumer", IsGenericType: true }
                    && i.ContainingNamespace?.ToDisplayString().StartsWith("MassTransit", StringComparison.Ordinal) is true);
                if (isConsumer)
                {
                    consumers.Add(type);
                }
            }, SymbolKind.NamedType);

            start.RegisterOperationAction(ctx =>
            {
                var name = ((IInvocationOperation)ctx.Operation).TargetMethod.Name;
                if (name == "AddGoldpathMessaging")
                {
                    state.BusComposed = true;
                }
                else if (name == "AddGoldpathOutbox")
                {
                    state.InboxWired = true;
                }
            }, OperationKind.Invocation);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (!state.BusComposed || state.InboxWired)
                {
                    return;
                }

                foreach (var type in consumers)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.ConsumerWithoutInbox, type.Locations[0], type.Name));
                }
            });
        });
    }

    private sealed class WiringState
    {
        public volatile bool BusComposed;
        public volatile bool InboxWired;
    }
}

/// <summary>
/// GP1004: [Idempotent] commands whose key is UNDECLARED and UNDETECTABLE — no Key* named
/// argument on the attribute and no natural-key-shaped property on the command, so the key
/// falls back to the full payload fingerprint (fragile under any volatile field).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IdempotentKeyAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] s_naturalKeySuffixes = ["Id", "Key", "No", "Number", "Reference"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.IdempotentKeyUndetectable);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(static ctx =>
        {
            var type = (INamedTypeSymbol)ctx.Symbol;
            if (!IdempotencyShapes.IsMediantCommand(type))
            {
                return;
            }

            var idempotent = type.GetAttributes()
                .FirstOrDefault(static a => a.AttributeClass?.Name == "IdempotentAttribute");
            if (idempotent is null)
            {
                return;
            }

            // A declared key (KeyExpression / KeyProperty / any Key* argument) settles it.
            var declaresKey = idempotent.NamedArguments.Any(static argument =>
                argument.Key.Contains("Key", StringComparison.Ordinal)
                && argument.Value.Value is string { Length: > 0 });
            if (declaresKey)
            {
                return;
            }

            var hasNaturalKey = type.GetMembers().OfType<IPropertySymbol>().Any(static property =>
                !property.IsStatic && s_naturalKeySuffixes.Any(suffix =>
                    property.Name.EndsWith(suffix, StringComparison.Ordinal)));
            if (!hasNaturalKey)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.IdempotentKeyUndetectable, type.Locations[0], type.Name));
            }
        }, SymbolKind.NamedType);
    }
}

/// <summary>The Mediant shapes both idempotency analyzers match by NAME (never by reference).</summary>
internal static class IdempotencyShapes
{
    internal static bool IsMediantCommand(INamedTypeSymbol type)
        => type.AllInterfaces.Any(static i =>
            i.Name == "ICommand"
            && i.ContainingNamespace?.ToDisplayString().StartsWith("Mediant", StringComparison.Ordinal) is true);

    internal static bool HasIdempotentAttribute(INamedTypeSymbol type)
        => type.GetAttributes().Any(static a => a.AttributeClass?.Name == "IdempotentAttribute");
}
