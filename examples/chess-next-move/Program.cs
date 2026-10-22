using ChessNextMove;
using Microsoft.Extensions.DependencyInjection;
using Oriel;
using Oriel.Hosting;

var builder = OrielApp.CreateBuilder(args);
builder.Options = new AppOptions { Name = "Chess Next Move" };
// The engine's process is stopped when the container disposes it, as the app quits.
builder.Services.AddSingleton(_ => new Engine(Engine.Locate()));
builder.Services.AddOrielService<Engine>();

await using var app = builder.Build();
app.OpenWindow(new WindowOptions
{
    Width = 1180,
    Height = 800,
    MinWidth = 760,
    MinHeight = 560,
});
return await app.RunAsync();
