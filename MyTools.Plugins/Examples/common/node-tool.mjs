import readline from "node:readline";
export class NodeTool {
    #handlers = new Map();
    #searchHandler = null;
    #actionHandler = null;
    #started = false;
    search(handler) {
        this.#searchHandler = handler;
        return this;
    }
    action(handler) {
        this.#actionHandler = handler;
        return this;
    }
    handle(action, handler) {
        if (!action || typeof action !== "string") {
            throw new Error("tool.handle requires an action name.");
        }
        if (typeof handler !== "function") {
            throw new Error("tool.handle requires a handler.");
        }
        this.#handlers.set(action, handler);
        return this;
    }
    publish(subjectId, payload = {}) {
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
    start() {
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
    async #handleLine(line) {
        if (!line || !line.trim()) {
            return;
        }
        let message;
        try {
            message = parseRequest(line);
        }
        catch (error) {
            writeError(null, -32700, error instanceof Error ? error.message : String(error));
            return;
        }
        const id = message.id;
        const method = message.method;
        const params = message.params ?? {};
        try {
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
            if (method === "detailCall") {
                const action = typeof params.action === "string" ? params.action : "";
                const handler = this.#handlers.get(action);
                if (!handler) {
                    writeError(id, -32601, `Unsupported detail action: ${action}`);
                    return;
                }
                const result = await handler(params.payload ?? {}, {
                    action,
                    itemId: typeof params.itemId === "string" ? params.itemId : "",
                    query: typeof params.query === "string" ? params.query : "",
                });
                writeResponse(id, { result });
                return;
            }
            writeError(id, -32601, `Unsupported method: ${method}`);
        }
        catch (error) {
            writeError(id, -32000, error instanceof Error ? error.message : "Node tool handler failed.");
        }
    }
}
export function createTool() {
    return new NodeTool();
}
function parseRequest(line) {
    const message = JSON.parse(line);
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
function writeResponse(id, result) {
    writeMessage({
        jsonrpc: "2.0",
        id,
        result,
    });
}
function writeError(id, code, message) {
    writeMessage({
        jsonrpc: "2.0",
        id,
        error: {
            code,
            message,
        },
    });
}
function writeMessage(message) {
    process.stdout.write(`${JSON.stringify(message)}\n`);
}
function isRecord(value) {
    return typeof value === "object" && value !== null;
}
function isJsonRpcId(value) {
    return typeof value === "string" || typeof value === "number" || value === null;
}
