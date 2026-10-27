# Oriel templates

Templates for `dotnet new`, to start a desktop app with
[Oriel](https://github.com/Sutsikii/Oriel): C# services, and a Vite and
TypeScript frontend that calls them through a generated client.

```sh
dotnet new install Oriel.Templates
dotnet new oriel -o MyApp --framework react   # or vanilla (the default), vue, svelte
cd MyApp/frontend
npm install
npm run dev
```

`npm run dev` starts Vite and the app together: editing the page reloads
it, editing C# restarts the app and rewrites the TypeScript client. With
the `oriel` command (`dotnet tool install -g Oriel.Cli`), `oriel dev` in
`MyApp` does the same, and `oriel doctor` checks what the machine needs.
`dotnet publish -c Release -r win-x64` gives the native executable.
