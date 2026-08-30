import { detectLanguage, isLanguageId, type LanguageId } from "../shared/language";
import { formatSource } from "../shared/format";

type WorkerRequest =
  | { id: number; operation: "detect"; source: string }
  | { id: number; operation: "format"; source: string; language: LanguageId };

type WorkerResponse =
  | { id: number; ok: true; detected: LanguageId | null }
  | { id: number; ok: true; formatted: string }
  | { id: number; ok: false; error: string };

async function detectValidLanguage(source: string): Promise<LanguageId | null> {
  const detected = detectLanguage(source);
  if (!detected) return null;
  try {
    await formatSource(source, detected);
    return detected;
  } catch {
    return null;
  }
}

function errorMessage(error: unknown): string {
  if (!(error instanceof Error)) return String(error);
  return error.message.replace(/^\s*Error:\s*/i, "").trim();
}

self.addEventListener("message", (event: MessageEvent<WorkerRequest>) => {
  const request = event.data;
  void (async () => {
    let response: WorkerResponse;
    try {
      if (request.operation === "detect") {
        response = { id: request.id, ok: true, detected: await detectValidLanguage(request.source) };
      } else if (request.operation === "format" && isLanguageId(request.language)) {
        response = { id: request.id, ok: true, formatted: await formatSource(request.source, request.language) };
      } else {
        response = { id: request.id, ok: false, error: "Unsupported formatter request." };
      }
    } catch (error) {
      response = { id: request.id, ok: false, error: errorMessage(error) };
    }
    self.postMessage(response);
  })();
});
