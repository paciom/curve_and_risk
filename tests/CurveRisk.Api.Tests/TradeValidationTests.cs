using System.Net;
using System.Net.Http.Json;
using System.Text;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

/// <summary>What a trade may and may not contain: each rule, its limit, and the message a caller gets.</summary>
public sealed class TradeValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> PostAsync(CreateTradeRequest trade) => _client.PostAsJsonAsync("/api/v1/trades", trade, Ct);

    [Theory]
    [InlineData("""{"tradeId":" ","notionalAmount":1,"fixedRatePercent":3.5,"direction":"PayFixed","tenor":"5Y"}""", "tradeId", "A trade id of up to 64 characters is required.")]
    [InlineData("""{"tradeId":"V-DIR","notionalAmount":1,"fixedRatePercent":3.5,"direction":"Sideways","tenor":"5Y"}""", "direction", "Use PayFixed or ReceiveFixed.")]
    [InlineData("""{"tradeId":"V-ZERO","notionalAmount":0,"fixedRatePercent":3.5,"direction":"PayFixed","tenor":"5Y"}""", "notionalAmount", "The notional must be positive.")]
    [InlineData("""{"tradeId":"V-NEG","notionalAmount":-1,"fixedRatePercent":3.5,"direction":"PayFixed","tenor":"5Y"}""", "notionalAmount", "The notional must be positive.")]
    [InlineData("""{"tradeId":"V-HIGH","notionalAmount":1,"fixedRatePercent":100.01,"direction":"PayFixed","tenor":"5Y"}""", "fixedRatePercent", "The fixed rate must be between -100 and 100 percent.")]
    [InlineData("""{"tradeId":"V-LOW","notionalAmount":1,"fixedRatePercent":-100.01,"direction":"PayFixed","tenor":"5Y"}""", "fixedRatePercent", "The fixed rate must be between -100 and 100 percent.")]
    [InlineData("""{"tradeId":"V-TENOR","notionalAmount":1,"fixedRatePercent":3.5,"direction":"PayFixed","tenor":"five"}""", "tenor", "Use a positive count and a unit, such as 6M or 10Y.")]
    public async Task An_invalid_field_is_named_with_a_message_that_says_what_to_send(string trade, string field, string message)
    {
        var response = await _client.PostAsync("/api/v1/trades", new StringContent(trade, Encoding.UTF8, "application/json"), Ct);
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemReader.TypePrefix + "validation", problem.Text("type"));
        Assert.Equal("One or more fields are invalid.", problem.Text("title"));
        Assert.Equal(field, Assert.Single(problem.GetProperty("errors").EnumerateObject()).Name);
        Assert.Equal([message], problem.ErrorsFor(field));
    }

    [Fact]
    public async Task A_trade_id_may_be_64_characters_but_not_65()
    {
        var longest = new string('L', 64);
        var tooLong = new string('M', 65);

        var accepted = await PostAsync(ApiFactory.Trade(longest));
        var rejected = await PostAsync(ApiFactory.Trade(tooLong));

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Theory]
    [InlineData("V-RATE-TOP", 100)]
    [InlineData("V-RATE-BOTTOM", -100)]
    public async Task A_fixed_rate_of_exactly_plus_or_minus_100_percent_is_allowed(string tradeId, double ratePercent)
    {
        var response = await PostAsync(ApiFactory.Trade(tradeId) with { FixedRatePercent = ratePercent });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_receive_fixed_trade_keeps_its_direction()
    {
        var posted = await PostAsync(ApiFactory.Trade("V-RECEIVE") with { Direction = "ReceiveFixed" });
        var created = await posted.Content.ReadFromJsonAsync<TradeResponse>(Ct);
        var fetched = await _client.GetFromJsonAsync<TradeResponse>("/api/v1/trades/V-RECEIVE", Ct);

        Assert.Equal("ReceiveFixed", created!.Direction);
        Assert.Equal("ReceiveFixed", fetched!.Direction);
    }

    [Fact]
    public async Task A_trade_without_a_description_has_an_empty_one_when_booked_and_when_changed()
    {
        var posted = await PostAsync(ApiFactory.Trade("V-NODESC") with { Description = null });
        var created = await posted.Content.ReadFromJsonAsync<TradeResponse>(Ct);
        var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/trades/V-DESC")
        {
            Content = JsonContent.Create(new UpdateTradeRequest(1m, 3.5, "5Y", null)),
        };
        put.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        await PostAsync(ApiFactory.Trade("V-DESC"));

        var updated = await (await _client.SendAsync(put, Ct)).Content.ReadFromJsonAsync<TradeResponse>(Ct);

        Assert.Equal(string.Empty, created!.Description);
        Assert.Equal(string.Empty, updated!.Description);
    }

    [Fact]
    public async Task A_surrounding_space_in_a_trade_id_is_not_stored()
    {
        var response = await PostAsync(ApiFactory.Trade(" V-TRIM "));
        var created = await response.Content.ReadFromJsonAsync<TradeResponse>(Ct);

        Assert.Equal("V-TRIM", created!.TradeId);
    }

    [Fact]
    public async Task An_id_that_differs_only_by_surrounding_space_is_the_same_trade_and_so_a_conflict()
    {
        await PostAsync(ApiFactory.Trade("V-SPACED"));

        var response = await PostAsync(ApiFactory.Trade(" V-SPACED "));
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Trade 'V-SPACED' already exists.", problem.Text("detail"));
    }
}
