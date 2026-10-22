// Served from the test assembly's resources: reports what the webview got.
try {
  const types = {};
  for (const file of ["index.html", "app.js", "assets/style.css", "assets/pic.svg", "assets/empty.wasm", "assets/font.woff2"]) {
    const response = await fetch(file);
    types[file] = response.status + " " + response.headers.get("content-type");
  }
  const missing = (await fetch("nope.js")).status;
  // Compiling while downloading needs the wasm content type.
  const wasm = (await WebAssembly.compileStreaming(fetch("assets/empty.wasm"))) instanceof WebAssembly.Module;
  const color = getComputedStyle(document.body).color;
  await oriel.call("E2E.Report", [JSON.stringify({ types, missing, wasm, color, origin: location.origin })]);
} catch (err) {
  await oriel.call("E2E.Report", ["page error: " + err]);
}
