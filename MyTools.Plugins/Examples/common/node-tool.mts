import readline from "node:readline";

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

    let message: JsonRpcRequest;
    try {
      message = parseRequest(line);
    } catch (error) {
      writeError(null, -32700, error instanceof Error ? error.message : String(error));
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
