using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Goldpath.Analyzers;

/// <summary>
/// GP2001 — the antidote to platform RFC §2b risk R-2, <b>source leakage</b>.
///
/// <para>ADR-0012's whole bargain is that a product lives in its own repo and binds to
/// PUBLISHED packages. The way that bargain breaks is never a decision anyone announces; it
/// is a developer who needs one internal type, copies the file, keeps its namespace so the
/// usings still compile, and ships. From that moment the product owns a copy of the core
/// that can never receive an update, and nobody finds out until a security fix does not
/// arrive. Declaring into <c>Goldpath</c> is that act's first and most detectable symptom.</para>
///
/// <para>Scope is deliberately narrow: it fires only in assemblies that are NOT Goldpath
/// packages, so the core and its tests are unaffected, and it says nothing about USING our
/// types — which is the entire point of shipping them.</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProductDeclaresGoldpathNamespaceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The single rule this analyzer reports.</summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.ProductDeclaresGoldpathNamespace);

    /// <summary>Registers the per-assembly guard.</summary>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            // A SEGMENT match, never a prefix: "GoldpathTemplate.Api" starts with "Goldpath"
            // and is adopter code. GP0404 shipped that exact bug and it silently exempted the
            // template it was written to guard (review #159) — the same trap, refused twice.
            if (IsGoldpathOwned(start.Compilation.AssemblyName))
            {
                return;
            }

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
                if (!IsGoldpathOwned(ns))
                {
                    return;
                }

                foreach (var location in type.Locations)
                {
                    if (location.IsInSource)
                    {
                        symbolContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.ProductDeclaresGoldpathNamespace,
                            location,
                            type.Name,
                            ns,
                            symbolContext.Compilation.AssemblyName ?? "(unnamed)"));
                        break;   // one report per type, not one per partial declaration
                    }
                }
            }, SymbolKind.NamedType);
        });
    }

    /// <summary>True for the namespace/assembly <c>Goldpath</c> itself and its dotted children only.</summary>
    private static bool IsGoldpathOwned(string? name) =>
        name is not null && (name == "Goldpath" || name.StartsWith("Goldpath.", StringComparison.Ordinal));
}
