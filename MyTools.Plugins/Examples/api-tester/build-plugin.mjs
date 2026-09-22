import {build, context} from "esbuild";
import fs from "node:fs";
import path from "node:path";
import {fileURLToPath} from 'node:url';
import {requestDevelopmentPluginRefresh} from "@qping/plugin-bus/dev";
import {parse, compileScript} from "@vue/compiler-sfc";

const RefreshDebounceMs = 75;
const vuePlugin = {
    name: 'vue-sfc',
    setup(build) {
        build.onLoad({filter: /\.vue$/}, async ({path: filename}) => {
            const source = await fs.promises.readFile(filename, 'utf8');
            const {descriptor, errors} = parse(source, {filename});
            if (errors.length) throw errors[0];
            const compiled = compileScript(descriptor, {id: filename, inlineTemplate: true});
            return {
                contents: compiled.content,
                loader: 'ts',
                resolveDir: path.dirname(filename),
                watchFiles: [filename]
            };
        });
    }
};

const watching = process.argv.includes("--watch");
const outputDirectory = path.resolve('dist');
const sourceDirectory = path.dirname(fileURLToPath(import.meta.url));
if (path.dirname(outputDirectory) !== sourceDirectory) throw new Error('Run the build from the plugin directory.');
const pluginId = "api-tester";
let refreshTimer;
let readyReported = false;

function requestMyToolsRefresh() {
    clearTimeout(refreshTimer);
    refreshTimer = setTimeout(async () => {
        try {
            await requestDevelopmentPluginRefresh(pluginId);
            if (!readyReported) {
                readyReported = true;
                console.log(`[MyTools] Plugin "${pluginId}" is registered and ready to test.`);
            } else {
                console.log(`[MyTools] Plugin "${pluginId}" refreshed.`);
            }
        } catch (error) {
            console.warn("[MyTools] " + error.message);
        }
    }, RefreshDebounceMs);
}

const refreshPlugin = {
    name: "refresh-mytools",
    setup(build) {
        build.onEnd((result) => {
            if (watching && result.errors.length === 0) requestMyToolsRefresh();
        });
    }
};

const builds = [
    {
        entryPoints: ["src/backend/index.mts"],
        bundle: true,
        platform: "node",
        format: "esm",
        target: "es2024",
        outfile: "dist/backend/index.mjs",
        plugins: [refreshPlugin]
    },
    {
        entryPoints: {
            main: "src/web/main.ts",
            "response-formatter.worker": "src/web/features/response/responseFormatter.worker.ts"
        },
        bundle: true,
        format: "iife",
        target: "es2024",
        outdir: "dist/web",
        plugins: [vuePlugin, refreshPlugin],
        define: {
            __VUE_OPTIONS_API__: 'false',
            __VUE_PROD_DEVTOOLS__: 'false',
            __VUE_PROD_HYDRATION_MISMATCH_DETAILS__: 'false',
            'process.env.NODE_ENV': '"production"'
        }
    }
];

function copyStatic() {
    fs.mkdirSync("dist", {recursive: true});
    fs.copyFileSync("plugin.json", "dist/plugin.json");
    fs.cpSync("i18n", "dist/i18n", {recursive: true});
    fs.mkdirSync("dist/web", {recursive: true});
    fs.copyFileSync("src/web/index.html", "dist/web/index.html");
    fs.copyFileSync("src/web/style.css", "dist/web/style.css");
}

if (!watching) {
    fs.rmSync(outputDirectory, {recursive: true, force: true});
    await Promise.all(builds.map((options) => build(options)));
    copyStatic();
} else {
    copyStatic();
    const contexts = await Promise.all(builds.map((options) => context(options)));
    await Promise.all(contexts.map((item) => item.watch()));
    fs.watch("plugin.json", () => {
        copyStatic();
        requestMyToolsRefresh();
    });
    fs.watch("i18n", {recursive: true}, () => {
        copyStatic();
        requestMyToolsRefresh();
    });
    fs.watch("src/web", {recursive: true}, (_event, file) => {
        if (file === "index.html" || file === "style.css") {
            copyStatic();
            requestMyToolsRefresh();
        }
    });
    console.log("Watching plugin sources...");
}

