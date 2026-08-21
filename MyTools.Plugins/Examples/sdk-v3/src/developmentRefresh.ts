import { createConnection } from "node:net";

export const DEVELOPMENT_REFRESH_PIPE_PATH = "\\\\.\\pipe\\MyTools.DevelopmentPlugins.Refresh";

const DEFAULT_RETRY_DELAY_MS = 250;
const DEFAULT_MAX_ATTEMPTS = 2;
const DEFAULT_REQUEST_TIMEOUT_MS = 2_000;
const VALID_PLUGIN_ID = /^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$/;

export type DevelopmentRefreshRequestOptions = {
  pipePath?: string;
  retryDelayMs?: number;
  maxAttempts?: number;
  requestTimeoutMs?: number;
};

/** Internal implementation with injectable timing and endpoint for protocol tests. */
export async function requestDevelopmentPluginRefreshWithOptions(
  pluginId: string,
  options: DevelopmentRefreshRequestOptions = {},
): Promise<void> {
  if (!VALID_PLUGIN_ID.test(pluginId)) {
    throw new TypeError(`Invalid MyTools plugin ID: ${pluginId}`);
  }

  const pipePath = options.pipePath ?? DEVELOPMENT_REFRESH_PIPE_PATH;
  const retryDelayMs = options.retryDelayMs ?? DEFAULT_RETRY_DELAY_MS;
  const maxAttempts = options.maxAttempts ?? DEFAULT_MAX_ATTEMPTS;
  const requestTimeoutMs = options.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;
  if (!Number.isInteger(maxAttempts) || maxAttempts < 1) {
    throw new RangeError("maxAttempts must be a positive integer");
  }
  if (!Number.isFinite(retryDelayMs) || retryDelayMs < 0) {
    throw new RangeError("retryDelayMs must be a non-negative number");
  }
  if (!Number.isFinite(requestTimeoutMs) || requestTimeoutMs <= 0) {
    throw new RangeError("requestTimeoutMs must be a positive number");
  }

  let lastError: unknown;
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    try {
      await sendRefreshRequest(pipePath, pluginId, requestTimeoutMs);
      return;
    } catch (error) {
      lastError = error;
      if (attempt < maxAttempts) {
        await delay(retryDelayMs);
      }
    }
  }

  throw new Error(
    `Failed to request MyTools refresh for ${pluginId} after ${maxAttempts} attempts`,
    { cause: lastError },
  );
}

function sendRefreshRequest(
  pipePath: string,
  pluginId: string,
  requestTimeoutMs: number,
): Promise<void> {
  return new Promise((resolve, reject) => {
    const socket = createConnection(pipePath);
    let settled = false;
    let timeout: ReturnType<typeof setTimeout> | undefined;

    const settle = (error?: Error) => {
      if (settled) return;
      settled = true;
      if (timeout) clearTimeout(timeout);
      if (error) reject(error);
      else resolve();
    };

    socket.once("connect", () => {
      socket.end(`${pluginId}\n`, () => {
        settle();
      });
    });
    socket.once("error", (error) => {
      socket.destroy();
      settle(error);
    });
    timeout = setTimeout(() => {
      socket.destroy();
      settle(new Error(`Development refresh request timed out after ${requestTimeoutMs}ms`));
    }, requestTimeoutMs);
  });
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
