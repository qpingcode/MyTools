export const languageIds = ["javascript", "typescript", "html", "css", "json", "yaml", "xml"] as const;

export type LanguageId = typeof languageIds[number];
export type LanguageSelection = "auto" | LanguageId;

export function isLanguageId(value: unknown): value is LanguageId {
  return typeof value === "string" && (languageIds as readonly string[]).includes(value);
}

function looksLikeHtml(source: string): boolean {
  return /<!doctype\s+html\b/i.test(source)
    || /<(?:html|head|body|script|style|main|header|footer|nav|section|article|div|span|p|a|img|input|button|form|label|ul|ol|li|table|tr|td|th)\b/i.test(source);
}

function looksLikeHtmlDocument(source: string): boolean {
  return /<!doctype\s+html\b|<html\b|<head\b|<body\b/i.test(source);
}

function looksLikeXml(source: string): boolean {
  if (/^\s*<\?xml\b/i.test(source)) return true;
  if (/\sxmlns(?::[\w.-]+)?\s*=/.test(source)) return true;
  if (/^\s*<[^!?/][^>]*\/\s*>\s*$/.test(source)) return !looksLikeHtml(source);
  if (!/^\s*<[^!?/][^>]*>/.test(source) || !/<\/[^>]+>\s*$/.test(source)) return false;
  return !looksLikeHtml(source);
}

function looksLikeTypeScript(source: string): boolean {
  return /\b(?:interface|namespace|enum|abstract|implements|declare)\s+[A-Za-z_$]/.test(source)
    || /\btype\s+[A-Za-z_$][\w$]*(?:\s*<[^>]+>)?\s*=/.test(source)
    || /\b(?:as\s+const|satisfies\s+[A-Za-z_$]|readonly\s+[A-Za-z_$])\b/.test(source)
    || /(?:const|let|var)\s+[A-Za-z_$][\w$]*\s*:\s*[A-Za-z_$<{[(]/.test(source)
    || /(?:function\s+[A-Za-z_$][\w$]*|\([^)]*\))\s*\([^)]*\)\s*:\s*[A-Za-z_$<{[(]/.test(source);
}

function looksLikeCss(source: string): boolean {
  if (/^\s*@(?:media|supports|keyframes|font-face|import|layer)\b/m.test(source)) return true;
  const blocks = source.match(/[^{}]+\{[^{}]*\}/g) ?? [];
  return blocks.length > 0 && blocks.some(block => {
    const body = block.slice(block.indexOf("{") + 1, block.lastIndexOf("}"));
    return /(?:^|;)\s*(?:--)?[\w-]+\s*:\s*[^;{}]+/m.test(body);
  });
}

function looksLikeYaml(source: string): boolean {
  if (/^\s*---\s*$/m.test(source) || /^\s*\.\.\.\s*$/m.test(source)) return true;
  const meaningful = source.split(/\r?\n/).filter(line => line.trim() && !/^\s*#/.test(line));
  if (meaningful.length < 2) return false;
  const yamlLines = meaningful.filter(line => /^\s*(?:-\s+)?[\w."'][^:{}\[\]]*:\s*(?:.*)?$/.test(line) || /^\s*-\s+\S/.test(line));
  return yamlLines.length >= Math.ceil(meaningful.length * 0.6) && !/[;{}]/.test(source);
}

function looksLikeJavaScript(source: string): boolean {
  return /\b(?:const|let|var|function|class|import|export|return|async|await|new|throw)\b/.test(source)
    || /=>|\bconsole\.[A-Za-z_$]+\s*\(/.test(source);
}

export function detectLanguage(source: string): LanguageId | null {
  const trimmed = source.trim();
  if (!trimmed) return null;

  try {
    JSON.parse(trimmed);
    return "json";
  } catch {
    // Continue with structural detection.
  }

  if (/^\s*<\?xml\b/i.test(trimmed) || /\sxmlns(?::[\w.-]+)?\s*=/.test(trimmed)) return "xml";
  if (looksLikeHtmlDocument(trimmed)) return "html";
  if (looksLikeTypeScript(trimmed)) return "typescript";
  if (looksLikeJavaScript(trimmed)) return "javascript";
  if (looksLikeCss(trimmed)) return "css";
  if (looksLikeHtml(trimmed)) return "html";
  if (looksLikeXml(trimmed)) return "xml";
  if (looksLikeYaml(trimmed)) return "yaml";
  return null;
}
