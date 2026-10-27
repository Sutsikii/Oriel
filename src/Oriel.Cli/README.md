# oriel

The command of [Oriel](https://github.com/Sutsikii/Oriel) apps: desktop
apps in C#, with a web frontend calling C# through a generated TypeScript
client.

```sh
dotnet tool install -g Oriel.Cli
oriel doctor      # what building, running and shipping an app needs, here
oriel dev         # the app of this folder, with its frontend's dev server
```

`oriel doctor` checks the .NET SDK, Node.js, and per system the WebView2
Runtime, the C++ build tools and Inno Setup (Windows), GTK and WebKitGTK
(Linux), or the Xcode command line tools (macOS), and says how to get what
is missing.

`oriel dev` starts Vite in `frontend/`, then the app with `dotnet watch`,
pointed at Vite: editing the page reloads it, editing C# restarts the app
and rewrites the TypeScript client.
