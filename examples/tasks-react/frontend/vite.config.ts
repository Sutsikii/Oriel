import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The app serves the built files at https://app.oriel/, and `npm run dev`
// points it at this server instead.
export default defineConfig({
  plugins: [react()],
  // oriel-react comes from the repository's packages, which have a React of their own: one React only.
  resolve: { dedupe: ["react", "react-dom"] },
  server: { port: 5173, strictPort: true },
  build: { outDir: "dist", emptyOutDir: true },
});
