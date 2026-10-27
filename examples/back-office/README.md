# Back office

A back office of 100 000 customers: what most business apps are.

- **SQLite**, through the [`Oriel.Sqlite`](../../docs/plugins.md) plugin's
  connection, which only C# uses: pages call `Customers`, typed methods,
  never SQL of their own. The customers are made at the first call.
- **A grid of 100 000 rows** that draws the rows in view only, and fetches
  pages of 200 as it scrolls, sorted and searched in SQL.
- **Validated forms**: `Customers.save` throws `InvalidCustomerException`, a
  `[ServiceError("invalid")]` with what is wrong by field, which the form
  shows under each field (`err.code === "invalid"` types `err.data.errors`).
- **Events** to every window: `Saved` and `Deleted`.

```sh
cd frontend
npm install
npm run dev
```

`dotnet publish -c Release -r win-x64` gives a native executable, as CI does.
