// Pure JavaScript helpers can run inside the VM without exposing Node crypto objects.
export function scriptCrypto() {
    const Sha256InitialState = [0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19];
    const Sha256RoundConstants = [0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2];
    const WordBits = 32;
    const ByteBits = 8;
    const WordBytes = 4;
    const Sha256BlockBytes = 64;
    const LengthFieldBytes = 8;
    const MessageWords = 16;
    const HmacInnerPad = 0x36;
    const HmacOuterPad = 0x5c;
    const PaddingMarker = 0x80;
    const ByteMask = 0xff;
    const Base64Alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
    const HexRadix = 16;
    const HexByteDigits = 2;
    const Base64GroupBytes = 3;
    const Base64SextetBits = 6;
    const Base64SextetMask = 63;
    const utf8 = (text: string) => Array.from(encodeURIComponent(String(text)).replace(/%([0-9A-F]{2})/g, (_: string, hex: string) => String.fromCharCode(parseInt(hex, HexRadix))), char => char.charCodeAt(0));
    const rotate = (word: number, count: number) => word >>> count | word << (WordBits - count);

    function hash(data: number[]): number[] {
        const padded = [...data, PaddingMarker];
        while (padded.length % Sha256BlockBytes !== Sha256BlockBytes - LengthFieldBytes) padded.push(0);
        const bitLength = data.length * ByteBits;
        for (let shift = LengthFieldBytes - 1; shift >= 0; shift--) padded.push(Math.floor(bitLength / 2 ** (shift * ByteBits)) & ByteMask);
        const state = [...Sha256InitialState];
        for (let offset = 0; offset < padded.length; offset += Sha256BlockBytes) {
            const words: number[] = [];
            for (let i = 0; i < MessageWords; i++) {
                let word = 0;
                for (let j = 0; j < WordBytes; j++) word = word << ByteBits | padded[offset + i * WordBytes + j];
                words.push(word);
            }
            for (let i = MessageWords; i < Sha256RoundConstants.length; i++) {
                const x = words[i - 15];
                const y = words[i - 2];
                // SHA-256 message schedule sigma rotations, as defined by FIPS 180-4.
                const s0 = rotate(x, 7) ^ rotate(x, 18) ^ x >>> 3;
                const s1 = rotate(y, 17) ^ rotate(y, 19) ^ y >>> 10;
                words.push((words[i - MessageWords] + s0 + words[i - 7] + s1) | 0);
            }
            let [a, b, c, d, e, f, g, h] = state;
            for (let i = 0; i < Sha256RoundConstants.length; i++) {
                const s1 = rotate(e, 6) ^ rotate(e, 11) ^ rotate(e, 25);
                const choose = e & f ^ ~e & g;
                const t1 = (h + s1 + choose + Sha256RoundConstants[i] + words[i]) | 0;
                const s0 = rotate(a, 2) ^ rotate(a, 13) ^ rotate(a, 22);
                const majority = a & b ^ a & c ^ b & c;
                h = g;
                g = f;
                f = e;
                e = (d + t1) | 0;
                d = c;
                c = b;
                b = a;
                a = (t1 + s0 + majority) | 0;
            }
            [a, b, c, d, e, f, g, h].forEach((word, i) => {
                state[i] = (state[i] + word) | 0;
            });
        }
        return state.flatMap(word => Array.from({length: WordBytes}, (_, i) => word >>> ((WordBytes - 1 - i) * ByteBits) & ByteMask));
    }

    const hex = (bytes: number[]) => bytes.map(byte => byte.toString(HexRadix).padStart(HexByteDigits, '0')).join('');
    return {
        sha256: (text: string) => hex(hash(utf8(text))),
        hmacSha256: (text: string, secret: string) => {
            let key = utf8(secret);
            if (key.length > Sha256BlockBytes) key = hash(key);
            while (key.length < Sha256BlockBytes) key.push(0);
            return hex(hash([...key.map(byte => byte ^ HmacOuterPad), ...hash([...key.map(byte => byte ^ HmacInnerPad), ...utf8(text)])]));
        },
        base64: (text: string) => {
            const bytes = utf8(text);
            let result = '';
            for (let i = 0; i < bytes.length; i += Base64GroupBytes) {
                const word = bytes[i] << (2 * ByteBits) | (bytes[i + 1] || 0) << ByteBits | (bytes[i + 2] || 0);
                result += Base64Alphabet[word >>> (3 * Base64SextetBits)] + Base64Alphabet[word >>> (2 * Base64SextetBits) & Base64SextetMask] + (i + 1 < bytes.length ? Base64Alphabet[word >>> Base64SextetBits & Base64SextetMask] : '=') + (i + 2 < bytes.length ? Base64Alphabet[word & Base64SextetMask] : '=');
            }
            return result;
        },
    };
}
