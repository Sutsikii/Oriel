using BackOffice;
using Oriel;
using Oriel.Sqlite;

var app = new OrielApp(new AppOptions { Name = "Oriel Back Office" });

// The database is C#'s: pages reach it through Customers, never with SQL of their own.
var database = new SqliteDatabase(Path.Combine(app.DataDirectory, "customers.db"));
// The build runs the app to write its client: the 100 000 customers are made at the first call, not here.
app.Bind(new Customers(database.Connection, seed: 100_000));

var exit = app.Run(() => app.OpenWindow(new WindowOptions { Width = 1100, Height = 720, MinWidth = 640, MinHeight = 400 }));
database.Dispose();
return exit;
