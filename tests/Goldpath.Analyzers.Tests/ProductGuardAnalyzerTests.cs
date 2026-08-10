using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

/// <summary>
/// GP2001 — source leakage (platform RFC §2b, R-2). The interesting axis here is the
/// ASSEMBLY, not the syntax, so every case pins an assembly name explicitly.
/// </summary>
public class ProductGuardAnalyzerTests
{
    [Fact]
    public Task GOLDPATH2001_flags_a_product_assembly_declaring_into_the_Goldpath_namespace()
        // The copy-a-file-keep-its-namespace move: compiles, looks harmless, and quietly
        // forks the core so security fixes stop arriving.
        => Verify("CorPay.Application", """
            namespace Goldpath
            {
                public sealed class {|#0:OutboxHelper|} { }
            }
            """,
            new DiagnosticResult(Descriptors.ProductDeclaresGoldpathNamespace)
                .WithLocation(0).WithArguments("OutboxHelper", "Goldpath", "CorPay.Application"));

    [Fact]
    public Task GOLDPATH2001_flags_a_nested_Goldpath_namespace_too()
        // Goldpath.Internal is the same act wearing a deeper name.
        => Verify("Portal.Api", """
            namespace Goldpath.Messaging.Internal
            {
                public sealed class {|#0:Filter|} { }
            }
            """,
            new DiagnosticResult(Descriptors.ProductDeclaresGoldpathNamespace)
                .WithLocation(0).WithArguments("Filter", "Goldpath.Messaging.Internal", "Portal.Api"));

    [Fact]
    public Task GOLDPATH2001_is_silent_inside_a_Goldpath_package()
        // The core declares its own namespace by definition; a rule that fires here is noise.
        => Verify("Goldpath.Messaging", """
            namespace Goldpath
            {
                public sealed class OutboxHelper { }
            }
            """);

    [Fact]
    public Task GOLDPATH2001_does_not_fire_on_an_assembly_whose_name_merely_STARTS_with_Goldpath()
        // GoldpathTemplate.Api is ADOPTER code. A prefix test would exempt it — the exact bug
        // GP0404 shipped with (review #159). Declaring into Goldpath from there is still a leak.
        => Verify("GoldpathTemplate.Api", """
            namespace Goldpath
            {
                public sealed class {|#0:Leak|} { }
            }
            """,
            new DiagnosticResult(Descriptors.ProductDeclaresGoldpathNamespace)
                .WithLocation(0).WithArguments("Leak", "Goldpath", "GoldpathTemplate.Api"));

    [Fact]
    public Task GOLDPATH2001_says_nothing_about_USING_Goldpath_types()
        // Consuming the packages is the entire point of shipping them, so implementing
        // Goldpath.IIntegrationEvent from a product namespace must stay silent.
        //
        // The stub below has to live somewhere, and in a single-assembly test that somewhere
        // is this compilation — which makes the STUB itself a leak by the rule's own
        // definition. That is not a flaw in the fixture, it is the rule being consistent:
        // the assertion therefore pins ONE diagnostic, on the declaration, and none on
        // PaymentExecuted. If the analyzer ever started punishing usage, this test goes red
        // with a second location.
        => Verify("CorPay.Api", """
            namespace Goldpath { public interface {|#0:IIntegrationEvent|} { } }
            namespace CorPay.Api
            {
                public sealed record PaymentExecuted : Goldpath.IIntegrationEvent;
            }
            """,
            new DiagnosticResult(Descriptors.ProductDeclaresGoldpathNamespace)
                .WithLocation(0).WithArguments("IIntegrationEvent", "Goldpath", "CorPay.Api"));

    private static Task Verify(string assemblyName, string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ProductDeclaresGoldpathNamespaceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        // The assembly name IS the subject under test, so it is set per case rather than left
        // to the harness default. Only the ASSEMBLY name is changed — renaming the PROJECT
        // too breaks the harness, which looks its primary project up by name and throws
        // "Sequence contains no matching element".
        test.SolutionTransforms.Add((solution, projectId) =>
            solution.WithProjectAssemblyName(projectId, assemblyName));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }
}
