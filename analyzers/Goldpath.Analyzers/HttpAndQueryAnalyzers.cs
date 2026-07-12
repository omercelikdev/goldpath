using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Goldpath.Analyzers;

/// <summary>GP0102: flags direct <c>new HttpClient()</c> instantiation.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NewHttpClientAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.NewHttpClient);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var httpClient = start.Compilation.GetTypeByMetadataName("System.Net.Http.HttpClient");
            if (httpClient is null)
            {
                return;
            }

            start.RegisterOperationAction(ctx =>
            {
                var creation = (IObjectCreationOperation)ctx.Operation;
                if (SymbolEqualityComparer.Default.Equals(creation.Type, httpClient))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.NewHttpClient, creation.Syntax.GetLocation()));
                }
            }, OperationKind.ObjectCreation);
        });
    }
}

/// <summary>GP0202: flags <c>Skip().Take()</c> offset pagination on LINQ queries.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OffsetPaginationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Descriptors.OffsetPagination);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(static ctx =>
        {
            var invocation = (IInvocationOperation)ctx.Operation;
            if (invocation.TargetMethod is not { Name: "Take", ContainingType.Name: "Queryable" })
            {
                return;
            }

            // Receiver of Take(...) — for extension methods the receiver is the first argument.
            var receiver = invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null;
            if (Unwrap(receiver) is IInvocationOperation { TargetMethod: { Name: "Skip", ContainingType.Name: "Queryable" } })
            {
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.OffsetPagination, invocation.Syntax.GetLocation()));
            }
        }, OperationKind.Invocation);
    }

    private static IOperation? Unwrap(IOperation? operation)
        => operation is IConversionOperation conversion ? conversion.Operand : operation;
}
