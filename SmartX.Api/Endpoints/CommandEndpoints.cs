//ST10440432
//Matteo Nusca
using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class CommandEndpoints
{
    public static void MapCommandEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/commands");

        //POST /api/commands - manual override 
        group.MapPost("/", (CommandRequest request, CommandService commands) => ToResult(commands.Execute(request)));

        //POST /api/commands/bulk - shuts down every valve
        group.MapPost("/bulk", (BulkCommandRequest request, CommandService commands) => ToResult(commands.ExecuteBulk(request)));

        //POST /api/commands/undo - pops the command stack and restores the previous state
        group.MapPost("/undo", (CommandService commands) => ToResult(commands.Undo()));

        //GET /api/commands/history
        group.MapGet("/history", (CommandService commands) => Results.Ok(commands.History()));

        //GET /api/commands/desired-state - what each actuator should be doing 
        group.MapGet("/desired-state", (CommandService commands) => Results.Ok(commands.DesiredStates()));
    }

    private static IResult ToResult(CommandResult result) => result.Outcome switch
    {
        CommandOutcome.Ok => Results.Ok(result.Record),
        CommandOutcome.NotFound => Results.NotFound(result.Message),
        CommandOutcome.NoChange => Results.Conflict(result.Message),
        _ => Results.BadRequest(result.Message)
    };
}
