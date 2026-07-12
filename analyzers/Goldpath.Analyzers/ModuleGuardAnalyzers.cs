using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Goldpath.Analyzers;

/// <summary>
/// GP0501/GP0601/GP0901: entities carrying a model-dependent marker (IAuditLogged,
/// ISoftDeletable, IMultiTenant)
/// in a compilation that contains a DbContext but never calls the corresponding model wiring
/// (AddGoldpathAuditLog / ApplyGoldpathSoftDelete / ApplyGoldpathMultiTenancy). Scoped to compilations that HAVE a DbContext —
/// entity-only assemblies are exempt (the wiring lives with the context).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModelWiringAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            Descriptors.AuditLogNotWired, Descriptors.SoftDeleteNotWired, Descriptors.MultiTenancyNotWired);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var auditMarker = start.Compilation.GetTypeByMetadataName("Goldpath.IAuditLogged");
            var softMarker = start.Compilation.GetTypeByMetadataName("Goldpath.ISoftDeletable");
            var tenantMarker = start.Compilation.GetTypeByMetadataName("Goldpath.IMultiTenant");
            var dbContext = start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            if (dbContext is null || (auditMarker is null && softMarker is null && tenantMarker is null))
            {
                return;
            }

            var auditTypes = new ConcurrentBag<INamedTypeSymbol>();
            var softTypes = new ConcurrentBag<INamedTypeSymbol>();
            var tenantTypes = new ConcurrentBag<INamedTypeSymbol>();
            var state = new WiringState();

            start.RegisterSymbolAction(ctx =>
            {
                var type = (INamedTypeSymbol)ctx.Symbol;
                if (type.TypeKind != TypeKind.Class)
                {
                    return;
                }

                if (IsDerivedFrom(type, dbContext))
                {
                    state.HasDbContext = true;
                }

                if (auditMarker is not null && type.AllInterfaces.Contains(auditMarker, SymbolEqualityComparer.Default))
                {
                    auditTypes.Add(type);
                }

                if (softMarker is not null && type.AllInterfaces.Contains(softMarker, SymbolEqualityComparer.Default))
                {
                    softTypes.Add(type);
                }

                if (tenantMarker is not null && type.AllInterfaces.Contains(tenantMarker, SymbolEqualityComparer.Default))
                {
                    tenantTypes.Add(type);
                }
            }, SymbolKind.NamedType);

            start.RegisterOperationAction(ctx =>
            {
                var name = ((IInvocationOperation)ctx.Operation).TargetMethod.Name;
                if (name == "AddGoldpathAuditLog")
                {
                    state.AuditWired = true;
                }
                else if (name == "ApplyGoldpathSoftDelete")
                {
                    state.SoftDeleteWired = true;
                }
                else if (name == "ApplyGoldpathMultiTenancy")
                {
                    state.MultiTenancyWired = true;
                }
            }, OperationKind.Invocation);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (!state.HasDbContext)
                {
                    return;
                }

                if (!state.AuditWired)
                {
                    foreach (var type in auditTypes)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.AuditLogNotWired, type.Locations[0], type.Name));
                    }
                }

                if (!state.SoftDeleteWired)
                {
                    foreach (var type in softTypes)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.SoftDeleteNotWired, type.Locations[0], type.Name));
                    }
                }

                if (!state.MultiTenancyWired)
                {
                    foreach (var type in tenantTypes)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.MultiTenancyNotWired, type.Locations[0], type.Name));
                    }
                }
            });
        });
    }

    private static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class WiringState
    {
        public volatile bool HasDbContext;
        public volatile bool AuditWired;
        public volatile bool SoftDeleteWired;
        public volatile bool MultiTenancyWired;
    }
}

/// <summary>
/// GP0502: application code assigning audit stamp fields (CreatedAt/CreatedBy/ModifiedAt/
/// ModifiedBy) on an IAuditedEntity — those are infrastructure-owned (the AuditTrail
/// contributor fills them). Save contributors themselves are exempt.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ManualStampWriteAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] s_stampFields =
        ["CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.ManualStampWrite);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var audited = start.Compilation.GetTypeByMetadataName("Goldpath.IAuditedEntity");
            var contributor = start.Compilation.GetTypeByMetadataName("Goldpath.IEntitySaveContributor");
            if (audited is null)
            {
                return;
            }

            start.RegisterOperationAction(ctx =>
            {
                var assignment = (ISimpleAssignmentOperation)ctx.Operation;
                if (assignment.Target is not IPropertyReferenceOperation { Property: var property }
                    || !s_stampFields.Contains(property.Name)
                    || property.ContainingType?.AllInterfaces.Contains(audited, SymbolEqualityComparer.Default) is not true
                       && !SymbolEqualityComparer.Default.Equals(property.ContainingType, audited))
                {
                    return;
                }

                // Infrastructure (save contributors) legitimately writes stamps.
                var enclosing = ctx.ContainingSymbol.ContainingType;
                if (contributor is not null
                    && enclosing?.AllInterfaces.Contains(contributor, SymbolEqualityComparer.Default) is true)
                {
                    return;
                }

                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ManualStampWrite, assignment.Syntax.GetLocation(), property.Name));
            }, OperationKind.SimpleAssignment);
        });
    }
}

/// <summary>GP1003: [Idempotent] on a Mediant query is a no-op (queries are idempotent by contract).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IdempotentOnQueryAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.IdempotentOnQuery);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(static ctx =>
        {
            var type = (INamedTypeSymbol)ctx.Symbol;
            var isQuery = type.AllInterfaces.Any(static i =>
                i.Name == "IQuery" && i.ContainingNamespace?.ToDisplayString().StartsWith("Mediant", StringComparison.Ordinal) is true);

            if (isQuery && type.GetAttributes().Any(static a => a.AttributeClass?.Name == "IdempotentAttribute"))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.IdempotentOnQuery, type.Locations[0], type.Name));
            }
        }, SymbolKind.NamedType);
    }
}
