using CurveRisk.Api;
using CurveRisk.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCurveRiskApi(builder.Configuration);

var app = builder.Build();
app.MapCurveRiskApi();

// Local and demo runs create the schema on start. Versioned migrations for production are not written yet.
if (app.Configuration.GetValue("Database:CreateOnStart", defaultValue: app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CurveRiskDbContext>().Database.EnsureCreatedAsync();
}

await app.RunAsync();
