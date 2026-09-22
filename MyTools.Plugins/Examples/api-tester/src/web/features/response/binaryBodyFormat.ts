const HexBytesPerRow = 16;
const HexByteWidth = 2;
const HexOffsetWidth = 8;
const Base64CharactersPerRow = 76;
const PrintableAsciiStart = 32;
const PrintableAsciiEnd = 126;
const Base64EncodingChunkBytes = 32 * 1024;

export function encodeBase64(bytes: Uint8Array): string {
    let binary = '';
    for (let offset = 0; offset < bytes.length; offset += Base64EncodingChunkBytes) {
        binary += String.fromCharCode(...bytes.subarray(offset, offset + Base64EncodingChunkBytes));
    }
    return btoa(binary);
}

export function decodeBase64(source: string): Uint8Array<ArrayBuffer> {
    return Uint8Array.from(atob(source), character => character.charCodeAt(0));
}

export function formatBase64(source: string): string {
    const rows = source.match(new RegExp(`.{1,${Base64CharactersPerRow}}`, 'g'));
    return rows?.join('\n') ?? '';
}

export function formatHex(source: string): string {
    const bytes = decodeBase64(source);
    const rows: string[] = [];
    for (let offset = 0; offset < bytes.length; offset += HexBytesPerRow) {
        const row = bytes.subarray(offset, offset + HexBytesPerRow);
        const hexadecimal = Array.from(row, byte => byte.toString(16).padStart(HexByteWidth, '0'))
            .join(' ')
            .padEnd(HexBytesPerRow * (HexByteWidth + 1) - 1);
        const ascii = Array.from(row, byte =>
            byte >= PrintableAsciiStart && byte <= PrintableAsciiEnd ? String.fromCharCode(byte) : '.',
        ).join('');
        rows.push(`${offset.toString(16).padStart(HexOffsetWidth, '0')}  ${hexadecimal}  |${ascii}|`);
    }
    return rows.join('\n');
}
