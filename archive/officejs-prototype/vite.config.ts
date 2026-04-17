import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import { resolve } from "path";
import { readFileSync } from "fs";
import { homedir } from "os";

const certDir = resolve(homedir(), ".office-addin-dev-certs");

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      "@": resolve(__dirname, "src"),
    },
  },
  server: {
    port: 3000,
    https: {
      key: readFileSync(resolve(certDir, "localhost.key")),
      cert: readFileSync(resolve(certDir, "localhost.crt")),
      ca: readFileSync(resolve(certDir, "ca.crt")),
    },
  },
  build: {
    rollupOptions: {
      input: {
        taskpane: resolve(__dirname, "taskpane.html"),
      },
    },
  },
});
