# Changelog

What changes in each release of the `Oriel` and `Oriel.Hosting` packages.
Versions follow [Semantic Versioning](https://semver.org); before 1.0, a
minor version may change the public API, and says so here.

## Unreleased

- **Linux: dialogs, printing and menus.** File dialogs and message boxes
  (GTK), the print dialog, pages to PDF files, the menu bar, context menus,
  accelerators and dropped files' paths now work on Linux.

- **Updates.** The `Oriel.Updates` package wires Velopack: the service
  `Updates` checks for a newer version, downloads it with progress, and
  restarts the app into it, from C# or from pages. docs/updates.md says how
  to make releases with `vpk`.

- **Signing.** `OrielSignCommand` signs the published executable, the
  installer and its uninstaller: signtool, Azure Trusted Signing, or any
  command. Installers of NativeAOT apps now take the executable after it is
  copied to the publish folder, which they could miss before.

- **Examples.** A back office (SQLite, 100 000 rows, validated forms), a 3D
  viewer (glTF through the binary channel, three.js) and a dashboard
  (coalesced events at 120 Hz, a log as a stream), published in CI.

- **Plugins.** `Oriel.Sqlite` (a SQLite database pages query) and
  `Oriel.SecureStorage` (secrets encrypted with DPAPI, Windows), with their
  TypeScript clients; docs/plugins.md says how to write one.

- **Templates and the `oriel` command.** `dotnet new oriel --framework
  react` (or `vue`, `svelte`, `vanilla`). The new `Oriel.Cli` tool gives
  `oriel doctor`, which checks what building and shipping apps needs, and
  `oriel dev`, which runs an app with its frontend's dev server.

- **Clients of several assemblies.** Services in referenced libraries get
  TypeScript clients too: a module per assembly, and an index at
  `OrielTypeScriptOutput` re-exporting them. Names two assemblies export
  fail the build. `CallError` is one class across clients.

- **One instance, links and files.** `AppOptions.SingleInstance` keeps one
  instance per user: later launches send it their arguments
  (`SecondInstanceStarted`). `OpenRequested` tells the links and files a
  launch asks to open, and the installer registers the app's schemes and
  file types (`OrielUrlSchemes`, `OrielFileTypes`).

- **Notifications.** `OrielApp.ShowNotification` shows a toast with up to
  five buttons, and `NotificationClicked` tells which was clicked (Windows).

- **Tray icons.** `OrielApp.AddTrayIcon` shows an icon in the notification
  area, with a tooltip and a menu (Windows). Windows gain `Hide`, `Show`,
  `IsVisible`, and `Closing`, raised when the person closes one, which
  can keep it open: close to the tray.

- **The clipboard.** `OrielApp.Clipboard` gets and sets text, HTML,
  images (PNG, screenshots included) and files, on Windows.

- **Binary results.** Methods returning `ReadOnlyMemory<T>` or `Memory<T>`
  of numbers give pages typed arrays (`Float32Array`, `Uint8Array`…),
  sent as bytes: through a buffer shared with the page on Windows, 25 to
  57 times faster than base64 JSON from 1 to 100 MB.

- **Files and large data by URL.** `OrielApp.Share` and `ShareFile` give
  a URL pages load a `Stream` from, in `<video>`, `<img>` or `fetch`,
  read as the page takes it, with range requests: videos seek.

- **Streams.** Methods returning `IAsyncEnumerable<T>` become
  `AsyncIterable<T>` in the TypeScript client, for `for await`. The page
  pulls items as it takes them, so a slow page slows the producer, and
  stopping the loop cancels the stream.

- **Coalesced events.** `new Event<T> { Coalesce = true }` sends pages only
  the latest payload of an event emitted faster than they take it, once
  per frame at most, so that a busy or hidden page does not fall behind.

## 0.1.0-preview.1

The first release, for Windows 10 1809 and later, with first versions of
Linux and macOS.

- **Services and the TypeScript client.** Classes marked `[Service]` are
  callable from pages through a client generated at compile time: methods,
  records, enums, nullable types, optional arguments, typed errors and
  events, with their documentation comments. Types that cannot cross to
  pages are reported at build time (ORIEL001 to ORIEL006).
- **Windows.** Win32 windows filled by WebView2, driven through COM with no
  native library of Oriel's own. Several windows, events sent to one of
  them, position, size, states, full screen, frameless windows and
  placements remembered across runs. Windows recover when a process of
  their webview fails.
- **Linux and macOS, first versions.** GTK 3 and WebKitGTK on Linux,
  AppKit and WKWebView on macOS, both without a native library of
  Oriel's own. They have the core (services, events, the app's files,
  navigation rules, window control) and not yet dialogs, menus, printing
  or dropped files' paths. The macOS backend is tested in CI only: help
  from Mac users is welcome
  ([docs/macos.md](https://github.com/Sutsikii/Oriel/blob/main/docs/macos.md)).
- **Native features, on Windows.** Dialogs modal to a window, the menu
  bar, context menus and accelerators, printing and saving pages as PDF.
- **Safety.** Only the app's own pages call services; calls of a page that
  navigated away are canceled; messages are limited in size, depth and
  number; pages get no permission by default.
- **Services' threads.** Calls run concurrently, one at a time, or on the
  UI thread.
- **Building and shipping.** One package brings the library, the generator
  and the build steps. NativeAOT publishing, a frontend copied or embedded
  in the executable, and installers built with Inno Setup on publish.
- **Oriel.Hosting.** Apps on the .NET Generic Host: services from
  dependency injection, per call or per window, `ILogger`, configuration and
  hosted services.
