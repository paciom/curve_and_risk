using CurveRisk.Ai.Tools;
using CurveRisk.Workflows;

namespace CurveRisk.Copilot.Briefs;

/// <summary>
/// The risk brief as a graph: which steps exist and what may follow each. Read this file to know every
/// path a brief can take. The model fills one node (narrate) and chooses none of the edges.
/// </summary>
public static class RiskBriefGraph
{
    public const string AwaitApproval = "await_approval";
    public const string Completed = "Completed";
    public const string WithoutCommentary = "WithoutCommentary";
    public const string EmptyPortfolio = "EmptyPortfolio";
    public const string EngineFailed = "EngineFailed";
    public const string ScenarioSaved = "ScenarioSaved";
    public const string SaveDeclined = "SaveDeclined";
    public const string SaveFailed = "SaveFailed";

    /// <summary>Steps on the longest path with no repair, and the steps one repair adds (repair, narrate, check).</summary>
    private const int StepsWithoutRepair = 12;
    private const int StepsPerRepair = 3;

    private const string Gather = "gather";
    private const string FindFlags = "find_flags";
    private const string Narrate = "narrate";
    private const string CheckGrounding = "check_grounding";
    private const string RequestRepair = "request_repair";
    private const string AcceptCommentary = "accept_commentary";
    private const string DropCommentary = "drop_commentary";
    private const string ProposeSave = "propose_save";
    private const string SaveScenario = "save_scenario";

    internal static GraphDefinition<RiskBriefState> Create(IModelClient? model, ToolExecutor executor, CopilotOptions options, BudgetGuard budget)
    {
        var tools = new BriefTools(executor);
        var builder = new GraphBuilder<RiskBriefState>("risk-brief").StartAt("load_portfolio");

        AddEngineSteps(builder, tools);
        AddCommentarySteps(builder, new NarrateNode(model, options, budget), MaxRepairs(options));
        AddSaveSteps(builder, tools);

        return builder
            .AddEnd(Completed).AddEnd(WithoutCommentary).AddEnd(EmptyPortfolio).AddEnd(EngineFailed)
            .AddEnd(ScenarioSaved).AddEnd(SaveDeclined).AddEnd(SaveFailed)
            .Build();
    }

    /// <summary>Repairs are model calls, so they are held under the same per-request call limit as the Copilot loop.</summary>
    internal static int MaxRepairs(CopilotOptions options) =>
        Math.Max(0, Math.Min(options.MaxGroundingRepairs, options.MaxModelCalls - 1));

    /// <summary>Enough steps for every repair the options allow, so the step limit is only ever hit by a defect.</summary>
    internal static int MaxSteps(CopilotOptions options) => StepsWithoutRepair + (StepsPerRepair * MaxRepairs(options));

    private static void AddEngineSteps(GraphBuilder<RiskBriefState> builder, BriefTools tools) => builder
        .AddNode("load_portfolio", new LoadPortfolioNode(tools), Edge.Route<RiskBriefState>(
            AfterLoad,
            new Branch(EngineFailed, "the engine failed"),
            new Branch(EmptyPortfolio, "no trades"),
            new Branch(Gather)))
        .AddParallel(Gather, new ParallelBranches<RiskBriefState>(EngineBranches(tools), MergeBranches), Edge.Route<RiskBriefState>(
            state => state.EngineError is null ? FindFlags : EngineFailed,
            new Branch(EngineFailed, "a tool failed"),
            new Branch(FindFlags)))
        .AddNode(FindFlags, GraphNode.FromRule<RiskBriefState>(BriefFactsBuilder.Build), Edge.Route<RiskBriefState>(
            state => state.EngineError is null ? Narrate : EngineFailed,
            new Branch(EngineFailed, "no totals"),
            new Branch(Narrate)));

    private static string AfterLoad(RiskBriefState state)
    {
        if (state.EngineError is not null)
        {
            return EngineFailed;
        }

        return state.Trades.Count == 0 ? EmptyPortfolio : Gather;
    }

    private static NamedNode<RiskBriefState>[] EngineBranches(BriefTools tools) =>
    [
        new("price_trades", new ToolFanOutNode<TradeValuation>(
            tools, ToolCatalog.PriceTrade, TradeArguments, (state, results) => state with { Valuations = results })),
        new("run_risk", new ToolFanOutNode<RiskReport>(
            tools, ToolCatalog.RunRisk, TradeArguments, (state, results) => state with { Risks = results })),
        new("run_scenarios", new ToolFanOutNode<ScenarioResult>(
            tools, ToolCatalog.RunScenario, ScenarioArguments, (state, results) => state with { Scenarios = results })),
    ];

    private static IEnumerable<object> TradeArguments(RiskBriefState state) =>
        state.Trades.Select(trade => new { tradeId = trade.TradeId });

    private static IEnumerable<object> ScenarioArguments(RiskBriefState state) =>
        state.Trades.SelectMany(trade => BriefShocks.All.Select(shock =>
            new { tradeId = trade.TradeId, parallelBp = shock.Shock.ParallelBp, steepenerBp = shock.Shock.SteepenerBp }));

    /// <summary>Each branch filled its own list and appended its own tool calls to the ones it started with.</summary>
    private static RiskBriefState MergeBranches(RiskBriefState start, IReadOnlyList<RiskBriefState> branches) => start with
    {
        Valuations = [.. branches.SelectMany(branch => branch.Valuations)],
        Risks = [.. branches.SelectMany(branch => branch.Risks)],
        Scenarios = [.. branches.SelectMany(branch => branch.Scenarios)],
        ToolCalls = [.. start.ToolCalls, .. branches.SelectMany(branch => branch.ToolCalls.Skip(start.ToolCalls.Count))],
        EngineError = branches.Select(branch => branch.EngineError).FirstOrDefault(error => error is not null),
    };

    private static void AddCommentarySteps(GraphBuilder<RiskBriefState> builder, NarrateNode narrate, int maxRepairs) => builder
        .AddNode(Narrate, narrate, Edge.Route<RiskBriefState>(
            state => state.Draft.Length == 0 ? DropCommentary : CheckGrounding,
            new Branch(CheckGrounding, "a draft"),
            new Branch(DropCommentary, "no draft")))
        .AddNode(CheckGrounding, GraphNode.FromRule<RiskBriefState>(CommentaryRules.Check), Edge.Route<RiskBriefState>(
            state => AfterCheck(state, maxRepairs),
            new Branch(AcceptCommentary, "grounded"),
            new Branch(RequestRepair, "ungrounded, repair left"),
            new Branch(DropCommentary, "ungrounded, none left")))
        .AddNode(RequestRepair, GraphNode.FromRule<RiskBriefState>(CommentaryRules.RequestRepair), Edge.To<RiskBriefState>(Narrate))
        .AddNode(AcceptCommentary, GraphNode.FromRule<RiskBriefState>(CommentaryRules.Accept), SaveOrEnd(Completed))
        .AddNode(DropCommentary, GraphNode.FromRule<RiskBriefState>(CommentaryRules.Drop), SaveOrEnd(WithoutCommentary));

    private static string AfterCheck(RiskBriefState state, int maxRepairs)
    {
        if (state.Grounding.IsGrounded)
        {
            return AcceptCommentary;
        }

        return state.Repairs < maxRepairs ? RequestRepair : DropCommentary;
    }

    /// <summary>A save is offered only when it was asked for and some scenario loses money: a flat book has no worst case to keep.</summary>
    private static Edge<RiskBriefState> SaveOrEnd(string end) => Edge.Route<RiskBriefState>(
        state => state.OfferSave && state.Facts!.Flags.WorstScenario.ProfitAndLoss < 0 ? ProposeSave : end,
        new Branch(ProposeSave, "save offered"),
        new Branch(end));

    private static void AddSaveSteps(GraphBuilder<RiskBriefState> builder, BriefTools tools) => builder
        .AddNode(ProposeSave, GraphNode.FromRule<RiskBriefState>(CommentaryRules.ProposeSave), Edge.To<RiskBriefState>(AwaitApproval))
        .AddPause(AwaitApproval, resumeAt: SaveScenario)
        .AddNode(SaveScenario, new SaveScenarioNode(tools), Edge.Route<RiskBriefState>(
            AfterSave,
            new Branch(ScenarioSaved, "saved"),
            new Branch(SaveDeclined, "declined"),
            new Branch(SaveFailed, "approved but failed")));

    private static string AfterSave(RiskBriefState state)
    {
        if (state.SavedScenario is not null)
        {
            return ScenarioSaved;
        }

        return state.SaveError is null ? SaveDeclined : SaveFailed;
    }
}
