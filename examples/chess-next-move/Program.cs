using ChessNextMove;
using Oriel;

var app = new OrielApp(new AppOptions { Name = "Chess Next Move" });
using var engine = new Engine(Engine.Locate());
app.Bind(engine);

// The engine's process is stopped when it is disposed, as the app quits.
return app.Run(() => app.OpenWindow(new WindowOptions
{
    Width = 1180,
    Height = 800,
    MinWidth = 760,
    MinHeight = 560,
}));
