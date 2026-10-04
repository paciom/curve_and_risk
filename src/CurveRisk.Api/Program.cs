using CurveRisk.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCurveRiskApi(builder.Configuration);

var app = builder.Build();
app.MapCurveRiskApi();
await app.EnsureDatabaseAsync();
await app.RunAsync();
