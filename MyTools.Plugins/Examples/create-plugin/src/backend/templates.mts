import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import Mustache from "mustache";

export type TemplateView = Record<string, unknown>;

const templatesRoot = fileURLToPath(new URL("../templates/", import.meta.url));
const templateSuffix = ".mustache";

function renderTree(tree: string, view: TemplateView): Record<string, string> {
  const root = path.join(templatesRoot, tree);
  const files: Record<string, string> = {};

  for (const entry of readdirSync(root, { recursive: true, withFileTypes: true })) {
    if (!entry.isFile() || !entry.name.endsWith(templateSuffix)) continue;

    const templatePath = path.join(entry.parentPath, entry.name);
    const relativePath = path.relative(root, templatePath)
      .slice(0, -templateSuffix.length)
      .replaceAll(path.sep, "/");
    files[relativePath] = Mustache.render(readFileSync(templatePath, "utf8"), view);
  }

  return files;
}

export function renderTemplateTrees(trees: string[], view: TemplateView): Record<string, string> {
  return Object.assign({}, ...trees.map((tree) => renderTree(tree, view)));
}
