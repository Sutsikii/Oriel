# Changelog

What changes in each release of the `Oriel` and `Oriel.Hosting` packages.
Versions follow [Semantic Versioning](https://semver.org); before 1.0, a
minor version may change the public API, and says so here.

## Unreleased

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
