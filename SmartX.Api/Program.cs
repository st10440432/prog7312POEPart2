//ST10440432
//Matteo Nusca
using SmartX.Api.Endpoints;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

//Add services to container.
builder.Services.AddOpenApi();
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("Gateway"));

//Core data-structure services (singletons so every request shares the same live state)
builder.Services.AddSingleton<DeviceRegistry>();       //Dictionary - live devices by MAC / id
builder.Services.AddSingleton<TelemetryHistory>();     //SortedDictionary - timeline per device
builder.Services.AddSingleton<AlertTracker>();         //HashSet - unique active alerts
builder.Services.AddSingleton<InsightEngine>();        //Predictive engine
builder.Services.AddSingleton<IngestionPipeline>();    //Queue + PriorityQueue
builder.Services.AddSingleton<CommandService>();       //Stack - undo history

builder.Services.AddHostedService<PipelineProcessor>();
builder.Services.AddHostedService<DisconnectWatchdog>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

DataSeeder.Seed(app.Services);

//Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowBlazorClient");

app.MapGet("/", () => Results.Ok(new { service = "Smart-X Gateway API", part = 2, status = "running" }));
app.MapSensorEndpoints();
app.MapTelemetryEndpoints();
app.MapFileUploadEndpoints();
app.MapStreamEndpoints();
app.MapCommandEndpoints();
app.MapInsightEndpoints();

app.Run();
