import * as prettier from "prettier/standalone";
import * as babelPlugin from "prettier/plugins/babel";
import * as estreePlugin from "prettier/plugins/estree";
import * as typescriptPlugin from "prettier/plugins/typescript";
import * as htmlPlugin from "prettier/plugins/html";
import * as postcssPlugin from "prettier/plugins/postcss";
import * as yamlPlugin from "prettier/plugins/yaml";
import xmlPlugin from "@prettier/plugin-xml";
import {detectLanguage, type LanguageId} from "./language.js";

const FormatterIndentWidth = 2;
const plugins = [
  babelPlugin,
  estreePlugin,
  typescriptPlugin,
  htmlPlugin,
  postcssPlugin,
  yamlPlugin,
  xmlPlugin,
];

const parsers: Record<LanguageId, string> = {
  javascript: "babel",
  typescript: "typescript",
  html: "html",
  css: "css",
  json: "json",
  yaml: "yaml",
  xml: "xml",
};

export async function formatSource(source: string, language: LanguageId): Promise<string> {
  return prettier.format(source, {
    parser: parsers[language],
    plugins,
    tabWidth: FormatterIndentWidth,
    useTabs: false,
    endOfLine: "lf",
    ...(language === "xml" ? {xmlWhitespaceSensitivity: "preserve"} : {}),
  });
}

export async function tryFormatSource(source: string, language?: LanguageId): Promise<string> {
  const detected = language ?? detectLanguage(source);
  if (!detected) return source;
  try {
    return await formatSource(source, detected);
  } catch {
    return source;
  }
}
