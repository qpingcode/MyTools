import {ResponseBodyFormat} from '../workspace/workspaceTypes.js';

export enum SyntaxTokenKind {
    Plain = 'plain',
    Property = 'property',
    String = 'string',
    Number = 'number',
    Literal = 'literal',
    Keyword = 'keyword',
    Tag = 'tag',
    Attribute = 'attribute',
    Comment = 'comment',
    Punctuation = 'punctuation',
}

export interface SyntaxToken {
    text: string;
    kind: SyntaxTokenKind;
}

const JsonTokenPattern = /"(?:\\.|[^"\\])*"(?=\s*:)|"(?:\\.|[^"\\])*"|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?|\b(?:true|false|null)\b/g;
const MarkupTokenPattern = /<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<\?[\s\S]*?\?>|<!doctype[^>]*>|<\/?[A-Za-z_:][^>]*>/gi;
const MarkupNamePattern = /[A-Za-z_:][\w:.-]*/y;
const JavaScriptIdentifierPattern = /[A-Za-z_$][\w$]*/y;
const JavaScriptNumberPattern = /(?:0[xX][\dA-Fa-f]+|0[bB][01]+|0[oO][0-7]+|(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)[n]?/y;
const JavaScriptOperatorPattern = /(?:===|!==|>>>|<<=|>>=|\*\*|=>|==|!=|<=|>=|\+\+|--|&&|\|\||\?\?|\?\.|\+=|-=|\*=|\/=|%=|&=|\|=|\^=|<<|>>|[{}()[\].,;:?~+\-*\/%<>=!&|^])/y;
const JavaScriptKeywords = new Set([
    'as', 'async', 'await', 'break', 'case', 'catch', 'class', 'const', 'continue', 'debugger',
    'default', 'delete', 'do', 'else', 'export', 'extends', 'finally', 'for', 'from', 'function',
    'get', 'if', 'import', 'in', 'instanceof', 'let', 'new', 'of', 'return', 'set', 'static',
    'super', 'switch', 'this', 'throw', 'try', 'typeof', 'var', 'void', 'while', 'with', 'yield',
]);
const JavaScriptLiterals = new Set(['true', 'false', 'null', 'undefined', 'NaN', 'Infinity']);

function pushPlain(tokens: SyntaxToken[], text: string) {
    if (!text) return;
    const previous = tokens.at(-1);
    if (previous?.kind === SyntaxTokenKind.Plain) previous.text += text;
    else tokens.push({text, kind: SyntaxTokenKind.Plain});
}

function jsonTokens(source: string): SyntaxToken[] {
    const tokens: SyntaxToken[] = [];
    let from = 0;
    for (const match of source.matchAll(JsonTokenPattern)) {
        const index = match.index;
        pushPlain(tokens, source.slice(from, index));
        const text = match[0];
        const after = source.slice(index + text.length);
        const kind = text.startsWith('"')
            ? /^\s*:/.test(after) ? SyntaxTokenKind.Property : SyntaxTokenKind.String
            : /^(?:true|false|null)$/.test(text) ? SyntaxTokenKind.Literal : SyntaxTokenKind.Number;
        tokens.push({text, kind});
        from = index + text.length;
    }
    pushPlain(tokens, source.slice(from));
    return tokens;
}

function regularMarkupTagTokens(source: string): SyntaxToken[] {
    const tokens: SyntaxToken[] = [];
    const opening = /^<\/?/.exec(source)?.[0] ?? '<';
    tokens.push({text: opening, kind: SyntaxTokenKind.Punctuation});
    let index = opening.length;
    MarkupNamePattern.lastIndex = index;
    const tagName = MarkupNamePattern.exec(source);
    if (tagName) {
        tokens.push({text: tagName[0], kind: SyntaxTokenKind.Tag});
        index = MarkupNamePattern.lastIndex;
    }
    let valueExpected = false;
    while (index < source.length) {
        const rest = source.slice(index);
        const whitespace = /^\s+/.exec(rest)?.[0];
        if (whitespace) {
            pushPlain(tokens, whitespace);
            index += whitespace.length;
            continue;
        }
        const ending = /^\/?>/.exec(rest)?.[0];
        if (ending) {
            tokens.push({text: ending, kind: SyntaxTokenKind.Punctuation});
            index += ending.length;
            continue;
        }
        if (rest[0] === '=') {
            tokens.push({text: '=', kind: SyntaxTokenKind.Punctuation});
            valueExpected = true;
            index++;
            continue;
        }
        const quoted = /^(?:"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')/.exec(rest)?.[0];
        if (quoted) {
            tokens.push({text: quoted, kind: SyntaxTokenKind.String});
            valueExpected = false;
            index += quoted.length;
            continue;
        }
        const word = /^[^\s=/>]+/.exec(rest)?.[0];
        if (word) {
            tokens.push({text: word, kind: valueExpected ? SyntaxTokenKind.String : SyntaxTokenKind.Attribute});
            valueExpected = false;
            index += word.length;
            continue;
        }
        pushPlain(tokens, rest[0]);
        index++;
    }
    return tokens;
}

function markupTokens(source: string): SyntaxToken[] {
    const tokens: SyntaxToken[] = [];
    let from = 0;
    for (const match of source.matchAll(MarkupTokenPattern)) {
        const index = match.index;
        pushPlain(tokens, source.slice(from, index));
        const text = match[0];
        if (/^<!--|^<!\[CDATA|^<\?|^<!doctype/i.test(text)) {
            tokens.push({text, kind: SyntaxTokenKind.Comment});
        } else {
            tokens.push(...regularMarkupTagTokens(text));
        }
        from = index + text.length;
    }
    pushPlain(tokens, source.slice(from));
    return tokens;
}

function quotedJavaScriptToken(source: string, from: number): number {
    const quote = source[from];
    for (let index = from + 1; index < source.length; index++) {
        if (source[index] === '\\') {
            index++;
            continue;
        }
        if (source[index] === quote) return index + 1;
        if (quote !== '`' && (source[index] === '\n' || source[index] === '\r')) return index;
    }
    return source.length;
}

function javaScriptTokens(source: string): SyntaxToken[] {
    const tokens: SyntaxToken[] = [];
    let index = 0;
    while (index < source.length) {
        if (index === 0 && source.startsWith('#!')) {
            const end = source.indexOf('\n', index);
            const tokenEnd = end < 0 ? source.length : end;
            tokens.push({text: source.slice(index, tokenEnd), kind: SyntaxTokenKind.Comment});
            index = tokenEnd;
            continue;
        }
        if (source.startsWith('//', index)) {
            const end = source.indexOf('\n', index);
            const tokenEnd = end < 0 ? source.length : end;
            tokens.push({text: source.slice(index, tokenEnd), kind: SyntaxTokenKind.Comment});
            index = tokenEnd;
            continue;
        }
        if (source.startsWith('/*', index)) {
            const end = source.indexOf('*/', index + 2);
            const tokenEnd = end < 0 ? source.length : end + 2;
            tokens.push({text: source.slice(index, tokenEnd), kind: SyntaxTokenKind.Comment});
            index = tokenEnd;
            continue;
        }
        if (source[index] === '"' || source[index] === "'" || source[index] === '`') {
            const end = quotedJavaScriptToken(source, index);
            tokens.push({text: source.slice(index, end), kind: SyntaxTokenKind.String});
            index = end;
            continue;
        }
        JavaScriptNumberPattern.lastIndex = index;
        const number = JavaScriptNumberPattern.exec(source);
        if (number) {
            tokens.push({text: number[0], kind: SyntaxTokenKind.Number});
            index = JavaScriptNumberPattern.lastIndex;
            continue;
        }
        JavaScriptIdentifierPattern.lastIndex = index;
        const identifier = JavaScriptIdentifierPattern.exec(source);
        if (identifier) {
            const text = identifier[0];
            const kind = JavaScriptKeywords.has(text)
                ? SyntaxTokenKind.Keyword
                : JavaScriptLiterals.has(text) ? SyntaxTokenKind.Literal : SyntaxTokenKind.Plain;
            if (kind === SyntaxTokenKind.Plain) pushPlain(tokens, text);
            else tokens.push({text, kind});
            index = JavaScriptIdentifierPattern.lastIndex;
            continue;
        }
        JavaScriptOperatorPattern.lastIndex = index;
        const operator = JavaScriptOperatorPattern.exec(source);
        if (operator) {
            tokens.push({text: operator[0], kind: SyntaxTokenKind.Punctuation});
            index = JavaScriptOperatorPattern.lastIndex;
            continue;
        }
        pushPlain(tokens, source[index]);
        index++;
    }
    return tokens;
}

export function syntaxTokens(source: string, format: ResponseBodyFormat): SyntaxToken[] {
    if (format === ResponseBodyFormat.Json) return jsonTokens(source);
    if (format === ResponseBodyFormat.Xml || format === ResponseBodyFormat.Html) return markupTokens(source);
    if (format === ResponseBodyFormat.JavaScript) return javaScriptTokens(source);
    return [{text: source, kind: SyntaxTokenKind.Plain}];
}
