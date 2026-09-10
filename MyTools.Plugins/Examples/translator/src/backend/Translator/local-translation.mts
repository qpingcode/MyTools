import fs from "node:fs";
import path from "node:path";
import http, { type Agent, type IncomingMessage } from "node:http";
import https from "node:https";
import { spawn, type ChildProcess } from "node:child_process";
import { DatabaseSync } from "node:sqlite";
import { createHash } from "node:crypto";
import { pipeline } from "node:stream/promises";
import { createReadStream, createWriteStream } from "node:fs";
import { HttpProxyAgent } from "http-proxy-agent";
import { HttpsProxyAgent } from "https-proxy-agent";
import { SocksProxyAgent } from "socks-proxy-agent";

import { DATA_DIR, normalizeText } from "../common/storage.mjs";

const DEFAULT_PORT = 39281;
const SERVER_START_TIMEOUT_MS = 30_000;

export type LocalDictionaryResult = {
  phonetic: string;
  definitions: { meaning: string; example: string }[];
  chineseTranslation: string;
};

export class LocalTranslationError extends Error {
  constructor(
    public readonly code: "missing-server" | "missing-model" | "missing-dictionary" | "server-start" | "request-failed" | "empty-result",
    public readonly detail = "",
  ) {
    super(code);
  }
}

let serverProcess: ChildProcess | null = null;
let serverStartPromise: Promise<string> | null = null;
let serverStartError = "";
let dictionary: DatabaseSync | null = null;

type ResourceManifestEntry = {
  path: string;
  url: string;
  size: number;
  sha256: string;
};

type ResourceManifest = {
  version: string;
  totalSize?: number;
  files: ResourceManifestEntry[];
};

export type LocalResourceStatus = {
  state: "missing" | "downloading" | "installed" | "error" | "cancelled";
  downloadedBytes: number;
  totalBytes: number;
  currentFile: string;
  error: string;
};

let installStatus: LocalResourceStatus = {
  state: "missing",
  downloadedBytes: 0,
  totalBytes: 0,
  currentFile: "",
  error: "",
};
let installPromise: Promise<void> | null = null;
let installAbortController: AbortController | null = null;

function configuredPath(environmentName: string, fallback: string): string {
  return path.resolve(normalizeText(process.env[environmentName]) || fallback);
}

function getServerExecutablePath(): string {
  return configuredPath(
    "MYTOOLS_TRANSLATOR_LLAMA_SERVER_PATH",
    path.join(DATA_DIR, "local", "llama", "llama-server.exe"),
  );
}

function getModelPath(): string {
  return configuredPath(
    "MYTOOLS_TRANSLATOR_HYMT_MODEL_PATH",
    path.join(DATA_DIR, "local", "models", "Hy-MT2-1.8B-Q4_K_M.gguf"),
  );
}

function getDictionaryPath(): string {
  return configuredPath(
    "MYTOOLS_TRANSLATOR_ECDICT_PATH",
    path.join(DATA_DIR, "local", "dictionaries", "ecdict.db"),
  );
}

function localResourcesExist(): boolean {
  const hasTranslationServer = Boolean(getConfiguredServerUrl())
    || (fs.existsSync(getServerExecutablePath()) && fs.existsSync(getModelPath()));
  return hasTranslationServer && fs.existsSync(getDictionaryPath());
}

export function getLocalResourceStatus(): LocalResourceStatus {
  if (!installPromise) {
    if (localResourcesExist()) {
      return { ...installStatus, state: "installed", error: "" };
    }
    if (installStatus.state === "installed") {
      return { ...installStatus, state: "missing", error: "" };
    }
  }
  return { ...installStatus };
}

function manifestUrl(): string {
  return normalizeText(process.env.MYTOOLS_TRANSLATOR_LOCAL_MANIFEST_URL)
    || "https://github.com/qpingcode/MyTools/releases/download/translator-local-v1/manifest.json";
}

function safeResourcePath(relativePath: string): string {
  const localRoot = path.resolve(DATA_DIR, "local");
  const destination = path.resolve(localRoot, relativePath);
  if (destination !== localRoot && !destination.startsWith(`${localRoot}${path.sep}`)) {
    throw new Error(`Invalid resource path: ${relativePath}`);
  }
  return destination;
}

type ResourceRequestContext = {
  proxyUrl: string;
  agents: Map<string, Agent>;
};

function getResourceAgent(target: URL, context: ResourceRequestContext): Agent | undefined {
  if (!context.proxyUrl) return undefined;
  const proxyProtocol = new URL(context.proxyUrl).protocol.toLowerCase();
  const agentKind = proxyProtocol.startsWith("socks") ? "socks" : target.protocol;
  const existing = context.agents.get(agentKind);
  if (existing) return existing;

  const agent = agentKind === "socks"
    ? new SocksProxyAgent(context.proxyUrl)
    : target.protocol === "https:"
      ? new HttpsProxyAgent(context.proxyUrl)
      : new HttpProxyAgent(context.proxyUrl);
  context.agents.set(agentKind, agent);
  return agent;
}

async function requestResource(
  url: string,
  signal: AbortSignal,
  context: ResourceRequestContext,
  headers?: Record<string, string>,
  redirectsRemaining = 10,
): Promise<IncomingMessage> {
  return await new Promise<IncomingMessage>((resolve, reject) => {
    const target = new URL(url);
    const request = target.protocol === "https:" ? https.request : http.request;
    const outgoing = request(target, {
      method: "GET",
      headers,
      signal,
      agent: getResourceAgent(target, context),
    }, (response) => {
      const status = response.statusCode || 0;
      const location = response.headers.location;
      if (location && [301, 302, 303, 307, 308].includes(status)) {
        response.resume();
        if (redirectsRemaining <= 0) {
          reject(new Error(`Too many redirects: ${url}`));
          return;
        }
        void requestResource(
          new URL(location, target).toString(),
          signal,
          context,
          headers,
          redirectsRemaining - 1,
        ).then(resolve, reject);
        return;
      }
      resolve(response);
    });
    outgoing.on("error", reject);
    outgoing.end();
  });
}

async function readResponseText(response: IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of response) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }
  return Buffer.concat(chunks).toString("utf8");
}

async function downloadResource(
  entry: ResourceManifestEntry,
  signal: AbortSignal,
  context: ResourceRequestContext,
): Promise<void> {
  const destination = safeResourcePath(entry.path);
  const partialPath = `${destination}.part`;
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  let existingBytes = fs.existsSync(partialPath) ? fs.statSync(partialPath).size : 0;
  if (entry.size > 0 && existingBytes === entry.size) {
    if (await sha256File(partialPath) === entry.sha256.toLowerCase()) {
      installStatus.downloadedBytes += existingBytes;
      fs.renameSync(partialPath, destination);
      return;
    }
    fs.rmSync(partialPath, { force: true });
    existingBytes = 0;
  }
  const headers = existingBytes > 0 ? { Range: `bytes=${existingBytes}-` } : undefined;
  const response = await requestResource(entry.url, signal, context, headers);
  const status = response.statusCode || 0;
  if (status < 200 || status >= 300) {
    response.resume();
    throw new Error(`${status} ${response.statusMessage || "Download failed"}: ${entry.url}`);
  }

  const resumed = existingBytes > 0 && status === 206;
  if (!resumed) {
    existingBytes = 0;
  }
  installStatus.downloadedBytes += existingBytes;

  let fileBytes = 0;
  response.on("data", (chunk: Buffer) => {
    fileBytes += chunk.length;
    installStatus.downloadedBytes += chunk.length;
  });
  await pipeline(response, createWriteStream(partialPath, { flags: resumed ? "a" : "w" }), { signal });
  const completedSize = existingBytes + fileBytes;
  if (entry.size > 0 && completedSize !== entry.size) {
    if (completedSize > entry.size) fs.rmSync(partialPath, { force: true });
    throw new Error(`Size mismatch for ${entry.path}`);
  }
  const actualHash = await sha256File(partialPath);
  if (!entry.sha256 || actualHash !== entry.sha256.toLowerCase()) {
    fs.rmSync(partialPath, { force: true });
    throw new Error(`SHA-256 mismatch for ${entry.path}`);
  }
  fs.renameSync(partialPath, destination);
}

async function sha256File(filePath: string): Promise<string> {
  const hash = createHash("sha256");
  await pipeline(createReadStream(filePath), hash);
  return hash.digest("hex").toLowerCase();
}

export async function installLocalResources(proxyUrl = ""): Promise<void> {
  if (installPromise) {
    return installPromise;
  }
  installAbortController = new AbortController();
  const signal = installAbortController.signal;
  installPromise = (async () => {
    const requestContext: ResourceRequestContext = { proxyUrl, agents: new Map() };
    installStatus = { state: "downloading", downloadedBytes: 0, totalBytes: 0, currentFile: "", error: "" };
    try {
      const response = await requestResource(manifestUrl(), signal, requestContext);
      const status = response.statusCode || 0;
      if (status < 200 || status >= 300) {
        response.resume();
        throw new Error(`${status} ${response.statusMessage || "Download failed"}`);
      }
      const manifest = JSON.parse(await readResponseText(response)) as ResourceManifest;
      if (!Array.isArray(manifest.files) || manifest.files.length === 0) {
        throw new Error("The local resource manifest has no files.");
      }
      installStatus.totalBytes = Number(manifest.totalSize)
        || manifest.files.reduce((sum, entry) => sum + (Number(entry.size) || 0), 0);
      for (const entry of manifest.files) {
        installStatus.currentFile = entry.path;
        const destination = safeResourcePath(entry.path);
        if (fs.existsSync(destination) && entry.size > 0 && fs.statSync(destination).size === entry.size) {
          if (await sha256File(destination) === entry.sha256.toLowerCase()) {
            installStatus.downloadedBytes += entry.size;
            continue;
          }
          fs.rmSync(destination, { force: true });
        }
        await downloadResource(entry, signal, requestContext);
      }
      if (!localResourcesExist()) {
        throw new Error("The downloaded package is missing llama-server.exe, HY-MT2, or ECDICT.");
      }
      dictionary?.close();
      dictionary = null;
      installStatus = {
        ...installStatus,
        state: "installed",
        downloadedBytes: installStatus.totalBytes,
        currentFile: "",
      };
    } catch (error) {
      installStatus = {
        ...installStatus,
        state: signal.aborted ? "cancelled" : "error",
        currentFile: "",
        error: signal.aborted ? "" : (error instanceof Error ? error.message : String(error)),
      };
    } finally {
      for (const agent of requestContext.agents.values()) agent.destroy();
      installAbortController = null;
      installPromise = null;
    }
  })();
  return installPromise;
}

export function cancelLocalResourceInstall(): void {
  installAbortController?.abort();
}

function getConfiguredServerUrl(): string {
  return normalizeText(process.env.MYTOOLS_TRANSLATOR_LLAMA_SERVER_URL).replace(/\/$/, "");
}

async function isServerReady(serverUrl: string): Promise<boolean> {
  try {
    const response = await fetch(`${serverUrl}/health`, { signal: AbortSignal.timeout(1_000) });
    return response.ok;
  } catch {
    return false;
  }
}

async function waitForServer(serverUrl: string): Promise<string> {
  const deadline = Date.now() + SERVER_START_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (await isServerReady(serverUrl)) {
      return serverUrl;
    }
    if (serverProcess && serverProcess.exitCode !== null) {
      throw new LocalTranslationError("server-start", `exit code ${serverProcess.exitCode}`);
    }
    if (serverStartError) {
      throw new LocalTranslationError("server-start", serverStartError);
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new LocalTranslationError("server-start", "startup timed out");
}

async function ensureServer(): Promise<string> {
  const configuredUrl = getConfiguredServerUrl();
  if (configuredUrl) {
    if (!await isServerReady(configuredUrl)) {
      throw new LocalTranslationError("server-start", configuredUrl);
    }
    return configuredUrl;
  }

  const serverUrl = `http://127.0.0.1:${DEFAULT_PORT}`;
  if (await isServerReady(serverUrl)) {
    return serverUrl;
  }
  if (serverStartPromise) {
    return serverStartPromise;
  }

  const executablePath = getServerExecutablePath();
  if (!fs.existsSync(executablePath)) {
    throw new LocalTranslationError("missing-server", executablePath);
  }
  const modelPath = getModelPath();
  if (!fs.existsSync(modelPath)) {
    throw new LocalTranslationError("missing-model", modelPath);
  }

  serverStartPromise = (async () => {
    serverStartError = "";
    serverProcess = spawn(executablePath, [
      "--model", modelPath,
      "--host", "127.0.0.1",
      "--port", String(DEFAULT_PORT),
      "--ctx-size", "4096",
      "--parallel", "1",
      "--jinja",
      "--no-webui",
    ], {
      cwd: path.dirname(executablePath),
      windowsHide: true,
      stdio: "ignore",
    });
    serverProcess.once("exit", (code) => {
      serverStartError = `llama-server exited with code ${code ?? "unknown"}`;
      serverProcess = null;
      serverStartPromise = null;
    });
    serverProcess.once("error", (error) => {
      serverStartError = error.message;
      serverProcess = null;
      serverStartPromise = null;
    });
    return waitForServer(serverUrl);
  })();

  try {
    return await serverStartPromise;
  } catch (error) {
    serverProcess?.kill();
    serverProcess = null;
    serverStartPromise = null;
    throw error;
  }
}

function closeServer(): void {
  if (serverProcess && serverProcess.exitCode === null) {
    serverProcess.kill();
  }
  serverProcess = null;
  serverStartPromise = null;
  serverStartError = "";
  dictionary?.close();
  dictionary = null;
}

process.once("exit", closeServer);
process.once("SIGINT", () => {
  closeServer();
  process.exit(0);
});
process.once("SIGTERM", () => {
  closeServer();
  process.exit(0);
});

function getDictionary(): DatabaseSync {
  if (dictionary) {
    return dictionary;
  }
  const dictionaryPath = getDictionaryPath();
  if (!fs.existsSync(dictionaryPath)) {
    throw new LocalTranslationError("missing-dictionary", dictionaryPath);
  }
  dictionary = new DatabaseSync(dictionaryPath, { readOnly: true });
  return dictionary;
}

function splitDefinitions(value: unknown): { meaning: string; example: string }[] {
  return normalizeText(value)
    .split(/\r?\n|(?<=\S);\s+/)
    .map((meaning) => meaning.trim())
    .filter(Boolean)
    .slice(0, 3)
    .map((meaning) => ({ meaning, example: "" }));
}

export function lookupEnglishWord(word: string): LocalDictionaryResult | null {
  const database = getDictionary();
  let row: Record<string, unknown> | undefined;
  try {
    row = database.prepare(
      "SELECT phonetic, definition, translation FROM stardict WHERE word = ? COLLATE NOCASE LIMIT 1",
    ).get(word) as Record<string, unknown> | undefined;
  } catch (error) {
    throw new LocalTranslationError("missing-dictionary", error instanceof Error ? error.message : String(error));
  }
  if (!row) {
    return null;
  }
  return {
    phonetic: normalizeText(row.phonetic),
    definitions: splitDefinitions(row.definition),
    chineseTranslation: normalizeText(row.translation).replace(/\r?\n/g, "；"),
  };
}

function isChinese(text: string): boolean {
  return /[\u3400-\u9fff\uf900-\ufaff]/u.test(text);
}

export async function translateWithHyMt(text: string): Promise<string> {
  const serverUrl = await ensureServer();
  const targetLanguage = isChinese(text) ? "英语" : "简体中文";
  const prompt = `将以下文本翻译成${targetLanguage}，只输出译文，不要解释：\n${text}`;
  let response: Response;
  try {
    response = await fetch(`${serverUrl}/v1/chat/completions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        model: "Hy-MT2-1.8B",
        messages: [{ role: "user", content: prompt }],
        temperature: 0.1,
        top_p: 0.6,
        top_k: 20,
        repetition_penalty: 1.05,
        max_tokens: 1024,
        stream: false,
      }),
      signal: AbortSignal.timeout(45_000),
    });
  } catch (error) {
    throw new LocalTranslationError("request-failed", error instanceof Error ? error.message : String(error));
  }
  if (!response.ok) {
    throw new LocalTranslationError("request-failed", `${response.status}: ${await response.text()}`);
  }
  const body = await response.json() as { choices?: { message?: { content?: unknown } }[] };
  const translated = normalizeText(body.choices?.[0]?.message?.content);
  if (!translated) {
    throw new LocalTranslationError("empty-result");
  }
  return translated;
}
