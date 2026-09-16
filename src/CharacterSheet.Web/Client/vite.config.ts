import { defineConfig } from "vite";

export default defineConfig({
    build: {
        lib: {
            entry: "src/app.ts",
            formats: ["es"],
            fileName: () => "app.js"
        },
        outDir: "../wwwroot",
        emptyOutDir: true,
        sourcemap: true
    }
});
