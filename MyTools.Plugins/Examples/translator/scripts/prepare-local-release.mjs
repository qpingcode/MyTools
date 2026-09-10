import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { pipeline } from "node:stream/promises";
import { fileURLToPath } from "node:url";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const sourceRoot = path.resolve(projectRoot, process.argv[2] || "local-package");
const tag = process.argv[3] || "translator-local-v1";
const outputRoot = path.join(projectRoot, "release-upload");
const repository = process.env.MYTOOLS_GITHUB_REPOSITORY || "qpingcode/MyTools";
const requiredFiles = [
  "llama/llama-server.exe",
  "models/Hy-MT2-1.8B-Q4_K_M.gguf",
  "dictionaries/ecdict.db",
];

for (const relativePath of requiredFiles) {
  if (!fs.existsSync(path.join(sourceRoot, relativePath))) {
    throw new Error(`Missing required file: ${relativePath}`);
  }
}

fs.rmSync(outputRoot, { recursive: true, force: true });
fs.mkdirSync(outputRoot, { recursive: true });

function listFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const fullPath = path.join(directory, entry.name);
    return entry.isDirectory() ? listFiles(fullPath) : [fullPath];
  });
}

async function sha256(filePath) {
  const hash = createHash("sha256");
  await pipeline(fs.createReadStream(filePath), hash);
  return hash.digest("hex");
}

const files = [];
for (const sourcePath of listFiles(sourceRoot)) {
  const relativePath = path.relative(sourceRoot, sourcePath).replaceAll("\\", "/");
  const assetName = relativePath.replaceAll("/", "--");
  const outputPath = path.join(outputRoot, assetName);
  try {
    fs.linkSync(sourcePath, outputPath);
  } catch {
    fs.copyFileSync(sourcePath, outputPath);
  }
  const size = fs.statSync(sourcePath).size;
  files.push({
    path: relativePath,
    url: `https://github.com/${repository}/releases/download/${tag}/${encodeURIComponent(assetName)}`,
    size,
    sha256: await sha256(sourcePath),
  });
}

const manifest = {
  version: tag,
  totalSize: files.reduce((sum, file) => sum + file.size, 0),
  files,
};
fs.writeFileSync(path.join(outputRoot, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
console.log(`Prepared ${files.length} files (${manifest.totalSize} bytes) in ${outputRoot}`);
console.log(`Publish with: gh release create ${tag} release-upload/* --repo ${repository}`);
