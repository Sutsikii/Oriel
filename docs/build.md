# Building and shipping

The Oriel package adds build steps to the projects that reference it. They
are set with properties of the app's project.

## Properties

| Property | Default | What it does |
|---|---|---|
| `OrielTypeScriptOutput` | empty | After each build, the TypeScript client is written to this path, relative to the project. Empty: not written. |
| `OrielFrontendDist` | empty | The folder of the built frontend, copied to `wwwroot` beside the executable on build and publish. |
| `OrielFrontendDirectory` | `frontend` | The folder of the frontend's sources, where `OrielFrontendBuildCommand` runs. |
| `OrielFrontendBuildCommand` | empty | Run before the frontend is copied, in Release builds only: for instance `npm run build`. |

Projects usually also set:

- `<OutputType>WinExe</OutputType>`, so no console opens with the app;
- `<DefaultItemExcludes>$(DefaultItemExcludes);frontend/**</DefaultItemExcludes>`,
  so `node_modules` is not scanned for C# files;
- `<PublishAot>true</PublishAot>`, for a native executable.

## How the client is written

The generator compiles the client into the app, as a string registered when
the assembly loads. The `OrielWriteTypeScript` step runs the built app with
`ORIEL_GENERATE` set: `OrielApp.Run` sees it, writes the client and returns
without opening anything. The app's `Main` runs up to `Run`; keep work that
should not happen at build time after it, or in the `ready` callback.

## Development

While developing, the app shows the pages of a dev server instead of its
files: set `ORIEL_DEV_URL` to the server's URL. The example's `npm run dev`
does it for you, with `dotnet watch` to restart the app when C# changes.

Development and production keep their data apart: `OrielApp.DataDirectory`
is `%APPDATA%\Name Dev` in development, and the webview's own data goes to
`%LOCALAPPDATA%\Name Dev`. Developer tools and the page's context menu are on
in development, off otherwise; `WindowOptions.DevTools` overrides it.

## Publishing

```sh
dotnet publish -c Release -r win-x64
```

With `PublishAot`, this gives a native executable of about 2 MB, plus
`wwwroot`. NativeAOT needs Visual Studio or its Build Tools with the
"Desktop development with C++" workload. Without `PublishAot`, the app runs
on the .NET runtime, installed or bundled (`--self-contained`).

Oriel itself has no native dependency: it finds the WebView2 Runtime as the
official loader does, so there is no `WebView2Loader.dll` to ship. Users need
the runtime, which Windows 11 has; installers can bootstrap it on Windows 10.

## Environment variables

| Variable | Effect |
|---|---|
| `ORIEL_DEV_URL` | Pages come from this dev server, which is then the only origin allowed to call services. |
| `ORIEL_GENERATE` | `OrielApp.Run` writes the TypeScript client to this path and returns. |
| `ORIEL_TRACE=1` | Traces windows, webviews, navigations and messages to the standard error stream. |
| `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` | Uses the fixed version WebView2 Runtime in this folder, as for the official loader. |
