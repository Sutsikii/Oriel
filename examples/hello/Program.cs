using Hello;
using Oriel;

var app = new OrielApp(new AppOptions { Name = "Oriel Hello" });
app.Bind(new Greeter());
app.Bind(new Clock());
app.Bind(new Machine());

return app.Run(() => app.OpenWindow(new WindowOptions
{
    Width = 760,
    Height = 560,
    MinWidth = 420,
    MinHeight = 360,
}));
