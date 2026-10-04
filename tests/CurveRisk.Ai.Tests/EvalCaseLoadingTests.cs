using System.Text.Json;
using CurveRisk.Copilot;
using CurveRisk.Evals;

namespace CurveRisk.Ai.Tests;

/// <summary>Loading a dataset file: what is skipped, what is rejected, and where the error points.</summary>
public sealed class EvalCaseLoadingTests : IDisposable
{
    private const string CaseA = """{"id":"a","question":"first?"}""";
    private const string CaseB = """{"id":"b","question":"second?"}""";

    private readonly string _directory = Directory.CreateTempSubdirectory("evalcases").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Cases_with_unique_ids_load_in_file_order()
    {
        var path = Write(CaseA, CaseB);

        var cases = EvalDataset.Load(path);

        Assert.Equal(["a", "b"], cases.Select(c => c.Id));
        Assert.Equal(["first?", "second?"], cases.Select(c => c.Question));
    }

    [Fact]
    public void Blank_lines_and_comment_lines_are_skipped()
    {
        var path = Write("// the pricing cases", "", CaseA, "   ", "  // indented comment", CaseB);

        var cases = EvalDataset.Load(path);

        Assert.Equal(["a", "b"], cases.Select(c => c.Id));
    }

    [Fact]
    public void A_case_states_only_what_differs_from_the_defaults()
    {
        var path = Write(CaseA);

        var loaded = EvalDataset.Load(path).Single();

        Assert.Equal(AnswerStatus.Answered, loaded.ExpectStatus);
        Assert.Equal(ApprovalMode.Deny, loaded.Approval);
        Assert.Equal(0, loaded.ExpectSavedScenarios);
        Assert.Empty(loaded.Tags);
        Assert.Empty(loaded.ExpectTools);
        Assert.Empty(loaded.ForbidTools);
        Assert.Empty(loaded.ExpectValues);
    }

    [Fact]
    public void Enums_are_read_by_name()
    {
        var path = Write("""{"id":"a","question":"q","expectStatus":"Refused","approval":"Approve","expectSavedScenarios":1}""");

        var loaded = EvalDataset.Load(path).Single();

        Assert.Equal((AnswerStatus.Refused, ApprovalMode.Approve, 1), (loaded.ExpectStatus, loaded.Approval, loaded.ExpectSavedScenarios));
    }

    [Fact]
    public void A_malformed_line_is_reported_with_the_file_and_its_line_number()
    {
        var path = Write("// header", CaseA, "", "{not json");

        var error = Assert.Throws<InvalidDataException>(() => EvalDataset.Load(path));

        Assert.StartsWith($"{path}:4: ", error.Message, StringComparison.Ordinal);
        Assert.IsType<JsonException>(error.InnerException, exactMatch: false);
        Assert.EndsWith(error.InnerException.Message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_that_is_json_null_is_rejected()
    {
        var path = Write(CaseA, "null");

        var error = Assert.Throws<InvalidDataException>(() => EvalDataset.Load(path));

        Assert.Equal($"{path}:2: Line deserialised to null.", error.Message);
    }

    [Fact]
    public void A_case_without_an_id_is_rejected()
    {
        var path = Write("""{"question":"q"}""");

        var error = Assert.Throws<InvalidDataException>(() => EvalDataset.Load(path));

        Assert.StartsWith($"{path}:1: ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_case_id_is_rejected_and_named()
    {
        var path = Write(CaseA, CaseB, """{"id":"b","question":"again?"}""");

        var error = Assert.Throws<InvalidDataException>(() => EvalDataset.Load(path));

        Assert.Equal($"{path}: duplicate case id 'b'.", error.Message);
    }

    [Fact]
    public void Case_ids_that_differ_only_in_case_are_distinct()
    {
        var path = Write(CaseA, """{"id":"A","question":"upper?"}""");

        var cases = EvalDataset.Load(path);

        Assert.Equal(["a", "A"], cases.Select(c => c.Id));
    }

    private string Write(params string[] lines)
    {
        var path = Path.Combine(_directory, "cases.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }
}
