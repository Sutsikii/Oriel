# Windows

What a window can do besides showing its page: move, resize, minimize,
maximize, go full screen, stay on top, open without a frame, remember where
it was, and take files dropped on it.

## Opening

```csharp
app.OpenWindow(new WindowOptions
{
    Width = 1000,
    Height = 700,
    Center = true,          // or X = 100, Y = 80
    State = WindowState.Maximized,
    StateKey = "main",      // reopens where it was last closed
});
```

Positions and sizes are in device-independent pixels, which the window
scales to its monitor: 1000 is 1000 pixels at 100%, 1500 at 150%. A
window's size includes its frame.

## From the app

`OrielWindow` has:

| Member | Does |
|---|---|
| `Bounds` | Where the window is, and its size, as it was last moved or resized |
| `Move(x, y)`, `Resize(width, height)` | Move and resize it, restoring it first when minimized or maximized |
| `Center()` | Puts it in the middle of its monitor |
| `State`, `Minimize()`, `Maximize()`, `Restore()` | `Normal`, `Minimized` or `Maximized` |
| `IsFullScreen` | Fills the monitor, without frame, menu bar or taskbar; set it back to false to return where it was |
| `IsAlwaysOnTop` | Stays over the other windows |
| `IsFocused`, `Focus()` | Whether it is the active window; makes it so |

The methods and setters can be called from any thread, and take effect on
the UI thread. The events are raised on the UI thread:

| Event | When |
|---|---|
| `Moved` | The window moved |
| `Resized` | It was resized |
| `StateChanged` | It was minimized, maximized or restored |
| `FocusChanged` | It became the active window, or stopped being it |

Minimizing, maximizing and full screen leave `Bounds` as they were: they
are where the window goes back to.

`Focus` cannot always take the keyboard from another app: Windows then
flashes the window in the taskbar instead.

## From the page

Services get the calling window as a parameter (see
[bindings.md](bindings.md#parameters-oriel-passes)), which is how a page
drives its own window:

```csharp
[Service]
public class Shell
{
    public void Minimize(OrielWindow window) => window.Minimize();

    public void ToggleMaximized(OrielWindow window)
    {
        if (window.State == WindowState.Maximized)
            window.Restore();
        else
            window.Maximize();
    }

    public void Close(OrielWindow window) => window.Close();
}
```

## Dropped files

Files dropped on a window come with their full paths, which the page's
`File` objects lack, to open documents in place. The app gets them first,
on the UI thread:

```csharp
window.FilesDropped += (_, e) =>
{
    foreach (var path in e.Paths)
        documents.Open(path);
};
```

then the page, through its client, with where they were dropped in CSS
pixels of the page:

```ts
import { onFilesDropped } from "./oriel";

onFilesDropped(({ paths, x, y }) => {
  const target = document.elementFromPoint(x, y);
  // ...
});
```

The page's own `drop` event still fires, with the `File` objects, before
either. Oriel lets files be dropped anywhere on the page, and keeps the
webview from opening a dropped file in place of the page. A page that
refuses a drop somewhere sets `dataTransfer.dropEffect = "none"` in its
`dragover` listener there.

## Remembering the placement

With `StateKey`, a window saves its placement as it closes: its position
and size, and whether it was maximized. The next window of that key opens
the same way, on the same monitor, at the size it had there, even when the
monitors have different scales. When that monitor is gone, the window
opens where it would without a key. A minimized window comes back
restored, and a full-screen one where it was before.

Placements are in `windows.json` in `OrielApp.DataDirectory`, by key; a
file that cannot be read is ignored. Two kinds of windows, say `"main"`
and `"settings"`, each keep their own.

## Frameless windows

`Frameless = true` gives a window without a title bar: the page is all of
it, and draws its own title bar and buttons. The window keeps its border,
its shadow and its rounded corners on Windows 11; it can be resized from
its sides and bottom.

Parts of the page with `app-region: drag` (or `-webkit-app-region: drag`)
move the window as a title bar would. Buttons inside them need
`app-region: no-drag` to be clicked:

```html
<header class="title-bar">
  <span>My app</span>
  <button id="minimize">–</button>
  <button id="close">×</button>
</header>
```

```css
.title-bar { app-region: drag; height: 32px; display: flex; }
.title-bar button { app-region: no-drag; }
```

```js
document.getElementById("minimize").onclick = () => Shell.minimize();
document.getElementById("close").onclick = () => Shell.close();
```

A frameless window shows no menu bar: the accelerators of its `Menu` still
work (see [menus.md](menus.md#accelerators)), and context menus too.
