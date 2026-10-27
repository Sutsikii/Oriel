# Oriel.Sqlite

An [Oriel](https://github.com/Sutsikii/Oriel) plugin: a SQLite database of
the app, queried from C# and from pages.

```csharp
app.Bind(new SqliteDatabase(Path.Combine(app.DataDirectory, "notes.db")));
```

```ts
import { Sqlite } from "./oriel";

await Sqlite.execute("create table if not exists notes (id integer primary key, title text)");
await Sqlite.execute("insert into notes (title) values (@title)", { title: "Groceries" });
const notes = await Sqlite.query("select id, title from notes"); // [{ id: 1, title: "Groceries" }]
```

See [plugins.md](https://github.com/Sutsikii/Oriel/blob/main/docs/plugins.md).
