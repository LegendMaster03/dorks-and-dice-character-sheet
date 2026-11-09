import { defineConfig } from "vite";

// Publish a predictable ES-module asset into the web host's wwwroot.
// The embedded Tool Host and standalone routes load the same app.js entry point.
export default defineConfig({
    build: {
        lib: {
            entry: "src/app.ts",
            formats: ["es"],
            fileName: () => "app.js",
            cssFileName: "app"
        },
        outDir: "../wwwroot",
        emptyOutDir: true,
        sourcemap: true
    }
});
