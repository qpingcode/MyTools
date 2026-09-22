import {HttpHeader, type RequestResult} from '../../../shared/model.js';
import {ResponseBodyFormat} from '../workspace/workspaceTypes.js';

const JsonMediaTypePattern = /(?:^|\/)json$|\+json$/i;
const XmlMediaTypePattern = /(?:^|\/)xml$|\+xml$/i;
const HtmlMediaTypes = new Set(['text/html', 'application/xhtml+xml']);
const JavaScriptMediaTypes = new Set([
    'text/javascript',
    'application/javascript',
    'text/ecmascript',
    'application/ecmascript',
    'application/x-javascript',
]);
const HtmlDocumentPattern = /^\s*(?:<!doctype\s+html\b|<html\b|<head\b|<body\b)/i;
const JavaScriptSourcePattern = /^\s*(?:#!.*\bnode\b|['"]use strict['"]\s*;?|(?:import|export|const|let|var|function|class|async\s+function)\b)/;
const XmlDeclarationPattern = /^\s*(<\?xml[^?]*\?>)/i;
const HtmlDocumentType = '<!doctype html>';
const MarkupIndent = '  ';
const PreviewContentSecurityPolicy = "default-src 'none'; img-src data: blob:; style-src 'unsafe-inline'; font-src data:; form-action 'none'; object-src 'none'; base-uri 'none'";

function mediaType(result: RequestResult): string {
    const value = result.headers.find(header => header.name.toLowerCase() === HttpHeader.ContentType)?.value ?? '';
    return value.split(';', 1)[0].trim().toLowerCase();
}

function isJson(source: string): boolean {
    try {
        JSON.parse(source);
        return true;
    } catch {
        return false;
    }
}

function isXml(source: string): boolean {
    if (!source.trimStart().startsWith('<')) return false;
    const document = new DOMParser().parseFromString(source, 'application/xml');
    return document.getElementsByTagName('parsererror').length === 0;
}

export function inferResponseBodyFormat(result: RequestResult): ResponseBodyFormat {
    const type = mediaType(result);
    if (JsonMediaTypePattern.test(type)) return ResponseBodyFormat.Json;
    if (HtmlMediaTypes.has(type)) return ResponseBodyFormat.Html;
    if (XmlMediaTypePattern.test(type)) return ResponseBodyFormat.Xml;
    if (JavaScriptMediaTypes.has(type)) return ResponseBodyFormat.JavaScript;
    if (result.binary || result.previewAvailable === false || result.truncated) return ResponseBodyFormat.Raw;
    if (isJson(result.preview)) return ResponseBodyFormat.Json;
    if (HtmlDocumentPattern.test(result.preview)) return ResponseBodyFormat.Html;
    if (JavaScriptSourcePattern.test(result.preview)) return ResponseBodyFormat.JavaScript;
    if (isXml(result.preview)) {
        const root = new DOMParser().parseFromString(result.preview, 'application/xml').documentElement;
        return root.localName.toLowerCase() === 'html' ? ResponseBodyFormat.Html : ResponseBodyFormat.Xml;
    }
    return ResponseBodyFormat.Raw;
}

export function parseJson(source: string): unknown | undefined {
    try {
        return JSON.parse(source);
    } catch {
        return undefined;
    }
}

function indentation(depth: number): string {
    return MarkupIndent.repeat(depth);
}

function serializeMarkupNode(node: Node, depth: number): string {
    const serializer = new XMLSerializer();
    if (node.nodeType !== Node.ELEMENT_NODE) return indentation(depth) + serializer.serializeToString(node).trim();
    const element = node as Element;
    const children = Array.from(element.childNodes);
    if (!children.length) return indentation(depth) + serializer.serializeToString(element);
    const hasElementChild = children.some(child => child.nodeType === Node.ELEMENT_NODE);
    const hasSignificantText = children.some(child =>
        (child.nodeType === Node.TEXT_NODE || child.nodeType === Node.CDATA_SECTION_NODE)
        && Boolean(child.textContent?.trim()),
    );
    if (!hasElementChild || hasSignificantText) return indentation(depth) + serializer.serializeToString(element);

    const serialized = serializer.serializeToString(element);
    const openingEnd = serialized.indexOf('>');
    const closingStart = serialized.lastIndexOf('</');
    if (openingEnd < 0 || closingStart <= openingEnd) return indentation(depth) + serialized;
    const opening = serialized.slice(0, openingEnd + 1);
    const closing = serialized.slice(closingStart);
    const body = children
        .filter(child => child.nodeType !== Node.TEXT_NODE || Boolean(child.textContent?.trim()))
        .map(child => serializeMarkupNode(child, depth + 1))
        .join('\n');
    return `${indentation(depth)}${opening}\n${body}\n${indentation(depth)}${closing}`;
}

export function formatXml(source: string): string | undefined {
    const document = new DOMParser().parseFromString(source, 'application/xml');
    if (document.getElementsByTagName('parsererror').length) return undefined;
    const declaration = XmlDeclarationPattern.exec(source)?.[1];
    const body = Array.from(document.childNodes)
        .filter(node => node.nodeType !== Node.PROCESSING_INSTRUCTION_NODE || !declaration)
        .map(node => serializeMarkupNode(node, 0))
        .filter(Boolean)
        .join('\n');
    return declaration ? `${declaration}\n${body}` : body;
}

export function formatHtml(source: string): string {
    const document = new DOMParser().parseFromString(source, 'text/html');
    return `${HtmlDocumentType}\n${serializeMarkupNode(document.documentElement, 0)}`;
}

export function createHtmlPreviewDocument(source: string): string {
    const document = new DOMParser().parseFromString(source, 'text/html');
    document.querySelectorAll('meta[http-equiv="Content-Security-Policy" i], meta[http-equiv="refresh" i], base')
        .forEach(element => element.remove());
    const policy = document.createElement('meta');
    policy.httpEquiv = 'Content-Security-Policy';
    policy.content = PreviewContentSecurityPolicy;
    document.head.prepend(policy);
    return `${HtmlDocumentType}\n${document.documentElement.outerHTML}`;
}
