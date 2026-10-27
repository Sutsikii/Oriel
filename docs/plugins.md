# Plugins

A plugin is a NuGet package of services: C# classes marked `[Service]`,
which apps bind, and pages call through a TypeScript client generated
with the app's own. Most apps need the same few: a database, secrets, files.

## Official plugins

| Package | Service | What it does |
|---|---|---|
| `Oriel.Sqlite` | `Sqlite` | A SQLite database: `query` and `execute` with named parameters, rows as objects |
| `Oriel.SecureStorage` | `SecureStorage` | The app's secrets, encrypted for the user with DPAPI (Windows) |

```sh
dotnet add package Oriel.Sqlite --prerelease
```

```csharp
var app = new OrielApp(new AppOptions { Name = "Notes" });
app.Bind(new SqliteDatabase(Path.Combine(app.DataDirectory, "notes.db")));
app.Bind(new SecureStorage(app));
```

```ts
import { SecureStorage, Sqlite } from "./oriel";

await Sqlite.execute("create table if not exists notes (id integer primary key, title text)");
await Sqlite.execute("insert into notes (title) values (@title)", { title: "Groceries" });
const notes = await Sqlite.query("select id, title from notes"); // [{ id: 1, title: "Groceries" }]

await SecureStorage.set("github-token", token);
```

**Sqlite.** Parameters are an object of names and values: `@title` in the
SQL takes `title`; names that start with `@`, `$` or `:` are taken as they
are. Numbers, strings, booleans (1 and 0) and null go in; integers, reals,
text, null and blobs (as base64) come out. Calls run one at a time, in
order. C# code has the connection itself, `SqliteDatabase.Connection`, for
transactions or Dapper. Pages run the SQL they send: only the app's own
pages can call services, but keep data pages should not change in a
database of its own.

**SecureStorage.** `get`, `set`, `delete` and `keys`. The secrets are one
file in the app's data folder (`OrielApp.DataDirectory`), encrypted with
DPAPI for the user who runs the app: another user of the machine, or the
file copied to another machine, cannot read it. Every page of the app can,
so keep the secrets pages should not see out of it. Windows only for now;
elsewhere the methods throw `PlatformNotSupportedException`.

## Writing a plugin

A plugin is a class library that references `Oriel`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Oriel" Version="..." />
  </ItemGroup>
</Project>
```

- The `Oriel` package brings the source generator: the plugin's build
  writes its services' dispatchers and their TypeScript client, as an
  app's does.
- The app binds the services it wants, with what they need: a path, an
  `OrielApp`, options. Constructors are C#'s; pages only see the public
  methods and events. Mark the public methods pages must not call
  `[ServiceIgnore]`, such as `Dispose`.
- When the app builds, its client includes the plugin's: with services in
  several assemblies, `oriel.ts` re-exports a module of each (see
  [bindings.md](bindings.md#services-of-several-assemblies)).
- Name services after the plugin, with `[Service(Name = "...")]`: two
  assemblies exporting the same name fail the app's build.
- Keep to what NativeAOT takes (`IsAotCompatible`), so that apps that
  publish native executables can use the plugin.
