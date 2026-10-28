# Tasks, with React

A task list in React, on the [npm packages](../../docs/packages.md):

- the project sets `OrielTypeScriptRuntime` to `oriel-runtime`, so its
  generated client imports the runtime rather than carrying it;
- `useOrielQuery(() => Tasks.all(), [], { refreshOn: [Tasks.onChanged] })`
  calls C# and calls again whenever a window changes the list;
- `useOrielEvent(Tasks.onChanged, setLeft)` listens while mounted;
- `Tasks.add` throws a `[ServiceError("invalid-title")]`, which the form
  shows.

The frontend takes the packages from the repository (`file:`): build them
first, once.

```sh
cd ../../packages && npm install && npm run build
cd ../examples/tasks-react/frontend
npm install
npm run dev
```
