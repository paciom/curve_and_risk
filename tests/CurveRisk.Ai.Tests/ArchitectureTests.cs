using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
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
        AssertNoDependency(Tools, "CurveRisk.Copilot", "CurveRisk.Evals", "CurveRisk.Mcp", "Anthropic", "ModelContextProtocol");
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
        foreach (var assembly in new[] { Tools, Copilot, Evals })
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
