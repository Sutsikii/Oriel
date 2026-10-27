# Linux

Oriel apps run on Linux with GTK 3 and WebKitGTK, the webview of GNOME,
which the system already has or installs as a package. This backend is
new: it has the core of Oriel, and not yet the native extras of Windows.

## What you need

- GTK 3 and WebKitGTK 4.1, from the system's packages:

  | System | Packages |
  |---|---|
  | Ubuntu 24.04, Debian 13 | `libgtk-3-0t64 libwebkit2gtk-4.1-0` |
  | Ubuntu 22.04, Debian 12 | `libgtk-3-0 libwebkit2gtk-4.1-0` |
  | Fedora | `gtk3 webkit2gtk4.1` |
  | Arch | `gtk3 webkit2gtk-4.1` |

- A display: X11, or Wayland through GTK. Without one, `OrielApp.Run`
  throws `WebViewUnavailableException`, saying so.

Publish the app for Linux on a Linux machine, with the .NET 10 SDK:

```sh
dotnet publish -c Release -r linux-x64
```

Publishing for Linux is not tested yet, with NativeAOT or without; the
tests run the app from its build.

## What works

Tested in real windows, in CI:

- services, calls, typed errors, events and the generated client;
- the app's files, from a folder or embedded in the executable;
- where pages may go, `window.open`, and no permission for pages, as on
  Windows;
- reloading a page, which cancels the calls of the page before;
- file dialogs (open, open several, save, folder) and message boxes, modal
  to their window: GTK's, through the desktop's portal when the app runs in
  a sandbox (Flatpak, Snap);
- printing: GTK's print dialog, which `ShowPrintDialog` shows whichever is
  asked, and pages to PDF files through GTK's "Print to File" printer,
  laid out by `PdfOptions` (but `HeaderAndFooter`, which WebKitGTK has not).

There, but not tested yet: several windows, sizes, positions, minimum
sizes, states, full screen, always on top, frameless windows, placements
remembered across runs, a dev server while developing, and developer
tools. Moving and resizing were tried by hand; maximizing needs a window
manager, which the tests' virtual display has not.

## What is not there yet

These throw `NotSupportedException` on Linux, or do nothing for menus:

- the menu bar, context menus and accelerators;
- the paths of dropped files (the page still gets its `File` objects).

[#30](https://github.com/Sutsikii/Oriel/issues/30) follows them.

## How it is tested

CI runs the end-to-end tests that need only the above in real WebKitGTK
windows on Ubuntu 24.04, under Xvfb. To run them on another system,
with Docker:

```sh
docker build -t oriel-linux tests/linux
docker run --rm -v "$PWD:/repo:ro" oriel-linux
```

## How it differs

- **The origin of the app's pages** is `oriel://app.oriel`, not
  `https://app.oriel`: WebKitGTK serves custom schemes only. Pages see it
  in `location.origin`; Oriel, its navigation rules and `OrielWindow.Url`
  keep saying `https://app.oriel`. The scheme is secure, so pages are a
  secure context, as on Windows.
- **The engine is WebKit**, as in Safari, not Chromium. Test pages there
  too: most web features match, and some newer ones come later.
- **Updates** come with the system's packages, not from the app.
