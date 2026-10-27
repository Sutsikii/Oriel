# 3D viewer

A 3D viewer: glTF models rendered with three.js, their bytes brought by
the binary channel.

- `Models.knot` makes a torus knot in C# as a glTF binary file (.glb),
  from a few thousand triangles to over a million with the slider, and
  returns it as `ReadOnlyMemory<byte>`: the page gets a `Uint8Array`, in a
  buffer WebView2 shares with it, no JSON or base64 on the way
  ([bindings.md](../../docs/bindings.md#binary-data)).
- `Models.open` asks for a .glb file with the system's dialog, and sends
  its bytes the same way.
- three.js's `GLTFLoader` parses the bytes as they are.

```sh
cd frontend
npm install
npm run dev
```

`dotnet publish -c Release -r win-x64` gives a native executable, as CI does.
