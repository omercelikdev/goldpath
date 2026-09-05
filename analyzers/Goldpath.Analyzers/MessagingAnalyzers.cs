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

/// <summary>
/// GP0405: flags an adopter type implementing MassTransit's <c>IConsumer&lt;T&gt;</c> directly.
///
/// The consume seam it defends: an adopter's handler should implement Goldpath's
/// <c>IIntegrationEventHandler&lt;T&gt;</c> and register through <c>AddGoldpathHandler</c>, so
/// the day the engine changes their file does not (ADR-0013). Inert when the seam type is
/// not in the compilation (older trains); Goldpath's OWN packages are exempt — the adapter
/// behind the seam IS a consumer.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConsumerImplementedAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.ConsumerImplemented);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var consumer = start.Compilation.GetTypeByMetadataName("MassTransit.IConsumer`1");
            var seam = start.Compilation.GetTypeByMetadataName("Goldpath.IIntegrationEventHandler`1");
            if (consumer is null || seam is null)
            {
                return;   // no transport, or a train before the seam — nothing to steer toward
            }

            start.RegisterSymbolAction(ctx =>
            {
                var type = (INamedTypeSymbol)ctx.Symbol;
                if (type.TypeKind != TypeKind.Class
                    || !type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, consumer)))
                {
                    return;
                }

                // SEGMENT match, not prefix (review #159 R3): "GoldpathTemplate.Api" is adopter code.
                var containing = type.ContainingNamespace?.ToDisplayString() ?? "";
                if (containing == "Goldpath" || containing.StartsWith("Goldpath.", StringComparison.Ordinal))
                {
                    return;
                }

                ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.ConsumerImplemented, type.Locations[0], type.Name));
            }, SymbolKind.NamedType);
        });
    }
}

/// <summary>
/// GP0404: flags a constructor or member taking MassTransit's <c>IPublishEndpoint</c>.
///
/// The seam it defends: an adopter's command handler should name Goldpath's
/// <c>IIntegrationEventPublisher</c>, so the day the transport changes their file does not.
/// Goldpath's OWN packages are exempt — the seam has to delegate to something, and the
/// implementation is where that happens.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishEndpointInjectedAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.PublishEndpointInjected);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var endpoint = start.Compilation.GetTypeByMetadataName("MassTransit.IPublishEndpoint");
            if (endpoint is null)
            {
                return;   // no transport in this compilation — nothing to guard
            }

            start.RegisterSymbolAction(ctx =>
            {
                var parameter = (IParameterSymbol)ctx.Symbol;
                if (!SymbolEqualityComparer.Default.Equals(parameter.Type, endpoint))
                {
                    return;
                }

                // The seam's own implementation must inject it; so may anything inside the
                // Goldpath packages, which own the delegation.
                // SEGMENT match, not prefix: "GoldpathTemplate.Application" starts with
                // "Goldpath" and is ADOPTER code — a prefix test would have exempted the
                // very handlers this rule exists to guard (review #159 R3).
                var containing = parameter.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";
                if (containing == "Goldpath" || containing.StartsWith("Goldpath.", StringComparison.Ordinal))
                {
                    return;
                }

                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.PublishEndpointInjected,
                    parameter.Locations[0],
                    parameter.ContainingType?.Name ?? parameter.Name));
            }, SymbolKind.Parameter);
        });
    }
}
