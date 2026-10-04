using CurveRisk.Ai.Tools;
using CurveRisk.Analytics.Curves;
using CurveRisk.Copilot;
using CurveRisk.Engine;
using CurveRisk.Evals;
using NetArchTest.Rules;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// The dependency rules stated in CLAUDE.md, as tests. An invariant written in prose is a request; one
/// that fails the build when broken is a rule. These read the compiled assemblies, so they cannot be
/// satisfied by renaming something.
/// </summary>
public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Tools = typeof(IRiskEngine).Assembly;
    private static readonly System.Reflection.Assembly Copilot = typeof(CopilotAgent).Assembly;
    private static readonly System.Reflection.Assembly Evals = typeof(EvalRunner).Assembly;
    private static readonly System.Reflection.Assembly Analytics = typeof(DiscountCurve).Assembly;
    private static readonly System.Reflection.Assembly Engine = typeof(AnalyticsRiskEngine).Assembly;

    [Fact]
    public void Only_the_Anthropic_adapter_types_depend_on_the_Anthropic_sdk()
    {
        var offenders = Types.InAssembly(Copilot)
            .That().HaveDependencyOn("Anthropic")
            .GetTypes()
            .Select(type => type.FullName!)
            .Where(name => !name.StartsWith("CurveRisk.Copilot.Anthropic", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_tool_layer_depends_on_nothing_above_it()
    {
        AssertNoDependency(
            Tools, "CurveRisk.Copilot", "CurveRisk.Evals", "CurveRisk.Mcp", "CurveRisk.Engine", "CurveRisk.Analytics", "Anthropic", "ModelContextProtocol");
    }

    [Fact]
    public void The_pricing_library_is_pure_no_io_no_ai_no_other_project()
    {
        // It must load in any .NET host, so it may not reach for files, the network, the console,
        // async plumbing, or anything else in this solution.
        AssertNoDependency(
            Analytics, "CurveRisk.Ai", "CurveRisk.Copilot", "CurveRisk.Engine", "CurveRisk.Evals", "Anthropic", "Microsoft.Extensions",
            "System.IO", "System.Net", "System.Console", "System.Threading.Tasks", "System.Environment");
        Assert.Contains(Analytics.GetReferencedAssemblies(), reference => reference.Name == "netstandard");
    }

    [Fact]
    public void The_engine_adapts_the_library_to_the_tool_port_and_knows_nothing_of_agents()
    {
        AssertNoDependency(Engine, "CurveRisk.Copilot", "CurveRisk.Evals", "CurveRisk.Mcp", "Anthropic", "ModelContextProtocol");
    }

    [Fact]
    public void The_agent_never_reaches_past_the_port_to_the_pricing_code()
    {
        AssertNoDependency(Copilot, "CurveRisk.Analytics", "CurveRisk.Engine");
    }

    [Fact]
    public void The_agent_does_not_depend_on_its_hosts_or_its_evaluation()
    {
        AssertNoDependency(Copilot, "CurveRisk.Evals", "CurveRisk.Mcp", "ModelContextProtocol");
    }

    [Fact]
    public void Only_the_eval_entry_point_touches_the_provider_sdk()
    {
        var offenders = Types.InAssembly(Evals)
            .That().HaveDependencyOn("Anthropic")
            .GetTypes()
            .Select(type => type.FullName!)
            .Where(name => !name.StartsWith("Program", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Libraries_never_write_to_the_console()
    {
        // Output is injected (TextWriter, ILogger, telemetry). Only entry points own the console.
        AssertNoDependency(Copilot, "System.Console");
        AssertNoDependency(Tools, "System.Console");
    }

    [Fact]
    public void Interfaces_are_named_with_an_I_prefix_and_implementations_are_sealed()
    {
        foreach (var assembly in new[] { Tools, Copilot, Evals, Analytics, Engine })
        {
            Assert.True(Types.InAssembly(assembly).That().AreInterfaces().Should().HaveNameStartingWith("I").GetResult().IsSuccessful);

            var unsealed = Types.InAssembly(assembly)
                .That().AreClasses().And().AreNotAbstract().And().AreNotStatic().And().ArePublic()
                .Should().BeSealed()
                .GetResult();
            Assert.True(unsealed.IsSuccessful, string.Join(", ", unsealed.FailingTypeNames ?? []));
        }
    }

    private static void AssertNoDependency(System.Reflection.Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} must not depend on: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
