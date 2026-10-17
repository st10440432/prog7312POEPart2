//ST10440432
//Matteo Nusca
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class InsightEndpoints
{
    public static void MapInsightEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/insights");

        //GET /api/insights - suggested actions + automated insights
        group.MapGet("/", (InsightEngine engine) => Results.Ok(engine.Build()));

        //POST /api/insights/events - the dashboard reports what the operator searched for/selected
        group.MapPost("/events", (SearchEventRequest request, DeviceRegistry registry, InsightEngine engine) =>
        {
            var device = registry.ResolveBest(request.Term);
            if (device is null)
                return Results.Ok(new SearchEventResponse { Logged = false });

            engine.RecordSearch(device.Id);
            return Results.Ok(new SearchEventResponse { Logged = true, DeviceId = device.Id });
        });

        //POST /api/insights/feedback - accepting / dismiss suggestion lowers its weight
        group.MapPost("/feedback", (FeedbackRequest request, InsightEngine engine) =>
        {
            if (string.IsNullOrWhiteSpace(request.SuggestionId))
                return Results.BadRequest("SuggestionId is required.");

            engine.Feedback(request.SuggestionId, request.Accepted);
            return Results.NoContent();
        });
    }
}
