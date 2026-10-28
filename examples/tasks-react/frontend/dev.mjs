// Development: starts Vite, then the app with `dotnet watch`, pointed at
// Vite through ORIEL_DEV_URL. Editing the frontend reloads the page; editing
// C# rebuilds and restarts the app, which rewrites src/oriel.ts.
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";

const server = await createServer({ configFile: fileURLToPath(new URL("./vite.config.ts", import.meta.url)) });
await server.listen();
const url = server.resolvedUrls.local[0];
server.printUrls();

const app = spawn("dotnet", ["watch", "run", "--project", ".."], {
  stdio: "inherit",
  env: { ...process.env, ORIEL_DEV_URL: url, DOTNET_WATCH_RESTART_ON_RUDE_EDIT: "true" },
  shell: process.platform === "win32",
});

const stop = async (code) => {
  await server.close();
  process.exit(code ?? 0);
};
app.on("exit", stop);
process.on("SIGINT", () => app.kill("SIGINT"));
