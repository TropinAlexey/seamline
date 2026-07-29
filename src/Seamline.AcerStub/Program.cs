var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Stand-in for ACER's real trade-report intake — not a real regulator
// endpoint, exists only so Reporting.Worker's retry/idempotency logic
// (ADR-0015) has genuine flakiness to exercise rather than an assertion
// that it would work. Weighted random outcome on every call, independent
// of what's actually in the request body.
app.MapPost("/reports", async (HttpContext context) =>
{
    var roll = Random.Shared.NextDouble();

    if (roll < 0.2)
        return Results.StatusCode(StatusCodes.Status500InternalServerError);

    if (roll < 0.3)
    {
        // Long enough that any reasonable client-side timeout gives up
        // first — the response below is never actually seen by a client
        // with a sane timeout budget.
        await Task.Delay(TimeSpan.FromSeconds(35), context.RequestAborted);
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    if (roll < 0.4)
        return Results.Ok(new { status = "duplicate", ackId = Guid.NewGuid().ToString() });

    return Results.Ok(new { status = "accepted", ackId = Guid.NewGuid().ToString() });
});

app.Run();
