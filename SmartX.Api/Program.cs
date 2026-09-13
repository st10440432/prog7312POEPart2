//ST10440432
//Matteo Nusca
using SmartX.Api.Endpoints;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

//Add services to container
builder.Services.AddOpenApi();
builder.Services.AddSingleton<TelemetryStore>();

var app = builder.Build();

//Configure HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapSensorEndpoints();
app.MapTelemetryEndpoints();

app.Run();