using Oriel;
using Viewer;

var app = new OrielApp(new AppOptions { Name = "Oriel 3D Viewer" });
app.Bind(new Models());
return app.Run(() => app.OpenWindow(new WindowOptions { Width = 1000, Height = 720 }));
