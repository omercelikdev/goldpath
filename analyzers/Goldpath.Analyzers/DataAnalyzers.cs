using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Goldpath.Analyzers;

/// <summary>GP0301: flags <c>DateTime</c> properties on entities implementing a Goldpath marker.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeOnEntityAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] s_markers =
        ["Goldpath.IAuditedEntity", "Goldpath.ISoftDeletable", "Goldpath.IMultiTenant"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.DateTimeOnEntity);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var markers = s_markers
                .Select(start.Compilation.GetTypeByMetadataName)
                .Where(static t => t is not null)
                .ToImmutableArray();
            var dateTime = start.Compilation.GetSpecialType(SpecialType.System_DateTime);
            if (markers.Length == 0)
            {
                return;   // Goldpath.Abstractions not referenced — rule is inert (L1 safe)
            }

            start.RegisterSymbolAction(ctx =>
            {
                var property = (IPropertySymbol)ctx.Symbol;
                var type = property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                    ? nullable.TypeArguments[0]
                    : property.Type;

                if (SymbolEqualityComparer.Default.Equals(type, dateTime)
                    && property.ContainingType.AllInterfaces.Any(i => markers.Contains(i, SymbolEqualityComparer.Default)))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.DateTimeOnEntity, property.Locations[0], property.Name));
                }
            }, SymbolKind.Property);
        });
    }
}

/// <summary>
/// GP0302: flags runtime <c>Migrate</c>/<c>EnsureCreated</c> calls without a Development
/// guard. Guard detection is a documented syntactic heuristic: an ancestor <c>if</c> whose
/// condition mentions <c>IsDevelopment</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RuntimeMigrateAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] s_methods =
        ["Migrate", "MigrateAsync", "EnsureCreated", "EnsureCreatedAsync"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.RuntimeMigrate);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(static ctx =>
        {
            var invocation = (IInvocationOperation)ctx.Operation;
            var method = invocation.TargetMethod;
            if (!s_methods.Contains(method.Name)
                || method.ContainingNamespace?.ToDisplayString().StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) is not true)
            {
                return;
            }

            for (var node = invocation.Syntax.Parent; node is not null; node = node.Parent)
            {
                if (node is IfStatementSyntax ifStatement
                    && ifStatement.Condition.ToString().Contains("IsDevelopment"))
                {
                    return;   // guarded — fine
                }
            }

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptors.RuntimeMigrate, invocation.Syntax.GetLocation(), method.Name));
        }, OperationKind.Invocation);
    }
}

/// <summary>GP0303: flags non-constant SQL strings passed to raw SQL APIs.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSqlInterpolationAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] s_methods =
        ["FromSqlRaw", "ExecuteSqlRaw", "ExecuteSqlRawAsync", "SqlQueryRaw"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.RawSqlInterpolation);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(static ctx =>
        {
            var invocation = (IInvocationOperation)ctx.Operation;
            if (!s_methods.Contains(invocation.TargetMethod.Name))
            {
                return;
            }

            var sqlArgument = invocation.Arguments
                .FirstOrDefault(static a => a.Value.Type?.SpecialType == SpecialType.System_String);
            if (sqlArgument is null)
            {
                return;
            }

            var value = sqlArgument.Value;
            var isConstant = value.ConstantValue.HasValue
                || value is IInterpolatedStringOperation { Parts.Length: > 0 } interpolated
                   && interpolated.Parts.All(static p => p is IInterpolatedStringTextOperation);

            if (!isConstant)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.RawSqlInterpolation, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));
            }
        }, OperationKind.Invocation);
    }
}
