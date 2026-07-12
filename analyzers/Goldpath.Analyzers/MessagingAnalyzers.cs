using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Goldpath.Analyzers;

/// <summary>
/// GP0401: flags publishing/sending a type that does not implement
/// <c>Goldpath.IIntegrationEvent</c> through a MassTransit endpoint (compile-time version of the
/// runtime boundary guard).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishUnmarkedAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.PublishUnmarked);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var marker = start.Compilation.GetTypeByMetadataName("Goldpath.IIntegrationEvent");
            if (marker is null)
            {
                return;   // Goldpath.Abstractions not referenced — inert
            }

            start.RegisterOperationAction(ctx =>
            {
                var invocation = (IInvocationOperation)ctx.Operation;
                var method = invocation.TargetMethod;
                if (method.Name is not ("Publish" or "Send")
                    || method.ContainingNamespace?.ToDisplayString().StartsWith("MassTransit", StringComparison.Ordinal) is not true)
                {
                    return;
                }

                var messageType = method.TypeArguments.Length == 1
                    ? method.TypeArguments[0]
                    : invocation.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 0)?.Value.Type;

                if (messageType is INamedTypeSymbol named
                    && named.TypeKind is TypeKind.Class or TypeKind.Struct
                    && named.ContainingNamespace?.ToDisplayString().StartsWith("MassTransit", StringComparison.Ordinal) is not true
                    && !named.AllInterfaces.Contains(marker, SymbolEqualityComparer.Default))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.PublishUnmarked, invocation.Syntax.GetLocation(), named.Name));
                }
            }, OperationKind.Invocation);
        });
    }
}

/// <summary>
/// GP0402: a type must live in exactly one event world — flags types implementing both
/// Mediant's <c>INotification</c> and <c>Goldpath.IIntegrationEvent</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NotificationCrossMarkedAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.NotificationCrossMarked);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var marker = start.Compilation.GetTypeByMetadataName("Goldpath.IIntegrationEvent");
            var notification = start.Compilation.GetTypeByMetadataName("Mediant.INotification");
            if (marker is null || notification is null)
            {
                return;   // one of the worlds absent — nothing to cross
            }

            start.RegisterSymbolAction(ctx =>
            {
                var type = (INamedTypeSymbol)ctx.Symbol;
                if (type.AllInterfaces.Contains(marker, SymbolEqualityComparer.Default)
                    && type.AllInterfaces.Contains(notification, SymbolEqualityComparer.Default))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.NotificationCrossMarked, type.Locations[0], type.Name));
                }
            }, SymbolKind.NamedType);
        });
    }
}
