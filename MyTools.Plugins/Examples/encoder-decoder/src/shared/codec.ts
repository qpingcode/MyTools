import { createHash } from "node:crypto";

export type CodecMode = "encode" | "decode";
export type EncodingAlgorithm = "base64" | "base64url" | "url" | "hex";
export type HashAlgorithm = "md5" | "sha1" | "sha256" | "sha512";
export type CodecAlgorithm = EncodingAlgorithm | HashAlgorithm;
export type HashOutputFormat = "hex-lower" | "hex-upper" | "base64";

export const encodingAlgorithms: EncodingAlgorithm[] = ["base64", "base64url", "url", "hex"];
export const hashAlgorithms: HashAlgorithm[] = ["md5", "sha1", "sha256", "sha512"];
export const codecAlgorithms: CodecAlgorithm[] = [...encodingAlgorithms, ...hashAlgorithms];

export type TransformResult = {
  output: string;
  binary: boolean;
};

export class CodecError extends Error {
  constructor(public readonly code: string) {
    super(code);
    this.name = "CodecError";
  }
}

function decodeBase64(value: string, urlSafe: boolean): Buffer {
  let normalized = value.replace(/\s/g, "");
  const alphabet = urlSafe ? /^[A-Za-z0-9_-]*={0,2}$/ : /^[A-Za-z0-9+/]*={0,2}$/;
  if (!alphabet.test(normalized)) throw new CodecError("invalid-base64-characters");
  if (normalized.includes("=") && normalized.length % 4 !== 0) throw new CodecError("invalid-base64-padding");
  const unpadded = normalized.replace(/=+$/, "");
  if (unpadded.length % 4 === 1) throw new CodecError("invalid-base64-length");
  if (urlSafe) normalized = unpadded.replace(/-/g, "+").replace(/_/g, "/");
  else normalized = unpadded;
  normalized += "=".repeat((4 - (normalized.length % 4)) % 4);
  return Buffer.from(normalized, "base64");
}

function displayDecoded(bytes: Buffer): TransformResult {
  try {
    return { output: new TextDecoder("utf-8", { fatal: true }).decode(bytes), binary: false };
  } catch {
    return { output: bytes.toString("hex"), binary: true };
  }
}

export function formatDigest(bytes: Uint8Array, format: HashOutputFormat): string {
  const buffer = Buffer.from(bytes);
  if (format === "base64") return buffer.toString("base64");
  const hex = buffer.toString("hex");
  return format === "hex-upper" ? hex.toUpperCase() : hex;
}

export function hashText(input: string, algorithm: HashAlgorithm, format: HashOutputFormat): string {
  const digest = createHash(algorithm).update(input, "utf8").digest();
  return formatDigest(digest, format);
}

export function transformText(
  input: string,
  mode: CodecMode,
  algorithm: CodecAlgorithm,
  hashFormat: HashOutputFormat = "hex-lower",
): TransformResult {
  if (isHashAlgorithm(algorithm)) {
    if (mode === "decode") throw new CodecError("hash-decode-unsupported");
    return { output: hashText(input, algorithm, hashFormat), binary: false };
  }

  if (mode === "encode") {
    switch (algorithm) {
      case "base64": return { output: Buffer.from(input, "utf8").toString("base64"), binary: false };
      case "base64url": return { output: Buffer.from(input, "utf8").toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, ""), binary: false };
      case "url": return { output: encodeURIComponent(input), binary: false };
      case "hex": return { output: Buffer.from(input, "utf8").toString("hex"), binary: false };
    }
  }

  switch (algorithm) {
    case "base64": return displayDecoded(decodeBase64(input, false));
    case "base64url": return displayDecoded(decodeBase64(input, true));
    case "url": {
      try {
        return { output: decodeURIComponent(input), binary: false };
      } catch {
        throw new CodecError("invalid-url-encoding");
      }
    }
    case "hex": {
      const normalized = input.replace(/\s/g, "");
      if (normalized.length % 2 !== 0) throw new CodecError("invalid-hex-length");
      if (!/^[0-9a-fA-F]*$/.test(normalized)) throw new CodecError("invalid-hex-characters");
      return displayDecoded(Buffer.from(normalized, "hex"));
    }
  }
}

export function isHashAlgorithm(value: string): value is HashAlgorithm {
  return value === "md5" || value === "sha1" || value === "sha256" || value === "sha512";
}
