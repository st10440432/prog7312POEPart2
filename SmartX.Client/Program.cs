//ST10440432
//Matteo Nusca
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SmartX.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

//Points at the SmartX.Api backend
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("https://localhost:7086/") });

await builder.Build().RunAsync();