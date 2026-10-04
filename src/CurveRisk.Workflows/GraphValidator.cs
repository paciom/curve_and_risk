using System.Text.RegularExpressions;

namespace CurveRisk.Workflows;

/// <param name="ParallelBranches">Null for an ordinary node.</param>
/// <param name="Labels">The condition text of each outgoing edge.</param>
internal sealed record StepOutline(string Name, IReadOnlyList<string> Targets, IReadOnlyList<string>? ParallelBranches, IReadOnlyList<string> Labels);

/// <summary>The wiring of a graph without its state type: all the validator needs.</summary>
internal sealed record GraphLayout(string Start, IReadOnlyList<StepOutline> Steps, IReadOnlyList<PausePoint> Pauses, IReadOnlyList<string> Ends);

/// <summary>
/// The rules a graph must satisfy before it may run. Each rule exists because breaking it produces a
/// run that can only fail later and less clearly: a missing target, a node nothing leads to, a path
/// that can never finish.
/// </summary>
internal static partial class GraphValidator
{
    private static readonly char[] LabelSyntax = ['|', '"', '\n', '\r'];

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex NamePattern();

    public static IReadOnlyList<string> Validate(GraphLayout layout)
    {
        var steps = layout.Steps.Select(step => step.Name).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        problems.AddRange(UnusableNames(layout));
        problems.AddRange(DuplicateNames(layout));
        problems.AddRange(EmptySteps(layout));
        problems.AddRange(UnknownTargets(layout, steps));
        if (!steps.Contains(layout.Start))
        {
            problems.Add(layout.Start.Length == 0 ? "No start node was set." : $"The start '{layout.Start}' is not a node.");
        }

        // Reachability is only meaningful once every edge points at something that exists.
        return problems.Count > 0 ? problems : [.. Unreachable(layout), .. DeadEnds(layout)];
    }

    /// <summary>
    /// Names and labels go into diagrams, traces and span names as they are, so they are held to a
    /// form that needs no escaping anywhere: an identifier, and a label without diagram syntax.
    /// </summary>
    private static IEnumerable<string> UnusableNames(GraphLayout layout)
    {
        var names = DeclaredNames(layout)
            .Where(name => !NamePattern().IsMatch(name) || string.Equals(name, "end", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Select(name => $"The name '{name}' is not usable: use letters, digits and underscores, starting with a letter, and not 'end'.");

        return names.Concat(layout.Steps
            .SelectMany(step => step.Labels.Where(label => label.IndexOfAny(LabelSyntax) >= 0).Select(label => (step.Name, Label: label)))
            .Select(edge => $"'{edge.Name}' has an edge labelled '{edge.Label}': a label may not contain '|', a quote or a line break."));
    }

    private static IEnumerable<string> DeclaredNames(GraphLayout layout) =>
        layout.Steps.Select(step => step.Name)
            .Concat(layout.Steps.SelectMany(step => step.ParallelBranches ?? []))
            .Concat(layout.Pauses.Select(pause => pause.Name))
            .Concat(layout.Ends);

    private static IEnumerable<string> DuplicateNames(GraphLayout layout) =>
        DeclaredNames(layout)
            .Append(GraphShape.StartName)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"The name '{group.Key}' is used more than once.");

    private static IEnumerable<string> EmptySteps(GraphLayout layout)
    {
        foreach (var step in layout.Steps)
        {
            if (step.Targets.Count == 0)
            {
                yield return $"'{step.Name}' has no outgoing edge.";
            }

            if (step.ParallelBranches is { Count: 0 })
            {
                yield return $"'{step.Name}' is parallel but has no branches.";
            }
        }
    }

    private static IEnumerable<string> UnknownTargets(GraphLayout layout, HashSet<string> steps)
    {
        var declared = Successors(layout).Keys.ToHashSet(StringComparer.Ordinal);
        var missing = layout.Steps
            .SelectMany(step => step.Targets.Where(target => !declared.Contains(target)).Select(target => (step.Name, Target: target)))
            .Select(edge => $"'{edge.Name}' has an edge to '{edge.Target}', which is not declared.");

        return missing.Concat(layout.Pauses
            .Where(pause => !steps.Contains(pause.ResumeAt))
            .Select(pause => $"The pause '{pause.Name}' resumes at '{pause.ResumeAt}', which is not a node."));
    }

    private static IEnumerable<string> Unreachable(GraphLayout layout)
    {
        var successors = Successors(layout);
        var reached = Closure([layout.Start], name => successors[name]);
        return successors.Keys.Where(name => !reached.Contains(name)).Select(name => $"'{name}' cannot be reached from the start.");
    }

    /// <summary>A node from which no end can be reached is a loop with no way out, or a path into one.</summary>
    private static IEnumerable<string> DeadEnds(GraphLayout layout)
    {
        var successors = Successors(layout);
        var predecessors = successors
            .SelectMany(pair => pair.Value.Select(target => (From: pair.Key, To: target)))
            .ToLookup(edge => edge.To, edge => edge.From, StringComparer.Ordinal);

        var finishing = Closure(layout.Ends, name => predecessors[name]);
        return successors.Keys.Where(name => !finishing.Contains(name)).Select(name => $"No end can be reached from '{name}'.");
    }

    /// <summary>Every name in the graph with the names that can follow it. Ends have none; a pause is followed by its resume node.</summary>
    private static Dictionary<string, IReadOnlyList<string>> Successors(GraphLayout layout)
    {
        var successors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var step in layout.Steps)
        {
            successors[step.Name] = step.Targets;
        }

        foreach (var pause in layout.Pauses)
        {
            successors[pause.Name] = [pause.ResumeAt];
        }

        foreach (var end in layout.Ends)
        {
            successors[end] = [];
        }

        return successors;
    }

    private static HashSet<string> Closure(IEnumerable<string> from, Func<string, IEnumerable<string>> next)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(from);
        while (pending.Count > 0)
        {
            var name = pending.Pop();
            if (seen.Add(name))
            {
                foreach (var following in next(name))
                {
                    pending.Push(following);
                }
            }
        }

        return seen;
    }
}
