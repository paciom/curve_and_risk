using CurveRisk.Api.Services;
using CurveRisk.Contracts.V1;
using NetArchTest.Rules;

namespace CurveRisk.Api.Tests;

/// <summary>The layering of the API, checked on the compiled assemblies.</summary>
public class ApiArchitectureTests
{
    private static readonly System.Reflection.Assembly Api = typeof(ProblemDetailsExceptionHandler).Assembly;
    private static readonly System.Reflection.Assembly Contracts = typeof(TradeResponse).Assembly;

    [Fact]
    public void Contracts_are_plain_data_that_any_dotnet_client_can_load()
    {
        var references = Contracts.GetReferencedAssemblies().Select(reference => reference.Name).ToList();

        Assert.Equal(["netstandard"], references);
    }

    [Fact]
    public void Endpoints_go_through_services_and_never_touch_the_database_or_the_pricing_library()
    {
        var result = Types.InAssembly(Api)
            .That().ResideInNamespace("CurveRisk.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny("CurveRisk.Api.Persistence", "Microsoft.EntityFrameworkCore", "CurveRisk.Analytics", "CurveRisk.Engine")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Persistence_knows_nothing_of_http_or_pricing()
    {
        var result = Types.InAssembly(Api)
            .That().ResideInNamespace("CurveRisk.Api.Persistence")
            .ShouldNot().HaveDependencyOnAny("Microsoft.AspNetCore", "CurveRisk.Analytics", "CurveRisk.Engine", "CurveRisk.Contracts")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_api_does_not_reference_the_model_provider_sdk()
    {
        var result = Types.InAssembly(Api).ShouldNot().HaveDependencyOn("Anthropic").GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
