# Oriel.SecureStorage

An [Oriel](https://github.com/Sutsikii/Oriel) plugin: the app's secrets
(tokens, passwords), encrypted for the user who runs it with DPAPI, for C#
and for pages.

```csharp
app.Bind(new SecureStorage(app));
```

```ts
import { SecureStorage } from "./oriel";

await SecureStorage.set("github-token", token);
const token = await SecureStorage.get("github-token"); // string | null
await SecureStorage.delete("github-token");
const keys = await SecureStorage.keys();
```

Windows only, for now. See [plugins.md](https://github.com/Sutsikii/Oriel/blob/main/docs/plugins.md).
