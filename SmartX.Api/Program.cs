//ST10440432
//Matteo Nusca
using SmartX.Api.Endpoints;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

//Add services to container.
builder.Services.AddOpenApi();
builder.Services.AddSingleton<TelemetryStore>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

DataSeeder.Seed(app.Services.GetRequiredService<TelemetryStore>());

//Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazorClient");

app.MapSensorEndpoints();
app.MapTelemetryEndpoints();
app.MapFileUploadEndpoints();

app.Run();