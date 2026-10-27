# Building and shipping

The Oriel package adds build steps to the projects that reference it. They
are set with properties of the app's project.

## Properties

| Property | Default | What it does |
|---|---|---|
| `OrielTypeScriptOutput` | empty | After each build, the TypeScript client is written to this path, relative to the project. Empty: not written. |
| `OrielFrontendDist` | empty | The folder of the built frontend, copied to `wwwroot` beside the executable on build and publish. Files an earlier publish left in the published `wwwroot` are deleted. |
| `OrielEmbedFrontend` | `false` | Embeds the frontend in the assembly instead of copying it to `wwwroot`: see [A single file](#a-single-file). |
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

## Driving the app in tests

Pages run in WebView2, which speaks the Chrome DevTools Protocol as Chrome
does. Start the app with a debugging port, and a test tool drives its pages:
clicks, typing, screenshots, and calls to services, which run for real.

```sh
WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222 dotnet run
```

With [Playwright](https://playwright.dev) (`npm install playwright-core`, no
browser to download):

```js
import { chromium } from "playwright-core";

const browser = await chromium.connectOverCDP("http://localhost:9222");
const page = browser.contexts()[0].pages()[0];
await page.click("#start");
console.log(await page.evaluate(() => document.title));
await browser.close(); // disconnects; the app keeps running
```

- The port lets any program of the machine control the pages, and so call
  the services: set it in tests only, never in what ships.
- Each window is a page of the browser. They share the app's webview data,
  in `%LOCALAPPDATA%\Name Dev` while developing: delete it for a test that
  starts without `localStorage` or cookies.

## Publishing

```sh
dotnet publish -c Release -r win-x64
```

With `PublishAot`, this gives a native executable of about 2.5 MB, plus
`wwwroot`. The published `wwwroot` holds the frontend of this publish only:
the SDK removes what its earlier publishes with the same options wrote, and
Oriel removes the rest, such as the bundles of a publish with other options. NativeAOT needs Visual Studio or its Build Tools with the
"Desktop development with C++" workload. Without `PublishAot`, the app runs
on the .NET runtime, installed or bundled (`--self-contained`).

Oriel itself has no native dependency: it finds the WebView2 Runtime as the
official loader does, so there is no `WebView2Loader.dll` to ship. Users need
the runtime, which Windows 11 has; installers can bootstrap it on Windows 10.

### A single file

With `OrielEmbedFrontend`, the frontend is embedded in the executable
instead of copied to `wwwroot`. The app has no folder to lose or edit, and
ships as one file:

```xml
<OrielEmbedFrontend>true</OrielEmbedFrontend>
```

Pages are served from the executable at the same `https://app.oriel/`, with
the content type of each file (html, js, css, svg, wasm, fonts, images…).
While developing, the dev server serves them as before. An app can also
serve a frontend embedded in another assembly with
`AppOptions.AssetsAssembly`; an `AssetsDirectory` it sets wins over both.

## Installers

Set `OrielInstaller` and publish: after the app, Oriel builds its installer
with [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```xml
<PropertyGroup>
  <Product>My App</Product>
  <Version>1.2.0</Version>
  <Company>Me</Company>
  <ApplicationIcon>app.ico</ApplicationIcon>
  <OrielInstaller>true</OrielInstaller>
</PropertyGroup>
```

```sh
winget install JRSoftware.InnoSetup    # once
dotnet publish -c Release -r win-x64
```

This gives `bin/installer/My-App-Setup-1.2.0.exe`, a wizard that:

- installs for the current user, without administrator rights, in
  `%LOCALAPPDATA%\Programs\My App`;
- adds a Start menu shortcut, a desktop one if the user asks, and the app to
  Windows' list of installed apps, with an uninstaller;
- offers to start the app at the end;
- checks for the WebView2 Runtime first, and offers its download when it is
  missing (on some Windows 10 machines);
- on uninstall, removes the webview's data but keeps the app's own data
  (`OrielApp.DataDirectory`), which is the user's.

The installer holds what `dotnet publish` produced. Publish with
`PublishAot` (or `--self-contained`): an app that depends on an installed
.NET runtime needs one on the user's machine, and the installer does not
install it.

Installing a new version over an old one replaces it: the two share an
**AppId**. Oriel derives it from the app's name, so set `OrielAppId` to a
GUID of your own once the app ships, and keep it: renaming the app would
otherwise make the next version a different app.

An app made by copying an example keeps the example's `OrielAppId`, and
its installs would replace the example's, or those of any other copy:
publishing it warns, and gives a new GUID to use instead.

| Property | Default | What it does |
|---|---|---|
| `OrielInstaller` | `false` | Builds the installer after `dotnet publish`. |
| `OrielAppName` | `$(Product)`, else the assembly's name | The name users see. Make it the `AppOptions.Name` of the app, whose webview data the uninstaller removes. |
| `OrielAppVersion` | `$(Version)` | The installed version. |
| `OrielPublisher` | `$(Company)` | Shown in the list of installed apps. |
| `OrielAppId` | derived from the name | Identifies the app across versions. |
| `OrielInstallerIcon` | `$(ApplicationIcon)` | The icon of the installer. |
| `OrielInstallerLanguages` | `english` | The wizard's languages, by Inno Setup's message files: `english;french;german`… The wizard picks the user's. |
| `OrielInstallerOutput` | `bin/installer` | Where the installer is written. |
| `OrielInnoSetupCompiler` | found | The path of `ISCC.exe`, when Inno Setup is not installed where it usually is. |

The script Oriel generates is in `obj/` (`oriel-installer.iss`), for a look
at what the installer does.

Installers that are not signed make Windows SmartScreen warn the people who
download them. Signing needs a code signing certificate; Oriel does not sign
yet (see the [roadmap](roadmap.md)).

## Environment variables

| Variable | Effect |
|---|---|
| `ORIEL_DEV_URL` | Pages come from this dev server, which is then the only origin allowed to call services. |
| `ORIEL_GENERATE` | `OrielApp.Run` writes the TypeScript client to this path and returns. |
| `ORIEL_TRACE=1` | Traces windows, webviews, navigations and messages to the standard error stream. |
| `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` | Arguments for the webview's browser, such as `--remote-debugging-port=9222` to [drive the app in tests](#driving-the-app-in-tests). |
| `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` | Uses the fixed version WebView2 Runtime in this folder, as for the official loader. |
