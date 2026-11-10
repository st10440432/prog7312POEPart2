//ST10440432
//Matteo Nusca
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class MeshEndpoints
{
    public static void MapMeshEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/mesh");

        //GET /api/mesh - nodes, links, minimum spanning tree links and up / down / isolated counts
        group.MapGet("/", (MeshTopologyService mesh) => Results.Ok(mesh.Snapshot()));

        //GET /api/mesh/connectivity - which nodes still reach the gateway, islands, down nodes
        group.MapGet("/connectivity", (MeshTopologyService mesh) => Results.Ok(mesh.Connectivity()));

        //GET /api/mesh/route/{deviceId} - fewest-hops, cheapest and spanning-tree routes to the gateway
        group.MapGet("/route/{deviceId}", (string deviceId, MeshTopologyService mesh) =>
        {
            var report = mesh.Route(deviceId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        //POST /api/mesh/nodes/{deviceId}/fail - simulate a node failure
        group.MapPost("/nodes/{deviceId}/fail", (string deviceId, MeshTopologyService mesh) =>
        {
            var result = mesh.SetFailed(deviceId, true);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        //POST /api/mesh/nodes/{deviceId}/restore - bring a failed node back
        group.MapPost("/nodes/{deviceId}/restore", (string deviceId, MeshTopologyService mesh) =>
        {
            var result = mesh.SetFailed(deviceId, false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }
}
