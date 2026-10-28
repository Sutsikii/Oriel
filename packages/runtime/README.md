# oriel-runtime

The runtime of [Oriel](https://github.com/Sutsikii/Oriel)'s TypeScript clients: calls, events and streams to C#, and the `CallError` they reject with.

A generated client carries it unless the app's project sets `OrielTypeScriptRuntime` to `oriel-runtime`: the client then imports it from here, and framework adapters share it.

See [packages.md](https://github.com/Sutsikii/Oriel/blob/main/docs/packages.md).
