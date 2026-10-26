# Oriel templates

Templates for `dotnet new`, to start a desktop app with
[Oriel](https://github.com/Sutsikii/Oriel): C# services, and a Vite and
TypeScript frontend that calls them through a generated client.

```sh
dotnet new install Oriel.Templates
dotnet new oriel -o MyApp
cd MyApp/frontend
npm install
npm run dev
```

`npm run dev` starts Vite and the app together: editing the page reloads
it, editing C# restarts the app and rewrites the TypeScript client.
`dotnet publish -c Release -r win-x64` gives the native executable.
