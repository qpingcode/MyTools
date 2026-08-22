import { execFile } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { promisify } from "node:util";
import { createPlugin, HostAction } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const execFileAsync = promisify(execFile);
const ClipboardMaxLength = 1024;
const ClipboardPreviewLength = 220;

type IdeKey = "rider" | "vscode" | "visual-studio" | "intellij";
type ClipboardPayload = { kind: "file" | "text"; value: string };
type ResolvedExe = { path: string | null; source: "config" | "auto" | "none" };
type OwnConfiguration = { values?: Record<string, unknown> };

const SettingNames = {
  rider: "RiderInstallPath",
  vscode: "VsCodeInstallPath",
  visualStudio: "VisualStudioInstallPath",
  intellij: "IntelliJInstallPath",
} as const;

const IdeItems: { id: IdeKey; title: string; subtitle: string; icon: string }[] = [
  { id: "rider", title: "Open Rider from Clipboard", subtitle: "Find nearest .sln and open in Rider", icon: "🟣" },
  { id: "vscode", title: "Open VSCode from Clipboard", subtitle: "Open folder in VSCode", icon: "🔵" },
  {
    id: "visual-studio",
    title: "Open Visual Studio from Clipboard",
    subtitle: "Find nearest .sln and open in Visual Studio",
    icon: "🟪",
  },
  {
    id: "intellij",
    title: "Open Intellij from Clipboard",
    subtitle: "Open nearest project root in IntelliJ",
    icon: "🔴",
  },
];

function normalizeText(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function displayClipboardSnippet(value: string): string {
  const normalized = value.replace(/\r?\n/g, " ").trim();
  if (normalized.length <= ClipboardPreviewLength) return normalized;
  return `${normalized.slice(0, ClipboardPreviewLength)}...`;
}

function ensureValidClipboardText(value: string): { ok: true; value: string } | { ok: false; reason: string } {
  if (!value) {
    return { ok: false, reason: "Clipboard is empty." };
  }
  if (value.length > ClipboardMaxLength) {
    return {
      ok: false,
      reason: `Clipboard text is too long (${value.length} chars, max ${ClipboardMaxLength}).`,
    };
  }
  return { ok: true, value };
}

function stripWrapQuotes(value: string): string {
  return value.startsWith('"') && value.endsWith('"') ? value.slice(1, -1).trim() : value;
}

function parentOrNull(current: string): string | null {
  const parsed = path.parse(current);
  if (parsed.root === current) return null;
  const parent = path.dirname(current);
  return parent === current ? null : parent;
}

async function readClipboard(): Promise<ClipboardPayload> {
  const script = [
    "$ErrorActionPreference='SilentlyContinue'",
    "$files = Get-Clipboard -Format FileDropList",
    "if ($files -and $files.Count -gt 0) {",
    "  [pscustomobject]@{ kind='file'; value=[string]$files[0] } | ConvertTo-Json -Compress",
    "  exit 0",
    "}",
    "$text = Get-Clipboard -Raw",
    "if ($null -eq $text) { $text = '' }",
    "[pscustomobject]@{ kind='text'; value=[string]$text } | ConvertTo-Json -Compress",
  ].join("; ");

  const { stdout } = await execFileAsync("powershell", ["-NoProfile", "-NonInteractive", "-Command", script], {
    windowsHide: true,
    maxBuffer: 1024 * 1024,
  });
  const parsed = JSON.parse(stdout.trim() || "{}") as Partial<ClipboardPayload>;
  return {
    kind: parsed.kind === "file" ? "file" : "text",
    value: normalizeText(parsed.value),
  };
}

function resolveInputPath(clipboardValue: string): { ok: true; path: string } | { ok: false; reason: string } {
  const normalized = stripWrapQuotes(clipboardValue);
  const validation = ensureValidClipboardText(normalized);
  if (validation.ok === false) return { ok: false, reason: validation.reason };
  if (!path.isAbsolute(validation.value)) {
    return { ok: false, reason: "Clipboard content is not an absolute path." };
  }
  if (!fs.existsSync(validation.value)) {
    return { ok: false, reason: "Clipboard path does not exist." };
  }
  return { ok: true, path: path.resolve(validation.value) };
}

function settingText(values: Record<string, unknown> | undefined, name: string): string {
  return normalizeText(values?.[name]);
}

function resolveExecutable(settingValue: string, relativeCandidates: string[]): string | null {
  const value = stripWrapQuotes(normalizeText(settingValue));
  if (!value) return null;
  if (!fs.existsSync(value)) return null;
  const stat = fs.statSync(value);
  if (stat.isFile()) return value;
  if (!stat.isDirectory()) return null;

  for (const candidate of relativeCandidates) {
    const fullPath = path.join(value, candidate);
    if (fs.existsSync(fullPath) && fs.statSync(fullPath).isFile()) {
      return fullPath;
    }
  }
  return null;
}

function existingFileOrNull(filePath: string): string | null {
  if (!filePath) return null;
  if (!fs.existsSync(filePath)) return null;
  return fs.statSync(filePath).isFile() ? filePath : null;
}

function sortDirectoriesByMtimeDesc(paths: string[]): string[] {
  return paths
    .map((fullPath) => ({
      fullPath,
      mtime: fs.statSync(fullPath).mtimeMs,
    }))
    .sort((a, b) => b.mtime - a.mtime || b.fullPath.localeCompare(a.fullPath))
    .map((x) => x.fullPath);
}

function findLatestExecutableUnder(baseDir: string, relativeExePath: string): string | null {
  if (!baseDir || !fs.existsSync(baseDir) || !fs.statSync(baseDir).isDirectory()) {
    return null;
  }
  const subDirs = sortDirectoriesByMtimeDesc(
    fs.readdirSync(baseDir, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => path.join(baseDir, entry.name)),
  );
  for (const subDir of subDirs) {
    const candidate = path.join(subDir, relativeExePath);
    const resolved = existingFileOrNull(candidate);
    if (resolved) return resolved;
  }
  return null;
}

function envPath(name: string): string {
  return normalizeText(process.env[name]);
}

function resolveRiderExecutable(settingValue: string): ResolvedExe {
  const configured = resolveExecutable(settingValue, ["bin\\rider64.exe", "bin\\rider.exe", "rider64.exe", "rider.exe"]);
  if (configured) return { path: configured, source: "config" };

  const localAppData = envPath("LOCALAPPDATA");
  const programFiles = envPath("ProgramFiles");
  const programFilesX86 = envPath("ProgramFiles(x86)");
  const directCandidates = [
    path.join(localAppData, "Programs", "Rider", "bin", "rider64.exe"),
    path.join(localAppData, "Programs", "Rider", "bin", "rider.exe"),
    path.join(programFiles, "JetBrains", "Rider", "bin", "rider64.exe"),
    path.join(programFiles, "JetBrains", "Rider", "bin", "rider.exe"),
    path.join(programFilesX86, "JetBrains", "Rider", "bin", "rider64.exe"),
    path.join(programFilesX86, "JetBrains", "Rider", "bin", "rider.exe"),
  ];
  for (const candidate of directCandidates) {
    const resolved = existingFileOrNull(candidate);
    if (resolved) return { path: resolved, source: "auto" };
  }

  const toolboxBase = path.join(localAppData, "JetBrains", "Toolbox", "apps", "Rider", "ch-0");
  const toolboxExe = findLatestExecutableUnder(toolboxBase, "bin\\rider64.exe")
    || findLatestExecutableUnder(toolboxBase, "bin\\rider.exe");
  if (toolboxExe) return { path: toolboxExe, source: "auto" };
  return { path: null, source: "none" };
}

function resolveVsCodeExecutable(settingValue: string): ResolvedExe {
  const configured = resolveExecutable(settingValue, ["Code.exe", "bin\\code.cmd", "bin\\code"]);
  if (configured) return { path: configured, source: "config" };

  const localAppData = envPath("LOCALAPPDATA");
  const programFiles = envPath("ProgramFiles");
  const programFilesX86 = envPath("ProgramFiles(x86)");
  const directCandidates = [
    path.join(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
    path.join(programFiles, "Microsoft VS Code", "Code.exe"),
    path.join(programFilesX86, "Microsoft VS Code", "Code.exe"),
  ];
  for (const candidate of directCandidates) {
    const resolved = existingFileOrNull(candidate);
    if (resolved) return { path: resolved, source: "auto" };
  }
  return { path: null, source: "none" };
}

async function tryResolveVisualStudioFromVsWhere(): Promise<string | null> {
  const installer = path.join(envPath("ProgramFiles(x86)"), "Microsoft Visual Studio", "Installer", "vswhere.exe");
  if (!existingFileOrNull(installer)) return null;
  try {
    const { stdout } = await execFileAsync(
      installer,
      ["-latest", "-products", "*", "-property", "installationPath", "-format", "value"],
      { windowsHide: true, maxBuffer: 1024 * 1024 },
    );
    const installPath = normalizeText(stdout);
    if (!installPath) return null;
    return existingFileOrNull(path.join(installPath, "Common7", "IDE", "devenv.exe"));
  } catch {
    return null;
  }
}

async function resolveVisualStudioExecutable(settingValue: string): Promise<ResolvedExe> {
  const configured = resolveExecutable(settingValue, ["Common7\\IDE\\devenv.exe", "devenv.exe"]);
  if (configured) return { path: configured, source: "config" };

  const byVsWhere = await tryResolveVisualStudioFromVsWhere();
  if (byVsWhere) return { path: byVsWhere, source: "auto" };

  const programFiles = envPath("ProgramFiles");
  const programFilesX86 = envPath("ProgramFiles(x86)");
  const editions = ["Enterprise", "Professional", "Community", "BuildTools"];
  const versions = ["2022", "2019", "2017"];
  for (const root of [programFiles, programFilesX86]) {
    for (const version of versions) {
      for (const edition of editions) {
        const candidate = path.join(root, "Microsoft Visual Studio", version, edition, "Common7", "IDE", "devenv.exe");
        const resolved = existingFileOrNull(candidate);
        if (resolved) return { path: resolved, source: "auto" };
      }
    }
  }
  return { path: null, source: "none" };
}

function resolveIntelliJExecutable(settingValue: string): ResolvedExe {
  const configured = resolveExecutable(settingValue, ["bin\\idea64.exe", "bin\\idea.exe", "idea64.exe", "idea.exe"]);
  if (configured) return { path: configured, source: "config" };

  const localAppData = envPath("LOCALAPPDATA");
  const programFiles = envPath("ProgramFiles");
  const directCandidates = [
    path.join(localAppData, "Programs", "IntelliJ IDEA Ultimate", "bin", "idea64.exe"),
    path.join(localAppData, "Programs", "IntelliJ IDEA Community Edition", "bin", "idea64.exe"),
    path.join(programFiles, "JetBrains", "IntelliJ IDEA", "bin", "idea64.exe"),
  ];
  for (const candidate of directCandidates) {
    const resolved = existingFileOrNull(candidate);
    if (resolved) return { path: resolved, source: "auto" };
  }

  const toolboxRoot = path.join(localAppData, "JetBrains", "Toolbox", "apps");
  const toolboxIdeaU = findLatestExecutableUnder(path.join(toolboxRoot, "IDEA-U", "ch-0"), "bin\\idea64.exe");
  if (toolboxIdeaU) return { path: toolboxIdeaU, source: "auto" };
  const toolboxIdeaC = findLatestExecutableUnder(path.join(toolboxRoot, "IDEA-C", "ch-0"), "bin\\idea64.exe");
  if (toolboxIdeaC) return { path: toolboxIdeaC, source: "auto" };

  return { path: null, source: "none" };
}

function findNearestSln(startPath: string): string | null {
  const initialDir = fs.statSync(startPath).isDirectory() ? startPath : path.dirname(startPath);
  let current: string | null = initialDir;
  while (current) {
    const entries = fs.readdirSync(current, { withFileTypes: true });
    const sln = entries
      .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".sln"))
      .map((entry) => entry.name)
      .sort((a, b) => a.localeCompare(b))[0];
    if (sln) return path.join(current, sln);
    current = parentOrNull(current);
  }
  return null;
}

function selectIntelliJProject(startPath: string): string {
  const markers = [
    ".idea",
    "pom.xml",
    "build.gradle.kts",
    "build.gradle",
    "settings.gradle.kts",
    "settings.gradle",
    "package.json",
    "pyproject.toml",
    ".git",
  ];
  const initialDir = fs.statSync(startPath).isDirectory() ? startPath : path.dirname(startPath);
  let current: string | null = initialDir;
  while (current) {
    for (const marker of markers) {
      if (fs.existsSync(path.join(current, marker))) {
        return current;
      }
    }
    current = parentOrNull(current);
  }
  return initialDir;
}

function quoteArg(value: string): string {
  if (!value) return value;
  if (value.startsWith('"') && value.endsWith('"')) return value;
  return `"${value.replaceAll('"', '\\"')}"`;
}

function hostExecute(filePath: string, argument: string, message: string) {
  return {
    message: { key: "Plugin.OpenPath.Action.Open.Success", defaultValue: message },
    close: true,
    host: {
      kind: HostAction.Execute,
      path: filePath,
      args: quoteArg(argument),
    },
  };
}

function invalidClipboardMessage(reason: string, rawClipboard: string): string {
  return `${reason} Clipboard: "${displayClipboardSnippet(rawClipboard)}"`;
}

function itemMatches(item: { title: string; subtitle: string }, query: string): boolean {
  if (!query) return true;
  const q = query.toLowerCase();
  return item.title.toLowerCase().includes(q) || item.subtitle.toLowerCase().includes(q);
}

function fail(message: string): never {
  console.log(`[openpath] fail: ${message}`);
  throw new Error(message);
}

function log(message: string): void {
  console.log(`[openpath] ${message}`);
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search((params) => {
    const query = normalizeText(params.query).toLowerCase();
    const items = IdeItems.filter((item) => itemMatches(item, query)).map((item) => ({
      id: `openpath:${item.id}`,
      title: item.title,
      subtitle: item.subtitle,
      priority: 100,
      icon: { kind: "emoji", value: item.icon },
      ide: item.id,
      actions: ["open"],
    }));
    return { items };
  })
  .actions<{ ide: IdeKey }>([{
    id: "open",
    title: { key: "Plugin.OpenPath.Action.Open", defaultValue: "Open" },
    description: { key: "Plugin.OpenPath.Action.OpenDescription", defaultValue: "Open target IDE" },
    execute: async ({ item, itemId }) => {
    const ide = item?.ide;
    log(`invokeAction itemId=${itemId} ide=${ide}`);
    if (!IdeItems.some((item) => item.id === ide)) {
      fail("Unsupported IDE action.");
    }

    const clipboard = await readClipboard();
    log(`clipboard kind=${clipboard.kind} len=${clipboard.value.length} snippet="${displayClipboardSnippet(clipboard.value)}"`);
    const pathResult = resolveInputPath(clipboard.value);
    if (pathResult.ok === false) {
      fail(invalidClipboardMessage(pathResult.reason, clipboard.value));
    }
    log(`resolvedPath=${pathResult.path}`);

    const config = (await plugin.hostCall("configuration.readOwn")) as OwnConfiguration;
    const settings = config?.values ?? {};
    const riderPath = settingText(settings, SettingNames.rider);
    const vscodePath = settingText(settings, SettingNames.vscode);
    const visualStudioPath = settingText(settings, SettingNames.visualStudio);
    const intellijPath = settingText(settings, SettingNames.intellij);
    log(
      `settingPaths rider="${riderPath}" vscode="${vscodePath}" vs="${visualStudioPath}" intellij="${intellijPath}"`,
    );
    const riderExe = resolveRiderExecutable(riderPath);
    const vscodeExe = resolveVsCodeExecutable(vscodePath);
    const vsExe = await resolveVisualStudioExecutable(visualStudioPath);
    const intellijExe = resolveIntelliJExecutable(intellijPath);
    log(
      `resolvedExe rider="${riderExe.path || ""}"(${riderExe.source}) vscode="${vscodeExe.path || ""}"(${vscodeExe.source}) vs="${vsExe.path || ""}"(${vsExe.source}) intellij="${intellijExe.path || ""}"(${intellijExe.source})`,
    );

    if (ide === "rider") {
      if (!riderExe.path) {
        fail(`Rider executable not found. Please set ${SettingNames.rider} in Settings.`);
      }
      const sln = findNearestSln(pathResult.path);
      log(`rider nearestSln="${sln || ""}"`);
      if (!sln) {
        fail("No .sln found in current path or parent paths.");
      }
      log(`launch rider exe="${riderExe.path}" arg="${sln}"`);
      return hostExecute(riderExe.path, sln, `Opened Rider with ${sln}`);
    }

    if (ide === "visual-studio") {
      if (!vsExe.path) {
        fail(`Visual Studio executable not found. Please set ${SettingNames.visualStudio} in Settings.`);
      }
      const sln = findNearestSln(pathResult.path);
      log(`visual-studio nearestSln="${sln || ""}"`);
      if (!sln) {
        fail("No .sln found in current path or parent paths.");
      }
      log(`launch visual-studio exe="${vsExe.path}" arg="${sln}"`);
      return hostExecute(vsExe.path, sln, `Opened Visual Studio with ${sln}`);
    }

    if (ide === "vscode") {
      if (!vscodeExe.path) {
        fail(`VSCode executable not found. Please set ${SettingNames.vscode} in Settings.`);
      }
      const stat = fs.statSync(pathResult.path);
      const openPath = stat.isDirectory() ? pathResult.path : path.dirname(pathResult.path);
      log(`launch vscode exe="${vscodeExe.path}" arg="${openPath}"`);
      return hostExecute(vscodeExe.path, openPath, `Opened VSCode with ${openPath}`);
    }

    if (!intellijExe.path) {
      fail(`IntelliJ executable not found. Please set ${SettingNames.intellij} in Settings.`);
    }
    const projectPath = selectIntelliJProject(pathResult.path);
    log(`launch intellij exe="${intellijExe.path}" arg="${projectPath}"`);
    return hostExecute(intellijExe.path, projectPath, `Opened IntelliJ with ${projectPath}`);
    },
  }])
  .start();
