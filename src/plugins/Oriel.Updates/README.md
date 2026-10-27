# Oriel.Updates

Updates for [Oriel](https://github.com/Sutsikii/Oriel) apps, with
[Velopack](https://velopack.io): check for a newer version, download it
with progress, and restart the app into it, from C# or from pages.

```csharp
Updates.Run(); // first in Main
app.Bind(new Updates("https://example.com/releases"));
```

```ts
const update = await Updates.check();
if (update) {
  Updates.onProgress((percent) => (bar.value = percent));
  await Updates.download();
  await Updates.restart();
}
```

See [updates.md](https://github.com/Sutsikii/Oriel/blob/main/docs/updates.md),
which also says how to make releases with `vpk`.
