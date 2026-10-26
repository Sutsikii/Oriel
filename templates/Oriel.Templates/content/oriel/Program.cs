using Oriel;
using OrielTemplate;

var app = new OrielApp(new AppOptions { Name = "OrielTemplate" });
app.Bind(new Greeter());

return app.Run(() => app.OpenWindow(new WindowOptions { Width = 800, Height = 600 }));
