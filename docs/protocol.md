# The page protocol

Pages and the app exchange JSON messages as strings: pages with
`chrome.webview.postMessage`, the app with `PostWebMessageAsString`. The page
side is [`bridge.js`](../src/Oriel/Bridge/bridge.js), injected at document
start; the app side is [`PageConnection`](../src/Oriel/Ipc/PageConnection.cs).
Generated code never deals with messages: the TypeScript client calls
`window.oriel`, and dispatchers get parsed arguments.

## Tokens

Each page picks a random token when its runtime starts, and sends it with
every message. The app keeps the token of the current page of each window:

- a message with a new token means a new page, after a navigation or a
  reload: the calls of the previous page are canceled (their
  `CancellationToken`) and their results are never sent;
- replies carry the token of the page that made the call, and a page ignores
  replies that are not its own.

So a reply can never resolve a promise of a page that did not make the call.

## Messages from pages

### `hello`

Sent by the runtime as it starts, before the page's scripts run.

```json
{ "t": "hello", "k": "3f9a…" }
```

### `call`

Calls `Service.Method` with an array of arguments, in the order of the
method's parameters, without the ones Oriel passes itself (see
[bindings.md](bindings.md#parameters-oriel-passes)).

```json
{ "t": "call", "id": 1, "k": "3f9a…", "m": "Greeter.Greet", "a": ["Ada"] }
```

`id` numbers the calls of a page, from 1.

### `key`

The page let a key with Ctrl or Alt, or a function key, go by: no listener
called `preventDefault()` on its `keydown`. It may be the accelerator of
the window's menu (see [menus.md](menus.md#accelerators)). `c` is the
key's `keyCode`, a Windows virtual-key code; `m` the modifiers, Ctrl 1,
Shift 2 and Alt 4. Keys typed with AltGr, and Shift, Ctrl or Alt alone,
are not sent.

```json
{ "t": "key", "k": "3f9a…", "c": 83, "m": 1 }
```

The runtime sees keys before the page's listeners, and sends them once
they all ran.

### `drop`

Files were dropped on the page, at `x` and `y`, its `clientX` and
`clientY`. The runtime sends it with `postMessageWithAdditionalObjects`, the
dropped `File` objects attached, and the backend reads their paths: the
message itself has none. A runtime without that function sends nothing.

```json
{ "t": "drop", "k": "3f9a…", "x": 120, "y": 80 }
```

The runtime lets dragged files be dropped anywhere on the page, and keeps
the webview from opening a dropped file in place of the page; the page's
own `dragover` and `drop` listeners still run.

### `ack`

The page took payloads of the coalesced events `n` (see
[`event`](#event)), and is ready for their next ones. The runtime sends
one at the next frame, with every coalesced event it got since the last.

```json
{ "t": "ack", "k": "3f9a…", "n": ["Tracker.PenMoved"] }
```

## Messages from the app

### `result`

The call `id` returned. `v` is the result, absent for methods that return
nothing.

```json
{ "t": "result", "id": 1, "k": "3f9a…", "v": "Hello, Ada" }
```

### `error`

The call `id` failed: the method threw, or the arguments did not fit. `e` is
the message, a string; were it anything else, the runtime would use
`"Service.Method failed"`. The runtime rejects the call's promise with an `Error` named
`CallError` with a `method` property; the generated client rethrows it as its
`CallError` class.

```json
{ "t": "error", "id": 1, "k": "3f9a…", "e": "argument 'name': expected a string, got a number" }
```

When the method threw a `[ServiceError]` exception, `c` is its code and `d`
its data, which the `CallError` gets as `code` and `data` (see
[bindings.md](bindings.md#errors-pages-can-tell-apart)):

```json
{ "t": "error", "id": 1, "k": "3f9a…", "e": "/d.json changed on disk", "c": "conflict", "d": { "path": "/d.json", "version": 4 } }
```

### `event`

An `Event<T>` of a service emitted `v`. Events have no token: they go to the
page that is there.

```json
{ "t": "event", "n": "Clock.Tick", "v": "2026-10-21T12:00:00+02:00" }
```

A coalesced event (`Coalesce = true`, see
[bindings.md](bindings.md#coalesced-events)) has `"c": 1`. The app sends a
page the next payload of such an event once the page acknowledges the one
before with an [`ack`](#ack), and keeps only the latest in the meantime. A
new page starts with no payload waiting.

```json
{ "t": "event", "n": "Tracker.PenMoved", "v": { "x": 12.5, "y": 40 }, "c": 1 }
```

The window sends one event of its own, `oriel:filesDropped`, to its page
after a `drop`: the paths, and where the files were dropped. No service
event has that name, since C# names have no colon.

```json
{ "t": "event", "n": "oriel:filesDropped", "v": { "paths": ["C:\Users\ada\notes.txt"], "x": 120, "y": 80 } }
```

## Who may send messages

Only the app's own pages may call services: documents of
`https://app.oriel`, or of the dev server in development. Messages from any
other origin, such as a page the window navigated to, are ignored. The
runtime only starts in the top document of a window, not in frames.

## Limits

A page with a bug, or made to misbehave, could send messages that take the
app's memory or threads. So messages are refused when they are:

| Refused when | Limit (`AppOptions`) | Default |
|---|---|---|
| longer than the limit, before they are parsed | `MaxMessageLength`, in characters | 16 Mi |
| nested deeper, or not JSON | `MaxMessageDepth` | 64 |
| a call while the window's pages run as many calls | `MaxCallsInFlight` | 1000 |

A refused call fails at once: its promise rejects with a `CallError`. Other
refused messages get no answer. Each refusal goes to `OrielApp.Error`, and
to the trace with `ORIEL_TRACE=1`.

## Where pages may go

A window stays on the app. When its page navigates or opens a window,
Oriel decides:

| The page goes to | Navigation | New window (`window.open`, `target="_blank"`) |
|---|---|---|
| A page of the app (or of the dev server) | Allowed | Opens an `OrielWindow` of that page |
| Another web page (`http`, `https`) | Opened in the system's browser; the window stays | Same |
| A `mailto:` link | Opened in the mail app | Same |
| `about:`, `data:`, `blob:` | Allowed | Refused |
| Anything else (`file:`, other apps' schemes…) | Refused | Refused |

Only `http`, `https` and `mailto` ever reach the system: other schemes could
start programs. `OrielWindow.NavigationRequested` is raised first, and
handlers may change the decision.

Permission requests (camera, microphone, notifications, geolocation…) are
denied, unless `WindowOptions.Permissions` grants them, and only to the app's
own pages.

## Compatibility

Messages of an unknown `t` are ignored on both sides, so that the protocol can
grow. Fields are only ever added to messages, never changed.
