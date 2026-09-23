import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Built straight into the API's wwwroot, so the page ships and deploys with the backend and is served
// from the same origin that creates the liveness sessions.
export default defineConfig({
  plugins: [react()],
  base: "/liveness/",
  build: {
    outDir: "../src/Aynera.Api/wwwroot/liveness",
    emptyOutDir: true,
  },
});
