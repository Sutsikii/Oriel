# Menus

A window can have a menu bar, and show menus at a point, as a right click
does. Both follow the dark theme when Windows is set to dark apps.

## The menu bar

Give the window a `Menu` when it opens, or set `OrielWindow.Menu` later
from any thread; `null` removes the bar:

```csharp
var menu = new Menu(
    new MenuItem("&File")
    {
        Items =
        [
            new MenuItem("&Open…", "Ctrl+O") { Clicked = window => _ = Open(window) },
            new MenuItem("&Save", "Ctrl+S") { Clicked = window => _ = Save(window) },
            MenuItem.Separator(),
            new MenuItem(MenuRole.CloseWindow),
        ],
    },
    new MenuItem("&Edit")
    {
        Items = [new(MenuRole.Undo), new(MenuRole.Redo), MenuItem.Separator(), new(MenuRole.Cut), new(MenuRole.Copy), new(MenuRole.Paste)],
    },
    new MenuItem("&View")
    {
        Items = [new MenuItem("&Word Wrap", "Alt+Z") { IsCheckable = true, IsChecked = true, Clicked = window => Wrap(window) }],
    });

app.OpenWindow(new WindowOptions { Menu = menu });
```

`Clicked` runs on the UI thread with the window the item was clicked in, so
one `Menu` can be the bar of several windows. An exception it throws goes
to `OrielApp.Error`. A check item (`IsCheckable`) toggles `IsChecked`
before `Clicked` runs.

`&` marks the letter that opens a menu or picks an item from the keyboard
(Alt+F opens `&File`); `&&` shows an `&`.

`Text`, `Enabled` and `IsChecked` can change at any time, from any thread:
a menu shows what its items are when it opens. To change the items
themselves, or the titles of the bar, set `Menu` again.

## Accelerators

An accelerator is a key with Ctrl, Alt or both, and maybe Shift
(`Ctrl+S`, `Ctrl+Shift+Z`, `Alt+Left`), or a function key with or without
them (`F5`, `Shift+F10`). Keys without a modifier are the page's: typing
never runs a menu. The keys are letters, digits, `Plus`, `Minus`,
`Comma`, `Period`, the arrows, `Home`, `End`, `PageUp`, `PageDown`,
`Insert`, `Delete`, `Backspace`, `Tab`, `Enter`, `Space` and `Escape`.

**The page gets the keys first.** When a `keydown` listener of the page
calls `preventDefault()`, the menu does not run, so an editor keeps
Ctrl+Z, or a page its own Ctrl+K:

```js
addEventListener("keydown", (e) => {
  if (e.ctrlKey && e.key === "k") {
    e.preventDefault(); // the page's search, not the menu's
    openSearch();
  }
});
```

When the page leaves a key alone, its item runs, and the webview does not
use the key itself: an item on Ctrl+P or F5 replaces printing or reloading.
Alt and a mnemonic open the menu of the bar, and F10 selects the bar, as in
any Windows app.

Only the bar's enabled items run from the keyboard. A context menu shows
the accelerators of its items, without running them.

The keys reach the menu through the page, so they work once the page's
runtime started, in the app's own pages.

## Roles

`new MenuItem(MenuRole.Copy)` is an item that copies, with its text and
accelerator in English. Change them for another language:
`new MenuItem(MenuRole.Copy) { Text = "&Copier" }`.

| Role | Text | Accelerator | Does |
|---|---|---|---|
| `Undo`, `Redo` | Undo, Redo | Ctrl+Z, Ctrl+Y | Undoes or redoes the last edit of the page |
| `Cut`, `Copy`, `Paste` | Cut, Copy, Paste | Ctrl+X, Ctrl+C, Ctrl+V | Edits the page's selection, as the keys do |
| `Delete` | Delete | | Deletes the selection |
| `SelectAll` | Select All | Ctrl+A | Selects the field, or the page |
| `Reload` | Reload | Ctrl+R | Reloads the page |
| `DevTools` | Developer Tools | Ctrl+Shift+I | Opens them, when `WindowOptions.DevTools` allows |
| `Print` | Print… | Ctrl+P | Opens the print dialog |
| `CloseWindow` | Close | Ctrl+W | Closes the window |
| `Quit` | Exit | | Quits the app |

The editing roles act on the element that has the focus, as the keyboard
would; pasting works, which pages cannot do by themselves. Their keys do
what they do in the page already, so the menu only shows them. An item
with a role and `Clicked` does what `Clicked` does.

## Context menus

`ShowContextMenuAsync` shows a menu at the mouse pointer, or at a point of
the page, and runs the item clicked. A service gets the calling window as a
parameter (see [bindings.md](bindings.md#parameters-oriel-passes)):

```csharp
[Service]
public class Files
{
    private static readonly Menu FileMenu = new(
        new MenuItem("&Open"),
        new MenuItem("&Rename…", "F2"),
        MenuItem.Separator(),
        new MenuItem("&Delete"));

    public async Task<string?> Menu(OrielWindow window, double x, double y) =>
        (await window.ShowContextMenuAsync(FileMenu, x, y))?.Text;
}
```

```js
list.addEventListener("contextmenu", async (e) => {
  e.preventDefault(); // no menu of the webview's own
  const chosen = await Files.menu(e.clientX, e.clientY);
});
```

The point is in CSS pixels from the page's corner, as `clientX` and
`clientY` are. The task completes once the menu closed, with the item
clicked, after its `Clicked` ran; with null when the menu was closed
without a click, or the window is closed.

## The dark theme

When Windows is set to dark apps, the bar and the menus are dark, and
follow the setting when it changes. Windows has no documented way for
this: Oriel uses the functions of `uxtheme.dll` that Windows' own apps
use, and paints the bar itself. Where they are missing, menus stay light.
