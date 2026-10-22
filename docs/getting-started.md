# Getting started

This guide builds a small app: a C# service, a TypeScript page calling it,
and a native executable at the end.

## What you need

- Windows 10 1809 or later, with the
  [Microsoft Edge WebView2 Runtime](https://go.microsoft.com/fwlink/p/?LinkId=2124703)
  (Windows 11 has it).
- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- [Node.js](https://nodejs.org) 20 or later, for the frontend.
- For a native executable (NativeAOT): Visual Studio or its Build Tools, with
  the "Desktop development with C++" workload.

## 1. The project

Start from the example, which has everything wired: copy
[`examples/hello`](../examples/hello) and replace its `Directory.Build.*`
references by the package:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <DefaultItemExcludes>$(DefaultItemExcludes);frontend/**</DefaultItemExcludes>
    <OrielTypeScriptOutput>frontend/src/oriel.ts</OrielTypeScriptOutput>
    <OrielFrontendDist>frontend/dist</OrielFrontendDist>
    <OrielFrontendBuildCommand>npm run build</OrielFrontendBuildCommand>
    <PublishAot>true</PublishAot>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Oriel" Version="0.1.0" />
  </ItemGroup>
</Project>
```

One package brings the library, the source generator and the build steps.
[build.md](build.md) describes each `Oriel*` property.

> The package is not on nuget.org yet. Build it from the repository with
> `dotnet pack src/Oriel -c Release -o packages`, and add that folder as a
> package source (`dotnet nuget add source <path>/packages -n oriel-local`).

## 2. A service

A service is a class marked `[Service]`. Its public methods are what pages
can call:

```csharp
using Oriel;

/// <summary>Greets people.</summary>
[Service]
public class Greeter
{
    /// <summary>Returns a greeting for <paramref name="name"/>.</summary>
    public string Greet(string name) => $"Hello, {name}!";
}
```

Bind an instance, and open a window:

```csharp
using Oriel;

var app = new OrielApp(new AppOptions { Name = "My App" });
app.Bind(new Greeter());
return app.Run(() => app.OpenWindow(new WindowOptions { Width = 800, Height = 600 }));
```

## 3. The page

Build the project once (`dotnet build`): Oriel writes the TypeScript client to
`frontend/src/oriel.ts`. Import it like any module:

```ts
import { Greeter } from "./oriel";

document.body.textContent = await Greeter.greet("Ada");
```

`Greeter.greet` is typed from the C# method, its documentation comment
included. [bindings.md](bindings.md) covers what can be sent: records,
enums, lists, nullable types, events and more.

## 4. Develop

From `frontend/`:

```sh
npm install
npm run dev
```

This starts Vite and `dotnet watch` together. Editing the page reloads it;
editing C# rebuilds the app, restarts it and rewrites `oriel.ts`, so the
compiler of the page sees the change at once.

## 5. Ship

```sh
dotnet publish -c Release -r win-x64
```

In Release, the frontend is built (`npm run build`) and copied to `wwwroot`
beside a native executable of about 2 MB.

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
