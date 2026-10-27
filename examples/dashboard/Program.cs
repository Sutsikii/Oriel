using Dashboard;
using Oriel;

var app = new OrielApp(new AppOptions { Name = "Oriel Dashboard" });
var metrics = new Metrics();
app.Bind(metrics);
return app.Run(() =>
{
    metrics.Start(); // once running: building the client runs the app until Run
    app.OpenWindow(new WindowOptions { Width = 1100, Height = 760 });
});
