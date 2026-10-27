# Getting started

This guide builds a small app: a C# service, a TypeScript page calling it,
and a native executable at the end.

## What you need

- Windows 10 1809 or later, with the
  [Microsoft Edge WebView2 Runtime](https://go.microsoft.com/fwlink/p/?LinkId=2124703)
  (Windows 11 has it; [webview2.md](webview2.md) says which machines lack
  it, and what apps do there).
- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- [Node.js](https://nodejs.org) 20 or later, for the frontend.

Later, for a native executable (NativeAOT) only: Visual Studio or its Build
Tools, with the "Desktop development with C++" workload.

The `oriel` command checks all of this, and says how to get what is
missing:

```sh
dotnet tool install -g Oriel.Cli --prerelease
oriel doctor
```

## 1. The project

Install the templates once, then create the app, with the frontend's
framework of your choice: `vanilla` (TypeScript and the DOM, the default),
`react`, `vue` or `svelte`:

```sh
dotnet new install Oriel.Templates
dotnet new oriel -o MyApp --framework react
cd MyApp
oriel dev
```

`oriel dev` installs the frontend's packages the first time, starts Vite,
then the app with `dotnet watch`, pointed at Vite. A window opens with a page
that calls C#: editing the page reloads it; editing C# rebuilds the app,
restarts it and rewrites the TypeScript client. Without the `oriel`
command, `npm install` then `npm run dev` in `frontend/` do the same.

The app has:

| File | What it is |
|---|---|
| `MyApp.csproj` | The project, with the `Oriel` package and its build properties ([build.md](build.md)), and an `OrielAppId` of its own |
| `Program.cs` | Binds the services and opens the window |
| `Services.cs` | A `[Service]` class, which the page calls |
| `frontend/` | A Vite and TypeScript frontend, with the framework chosen; `src/oriel.ts` is the generated client |

One package brings the library, the source generator and the build steps.
It is on [nuget.org](https://www.nuget.org/packages/Oriel), in preview;
[CHANGELOG.md](../CHANGELOG.md) says what each version changes. To add
Oriel to a project of your own instead, `dotnet add package Oriel
--prerelease`, then set the properties the template's project sets.

## 2. A service

A service is a class marked `[Service]`. Its public methods and events are
what pages can use; `Services.cs` has one:

```csharp
using Oriel;

/// <summary>Greets people.</summary>
[Service]
public class Greeter
{
    /// <summary>Greets <paramref name="name"/>.</summary>
    public Greeting Greet(string name) => new($"Hello, {name}!", DateTimeOffset.Now);

    /// <summary>Fires when someone was greeted.</summary>
    public Event<string> Greeted { get; } = new();
}
```

`Program.cs` binds an instance, and opens a window:

```csharp
var app = new OrielApp(new AppOptions { Name = "MyApp" });
app.Bind(new Greeter());
return app.Run(() => app.OpenWindow(new WindowOptions { Width = 800, Height = 600 }));
```

For services from dependency injection, `ILogger` and configuration, run
the app on the .NET Generic Host with the `Oriel.Hosting` package: see
[hosting.md](hosting.md).

## 3. The page

Each build writes the TypeScript client to `frontend/src/oriel.ts`. The page
imports it like any module:

```ts
import { Greeter } from "./oriel";

const greeting = await Greeter.greet("Ada"); // { text: string; at: string }
Greeter.onGreeted((name) => console.log(`greeted ${name}`));
```

`Greeter.greet` is typed from the C# method, its documentation comment
included. [bindings.md](bindings.md) covers what can be sent: records,
enums, lists, nullable types, events and more.

## 4. Develop

While `oriel dev` runs, add a method to `Greeter` and save: the app
restarts, `oriel.ts` gets the method, and the page's editor completes it.
Rename one, and the page's compiler shows each call that broke.

## 5. Ship

```sh
dotnet publish -c Release -r win-x64
```

In Release, the frontend is built (`npm run build`) and copied to `wwwroot`
beside a native executable of about 2.5 MB.

To ship it as a real Windows install, with a setup wizard, shortcuts and an
uninstaller, install [Inno Setup](https://jrsoftware.org/isinfo.php) once
(`winget install JRSoftware.InnoSetup`) and add to the project:

```xml
<Product>My App</Product>
<Version>1.0.0</Version>
<ApplicationIcon>app.ico</ApplicationIcon>
<OrielInstaller>true</OrielInstaller>
```

The same `dotnet publish` then writes `bin/installer/My-App-Setup-1.0.0.exe`.
[build.md](build.md#installers) has the details.

## When something goes wrong

- **No window, and an error about WebView2**: install the runtime from the
  link above.
- **A page calls a method and gets `window.oriel is missing`**: the page is
  open in a browser, not in an Oriel window.
- **The build reports ORIEL001 to ORIEL006**: a type, method or error cannot cross
  to pages; [diagnostics.md](diagnostics.md) explains each one.
- **Anything else**: run with `ORIEL_TRACE=1` to see what the app does, step
  by step.
