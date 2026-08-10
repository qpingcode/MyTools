import readline from "node:readline";
import crypto from "node:crypto";

type JsonRpcId = string | number | null;

type JsonRpcRequest = {
  id: JsonRpcId;
  method: string;
  params: Record<string, unknown>;
};

type NodeToolContext = {
  action: string;
  itemId: string;
  query: string;
  locale: string;
  fallbackLocale: string;
};

type NodeToolHandler = (payload: unknown, context: NodeToolContext) => unknown | Promise<unknown>;
type NodeToolHostHandler = (params: Record<string, unknown>) => unknown | Promise<unknown>;

export class NodeTool {
  #handlers = new Map<string, NodeToolHandler>();
  #searchHandler: NodeToolHostHandler | null = null;
  #actionHandler: NodeToolHostHandler | null = null;
  #initializeHandler: NodeToolHostHandler | null = null;
  #started = false;
  #hostCallPending = new Map<string, { resolve: (value: unknown) => void; reject: (reason: unknown) => void; timer: ReturnType<typeof setTimeout> }>();

  initialize(handler: NodeToolHostHandler): this {
    this.#initializeHandler = handler;
    return this;
  }

  search(handler: NodeToolHostHandler): this {
    this.#searchHandler = handler;
    return this;
  }

  action(handler: NodeToolHostHandler): this {
    this.#actionHandler = handler;
    return this;
  }

  handle(action: string, handler: NodeToolHandler): this {
    if (!action || typeof action !== "string") {
      throw new Error("tool.handle requires an action name.");
    }

    if (typeof handler !== "function") {
      throw new Error("tool.handle requires a handler.");
    }

    this.#handlers.set(action, handler);
    return this;
  }

  publish(subjectId: string, payload: unknown = {}): void {
    if (!subjectId || typeof subjectId !== "string") {
      throw new Error("tool.publish requires a subject id.");
    }

    writeMessage({
      jsonrpc: "2.0",
      method: "publish",
      params: {
        subjectId,
        payload,
      },
    });
  }

  /**
   * 向宿主发起能力请求（hostCall），等待宿主写回响应。
   * 仅对注册了 HostCallHandler 的插件有效（如 settings 插件）。
   */
  hostCall(method: string, params: Record<string, unknown> = {}): Promise<unknown> {
    if (!method || typeof method !== "string") {
      return Promise.reject(new Error("tool.hostCall requires a method name."));
    }

    const id = crypto.randomUUID();
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        if (this.#hostCallPending.delete(id)) {
          reject(new Error(`hostCall "${method}" timed out after 30s.`));
        }
      }, 30000);

      this.#hostCallPending.set(id, { resolve, reject, timer });

      writeMessage({
        jsonrpc: "2.0",
        id,
        method: "hostCall",
        params: { method, params },
      });
    });
  }

  start(): void {
    if (this.#started) {
      return;
    }

    this.#started = true;
    const rl = readline.createInterface({
      input: process.stdin,
      crlfDelay: Infinity,
    });

    rl.on("line", (line) => {
      void this.#handleLine(line);
    });
  }

  async #handleLine(line: string): Promise<void> {
    if (!line || !line.trim()) {
      return;
    }

    let raw: Record<string, unknown>;
    try {
      raw = JSON.parse(line.replace(/^\uFEFF/, "")) as Record<string, unknown>;
    } catch (error) {
      writeError(null, -32700, error instanceof Error ? error.message : String(error));
      return;
    }

    // 宿主写回的 hostCall 响应：有 id 无 method，可能是 result 或 error。
    const rawId = typeof raw.id === "string" ? raw.id : null;
    if (rawId && !raw.method && this.#hostCallPending.has(rawId)) {
      const pending = this.#hostCallPending.get(rawId)!;
      this.#hostCallPending.delete(rawId);
      clearTimeout(pending.timer);

      const error = raw.error as { message?: string } | undefined;
      if (error) {
        pending.reject(new Error(error.message ?? "hostCall error"));
      } else {
        pending.resolve(raw.result);
      }
      return;
    }

    let message: JsonRpcRequest;
    try {
      message = parseRequest(line);
    } catch (error) {
      writeError(rawId, -32700, error instanceof Error ? error.message : String(error));
      return;
    }

    const id = message.id;
    const method = message.method;
    const params = message.params ?? {};

    try {
      if (method === "initialize") {
        const result = this.#initializeHandler ? await this.#initializeHandler(params) : {};
        writeResponse(id, result ?? {});
        return;
      }

      if (method === "search") {
        if (!this.#searchHandler) {
          writeError(id, -32601, "Search handler is not registered.");
          return;
        }

        writeResponse(id, await this.#searchHandler(params));
        return;
      }

      if (method === "invokeAction") {
        if (!this.#actionHandler) {
          writeError(id, -32601, "Action handler is not registered.");
          return;
        }

        writeResponse(id, await this.#actionHandler(params));
        return;
      }

      if (method === "detailCall" || method === "detailEvent") {
        const action = method === "detailEvent"
          ? (typeof params.eventName === "string" ? params.eventName : "")
          : (typeof params.action === "string" ? params.action : "");
        const handler = this.#handlers.get(action);
        if (!handler) {
          writeError(id, -32601, `Unsupported detail action: ${action}`);
          return;
        }

        const result = await handler(params.payload ?? {}, {
          action,
          itemId: typeof params.itemId === "string" ? params.itemId : "",
          query: typeof params.query === "string" ? params.query : "",
          locale: typeof params.locale === "string" ? params.locale : "en-US",
          fallbackLocale: typeof params.fallbackLocale === "string" ? params.fallbackLocale : "en-US",
        });
        writeResponse(id, method === "detailEvent" ? { state: result } : { result });
        return;
      }

      writeError(id, -32601, `Unsupported method: ${method}`);
    } catch (error) {
      writeError(id, -32000, error instanceof Error ? error.message : "Node tool handler failed.");
    }
  }
}

export function createTool(): NodeTool {
  return new NodeTool();
}

function parseRequest(line: string): JsonRpcRequest {
  const message = JSON.parse(line.replace(/^\uFEFF/, "")) as unknown;
  if (!isRecord(message)) {
    throw new Error("JSON-RPC request must be an object.");
  }
  const rawId = message.id;
  const id = isJsonRpcId(rawId)
    ? rawId
    : null;

  return {
    id,
    method: typeof message.method === "string" ? message.method : "",
    params: isRecord(message.params) ? message.params : {},
  };
}

function writeResponse(id: JsonRpcId, result: unknown): void {
  writeMessage({
    jsonrpc: "2.0",
    id,
    result,
  });
}

function writeError(id: JsonRpcId, code: number, message: string): void {
  writeMessage({
    jsonrpc: "2.0",
    id,
    error: {
      code,
      message,
    },
  });
}

function writeMessage(message: unknown): void {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isJsonRpcId(value: unknown): value is JsonRpcId {
  return typeof value === "string" || typeof value === "number" || value === null;
}
