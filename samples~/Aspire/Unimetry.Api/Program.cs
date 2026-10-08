using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", static () => Results.Text("Unimetry Aspire sample API"));

app.MapGet("/match/load", static () =>
{
    var activity = Activity.Current;
    activity?.SetTag("unimetry.sample", "match.load");
    return Results.Ok(new
    {
        status = "ok",
        traceId = activity?.TraceId.ToHexString(),
        spanId = activity?.SpanId.ToHexString(),
    });
});

app.MapPost("/error", static () =>
{
    throw new InvalidOperationException("Unimetry Aspire sample error");
});

app.Run();
