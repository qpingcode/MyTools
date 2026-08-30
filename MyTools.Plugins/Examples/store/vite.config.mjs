import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "node:path";

export default defineConfig({
    root: "src/web",
    base: "./",
    plugins: [vue()],
    resolve: {
        alias: { "@": path.resolve("src/web") },
    },
    build: {
        outDir: path.resolve("dist/web"),
        emptyOutDir: true,
        cssCodeSplit: false,
        chunkSizeWarningLimit: 2000,
        rollupOptions: {
            output: {
                entryFileNames: "main.js",
                chunkFileNames: "chunk-[name].js",
                assetFileNames: (info) => {
                    if (info.names?.some((name) => name.endsWith(".css")) || info.name?.endsWith(".css")) {
                        return "style.css";
                    }
                    return "assets/[name][extname]";
                },
            },
        },
    },
});
