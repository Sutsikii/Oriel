# Updates

Apps update themselves with [Velopack](https://velopack.io), through the
`Oriel.Updates` package: it checks for a newer version, downloads it (only
what changed, when releases have deltas), and restarts the app into it.
Pages show it all: the service `Updates` has a TypeScript client like the
app's own services.

```sh
dotnet add package Oriel.Updates --prerelease
```

```csharp
using Oriel;
using Oriel.Updates;

Updates.Run(); // first in Main: Velopack's setup and updates run their hooks here

var app = new OrielApp(new AppOptions { Name = "Notes" });
app.Bind(new Updates("https://example.com/notes/releases"));
return app.Run(() => app.OpenWindow());
```

```ts
import { CallError, Updates } from "./oriel";

const update = await Updates.check(); // { version: "1.4.0", notes: "…" } or null
if (update && confirm(`Install ${update.version}?`)) {
  Updates.onProgress((percent) => (bar.value = percent));
  await Updates.download();
  await Updates.restart(); // the app closes, updates, and starts again
}
```

| Method | Does |
|---|---|
| `status()` | `{ installed, version, pendingRestart }`: a build run from the IDE is not installed, and does not update |
| `check()` | The newer version, with its release notes in Markdown, or null |
| `download()` | Downloads it, raising `onProgress` with the percent (coalesced) |
| `restart()` | Closes the app, installs what was downloaded, and starts it again |

`check`, `download` and `restart` fail with a `CallError` of code
`not-installed` in an app that is not installed: during development, they
have nothing to update.

## Where updates come from

The `Updates` constructor takes:

- the URL of a folder of releases on a web server, such as a storage
  bucket or a CDN: `https://example.com/notes/releases`;
- a GitHub repository's URL, whose releases hold the files:
  `https://github.com/contoso/notes`;
- a local or network folder: `\\server\releases\notes`.

For a channel, a token or another source, give it Velopack's own
`UpdateManager` instead.

## Making releases

Velopack's `vpk` packs the published app into a release: the setup users
install it with, the full package, and a delta from the version before.

```sh
dotnet tool install -g vpk
dotnet publish -c Release -r win-x64 -p:Version=1.4.0 -o publish
vpk pack --packId Contoso.Notes --packVersion 1.4.0 --packDir publish --mainExe Notes.exe --outputDir releases
```

Upload what `releases` holds to the source, keeping the files of earlier
versions: apps find the newest in `releases.win.json`. Users install the
app with `Contoso.Notes-win-Setup.exe`, per user, without administrator
rights, and their copy updates from then on. Velopack's setup replaces
Oriel's Inno Setup installer for apps that update: leave `OrielInstaller`
off. `vpk pack` signs too, with `--signParams`; see
[Velopack's documentation](https://docs.velopack.io).

## Tested

`tests/updates/run.ps1` does what users go through: it publishes a small
app at 1.0.0 and 2.0.0 with NativeAOT, packs both, installs 1.0.0 with its
setup, runs it, and checks that it finds 2.0.0, downloads it and restarts
as 2.0.0; then it uninstalls it. CI runs it on Windows.
