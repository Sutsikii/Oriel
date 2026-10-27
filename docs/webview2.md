# WebView2

Oriel apps show their pages in the Microsoft Edge WebView2 Runtime, the
webview Windows already has. This page says which runtime they need, how it
stays up to date, and what happens on a machine without it.

## Why the system webview

An app that bundles its browser engine, as Electron does, ships 100 MB or
more and updates the engine itself, or never does. WebView2 is Chromium,
installed once per machine and updated by Windows, so an Oriel app is a
2.5 MB executable and its pages get the engine's security fixes without a
new release of the app.

The usual worry with system webviews is that each user has a different
engine. On Windows this is mostly not the case: every WebView2 Runtime is
Chromium, the same engine as Edge and Chrome, and the Evergreen runtime
(below) follows Edge's releases, a few weeks apart. Pages can target the
current Chromium, as for Chrome. The differences between Windows, macOS and
Linux webviews only matter once Oriel runs on those systems.

## Which machines have it

- **Windows 11** has the runtime, and updates it.
- **Windows 10** has it on most machines, where Microsoft installed it
  through Windows Update. Some machines lack it, managed ones especially.
- **Windows 10 before 1809**, and older Windows, are not supported by
  Oriel.

Oriel finds the runtime as Microsoft's own loader does, from the registry
records of the Stable, Beta, Dev and Canary channels, in that order, per
machine and per user. A record left behind by an uninstalled runtime is
skipped.

## When it is missing

- **The installer** that `OrielInstaller` builds checks for the runtime
  first. Without it, it says so and offers to open the download page
  ([build.md](build.md#installers)).
- **The app** throws `WebViewUnavailableException` from `OrielApp.Run`,
  with a message that says what is missing and where to get it:

  ```
  The Microsoft Edge WebView2 Runtime is not installed; get it from https://go.microsoft.com/fwlink/p/?LinkId=2124703
  ```

  A runtime that is registered but whose files are gone gets its own
  message, asking to reinstall it. Catch the exception to tell users in
  your own words, with a native message box for instance.

## Updates: Evergreen

The installed runtime is **Evergreen**: Windows updates it in the
background, about every four weeks, as Edge. Apps do nothing for it, and
never ship an engine with known security holes.

An update never removes the features Oriel uses. Oriel needs a runtime that
serves an app's files from a folder (`ICoreWebView2_3`), which every
runtime of recent years has; an older one fails with
`WebViewUnavailableException` ("too old to serve app files"). A few
features ask for newer interfaces, and say so when the runtime lacks them:

| Feature | Needs | Without it |
|---|---|---|
| Showing pages, calls, events | `ICoreWebView2_3` | `WebViewUnavailableException` |
| Printing, and pages to PDF ([printing.md](printing.md)) | `ICoreWebView2_7`, `ICoreWebView2_16` | `NotSupportedException` from the call |
| `app-region: drag` in frameless windows ([windows.md](windows.md)) | `ICoreWebView2Settings9` | The drag regions do nothing |
| Keeping the browser's own shortcut off a key the page handles ([menus.md](menus.md)) | `ICoreWebView2AcceleratorKeyPressedEventArgs2` | The browser's shortcut runs too, such as its print dialog for Ctrl+P |

## A fixed version

Some places freeze software: machines without internet, or validated
setups. WebView2 has a **Fixed Version** runtime for them, a folder of a
given version that the app carries and that nothing updates.

Set `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` to that folder, as for Microsoft's
loader, and Oriel uses it instead of the installed runtime. A folder that
holds no runtime fails with `WebViewUnavailableException`, naming it.

A fixed version gets no security fixes: update it with each release of
the app. Microsoft's
[distribution guide](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution)
explains the trade-offs.

## Test against another runtime

- **An older or newer one**: download a Fixed Version runtime of that
  version from
  [Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/),
  and run the app with `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` set to it.
- **The next one**: Oriel takes the most stable channel installed, so a
  Beta, Dev or Canary runtime is used only on a machine without the stable
  one. To try a coming version, use its Fixed Version download as above.
- **None**: a Windows 10 virtual machine without the runtime, to see the
  installer's check and the app's message.
