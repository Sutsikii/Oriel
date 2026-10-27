# Roadmap

What Oriel has, and what is planned. Items are roughly in order; each is a
self-contained piece of work.

## Done

- Services: typed calls from pages, run on the thread pool, with
  cancellation when the page goes away and the calling window.
- Events from services to pages, to every window or to one; windows have
  ids, and names to find them by. Coalesced events for
  high-frequency updates, once per frame at most.
- The clipboard: text, HTML, images and files (Windows).
- Templates for vanilla TypeScript, React, Vue and Svelte frontends
  (`dotnet new oriel --framework`), and the `oriel` command: `oriel doctor`
  and `oriel dev`.
- Examples of what Oriel is for: a back office, a 3D viewer and a
  real-time dashboard, published in CI.
- Plugins: `Oriel.Sqlite` and `Oriel.SecureStorage` (Windows), and how to
  write one.
- Notifications with buttons (Windows).
- One instance per user, with the arguments of later launches; links of
  the app's schemes and its files, registered by the installer.
- Tray icons with a menu (Windows); windows that hide, and ask before
  the person closes them.
- Binary results: `ReadOnlyMemory<T>` as typed arrays, through buffers
  shared with the page on Windows.
- Files and large data by URL (`OrielApp.Share`), read as pages take
  them, with range requests for videos.
- Streams: `IAsyncEnumerable<T>` results as async iterables in pages, with
  back-pressure and cancellation when the page stops iterating.
- The source generator: dispatchers, JSON codecs and the TypeScript client,
  without reflection; diagnostics for what it cannot generate.
- Windows backend: Win32 windows with WebView2 through COM, no loader DLL;
  dark title bar, per-monitor DPI, app files served at `https://app.oriel/`;
  pages recover from failures of the webview's processes; windows stay on the
  app, open other sites in the browser, and grant no permission by default.
- Build integration: the client written on build, the frontend copied on
  publish, NativeAOT.
- Installers: `OrielInstaller` builds a per-user Inno Setup installer on
  publish, with shortcuts, an uninstaller and a WebView2 check.
- Native dialogs, modal to a window: open one or several files, save,
  pick a folder, message boxes.
- Printing: print dialogs, and pages to PDF files with their layout.
- Menus: the menu bar and context menus, with check items, roles and
  accelerators the page gets first; dark with Windows.
- Window control: position, size, states, full screen, always on top,
  focus; frameless windows with drag regions; placements remembered across
  runs, on the right monitor.
- `Oriel.Hosting`: apps on the .NET Generic Host, with services from
  dependency injection (for the app, per window, per call), `ILogger`,
  configuration and hosted services.

## Next

### Calls and types

- **Polymorphism**: `[JsonDerivedType]` hierarchies as TypeScript unions.
- **Generic records** of the app's own.

### Windows and desktop

- **Embedded files**: the frontend inside the executable, served through
  `WebResourceRequested`, for a single-file app.

### Tooling

- **Signing** of the app and its installer, from a certificate or a
  signing service.
- **MSIX** packages, for the Microsoft Store.
- **Auto-updates**, signed.

### Platforms

- **Linux**: GTK and WebKitGTK, through `LibraryImport` of the system's
  libraries. The core is there ([linux.md](linux.md)); dialogs, menus,
  printing and dropped files' paths are next.
- **macOS**: AppKit and WKWebView, through the Objective-C runtime. The
  core is there ([macos.md](macos.md)), tested in CI only; the same
  features are next, then app bundles, signing and notarization. Help from
  Mac users is wanted.

See [CONTRIBUTING.md](../CONTRIBUTING.md) to pick one up.
