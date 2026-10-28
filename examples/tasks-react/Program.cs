using Oriel;
using TasksApp;

var app = new OrielApp(new AppOptions { Name = "Oriel Tasks" });
app.Bind(new Tasks());
return app.Run(() => app.OpenWindow(new WindowOptions { Width = 520, Height = 640 }));
