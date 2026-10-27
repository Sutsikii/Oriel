import { defineConfig } from "vite";

// The app serves the built files at https://app.oriel/, and `npm run dev`
// points it at this server instead.
export default defineConfig({
  server: { port: 5173, strictPort: true },
  build: { outDir: "dist", emptyOutDir: true },
});
