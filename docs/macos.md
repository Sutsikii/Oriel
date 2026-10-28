# macOS

Oriel apps run on macOS with AppKit and WKWebView, the webview of Safari,
which every Mac has. This backend is new: it has the core of Oriel, as on
Linux, and not yet the native extras of Windows.

> **Help wanted.** The maintainers have no Mac yet. The backend is tested
> only by CI, on GitHub's macOS runners. Nobody has used it on a real Mac
> by hand yet, so what a person sees and does is untested: the Dock,
> the menu bar, full screen, Retina screens, several monitors, keyboard
> layouts. If you have a Mac, running an app and
> [opening an issue](https://github.com/Sutsikii/Oriel/issues/new) with
> what you saw helps a lot, and pull requests are welcome; see
> [#31](https://github.com/Sutsikii/Oriel/issues/31).

## What you need

- A version of macOS that .NET 10 supports, on Apple silicon or Intel.
  WKWebView is part of the system: there is nothing to install.
- The .NET 10 SDK, to build.

```sh
dotnet run
```

Publishing for macOS (`dotnet publish -r osx-arm64`), app bundles
(`.app`), signing and notarization are not done yet: for now, apps run
from their build.

## Call `Run` on the main thread

AppKit works on the process's main thread only. So on macOS,
`OrielApp.Run` runs the app on the thread that calls it, not on a thread
of Oriel's, and that thread must be the main one: call `Run` from `Main`,
before any `await`. Otherwise it throws `InvalidOperationException`.

```csharp
public static int Main()
{
    var app = new OrielApp(new AppOptions { Name = "Notes" });
    return app.Run(() => app.OpenWindow());
}
```

With `Oriel.Hosting`, `await host.RunAsync()` works as the first `await` of
`Main`, which still runs on the main thread there; `host.Run()` works
anywhere in `Main`.

Nothing else changes: services still run on the thread pool, and
`OrielWindow`'s members can still be called from any thread.

## What works

Tested in real windows, in CI on GitHub's macOS 26 runners (Apple silicon):

- services, calls, typed errors, events and the generated client;
- the app's files, from a folder or embedded in the executable;
- where pages may go, `window.open`, and no permission for pages, as on
  Windows;
- reloading a page, which cancels the calls of the page before, and
  reloading it when its web content process dies;
- moving, resizing, maximizing and restoring, frameless windows, full
  screen, and placements remembered across runs;
- the editing roles (Undo, Delete, Select All…) run on the page;
- file dialogs (open, open several, save, folder: NSOpenPanel and
  NSSavePanel) and message boxes (NSAlert), modal to the app. While one
  is open, the app goes on: calls return and events reach pages.

There, but not tested yet: several windows, minimum sizes, minimizing,
always on top, a dev server while developing, the Edit menu's keys
(Cmd+C, Cmd+V, Cmd+Z) typed by a person, and the Web Inspector. Intel Macs
are not tested.

## What is not there yet

These throw `NotSupportedException` on macOS, or do nothing for menus:

- the app's own menus, context menus and accelerators (the menu bar has
  Oriel's Quit and Edit menus);
- printing, and pages to PDF;
- the paths of dropped files (the page still gets its `File` objects);
- opening the Web Inspector from code: with `DevTools`, open it from the
  page's context menu, with Inspect Element, or from Safari's Develop
  menu.

[#31](https://github.com/Sutsikii/Oriel/issues/31) follows them.

## How it is tested

CI runs the end-to-end tests that need only the above in real windows on
GitHub's macOS runners: 10 of the 14, for now. `dotnet test` keeps the
main thread for itself, so on a Mac the test project runs them from its
own `Main`, with the apps' loops on the main thread:

```sh
ORIEL_E2E=1 dotnet run --project tests/Oriel.EndToEnd.Tests
```

Arguments filter the tests by name.

## How it differs

- **The origin of the app's pages** is `oriel://app.oriel`, not
  `https://app.oriel`, as on Linux: WKWebView serves custom schemes only.
  Pages see it in `location.origin`; Oriel, its navigation rules and
  `OrielWindow.Url` keep saying `https://app.oriel`.
- **The engine is WebKit**, as in Safari, not Chromium. Test pages there
  too.
- **Positions** are from the top left of the main screen, as on the other
  systems; Oriel turns them into AppKit's, which start at the bottom left.
- **Quitting from the menu**, or with Cmd+Q, ends `Run` as
  `OrielApp.Quit` does.
- **Updates** of the webview come with macOS.
