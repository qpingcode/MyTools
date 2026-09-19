/**
 * v3 Node SDK bootstrap entry. Reads the bootstrap line from stdin (pipePath\ttoken), connects to
 * the named pipe, completes bus.handshake by presenting the token, and starts a HandlerRouter.
 *
 * This mirrors the C# NodeProcessController's spawn contract: the host writes one line to the
 * Node process's stdin — "<pipePath>\t<token>" — then waits for the Node side to connect the pipe
 * and complete handshake before promoting the session to Ready.
 */

import readline from "node:readline/promises";
import { randomBytes } from "node:crypto";
import { NodeTransport } from "./transport.ts";
import { HandlerRouter } from "./router.ts";
import {
  type Envelope,
  MessageKind,
  ProtocolVersion,
  Routes,
} from "./protocol.ts";

export interface PluginHandlers {
  [route: string]: (payload: any) => Promise<any> | any;
}

export interface PluginRuntime {
  transport: NodeTransport;
  router: HandlerRouter;
  close(): Promise<void>;
}

/**
 * Connects to the host pipe (reading the bootstrap line from stdin), completes handshake, and
 * returns a runtime whose router dispatches inbound plugin.call.* requests to the given handlers.
 */
export async function runPlugin(handlers: PluginHandlers): Promise<PluginRuntime> {
  const { pipePath, token } = await readBootstrapLine();

  const transport = new NodeTransport();
  await transport.connect(pipePath);

  await completeHandshake(transport, token);
  const router = new HandlerRouter({ send: (env: Envelope) => transport.send(env) });

  transport.onDisconnect(() => {
    process.exit(1);
  });

  transport.onMessage((env) => {
    router.dispatch(env);
  });

  for (const [route, handler] of Object.entries(handlers)) {
    router.handle(route, handler);
  }

  return {
    transport,
    router,
    close: async () => {
      await transport.close();
    },
  };
}

/** Reads line 1 of stdin: "<pipePath>\t<token>". */
async function readBootstrapLine(): Promise<{ pipePath: string; token: string }> {
  const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
  try {
    const line = await new Promise<string>((resolve, reject) => {
      rl.once("line", (l) => resolve(l));
      rl.once("close", () => reject(new Error("stdin closed before bootstrap line")));
    });
    const [pipePath, token] = line.split("\t");
    if (!pipePath || !token) {
      throw new Error(`malformed bootstrap line: ${JSON.stringify(line)}`);
    }
    return { pipePath, token };
  } finally {
    rl.close();
  }
}

/**
 * Sends bus.handshake with the bootstrap token and waits for host acknowledgement.
 * Protocol compatibility has already been checked from the manifest before process startup.
 */
export async function completeHandshake(
  transport: NodeTransport,
  token: string,
  timeoutMs = 10000,
): Promise<void> {
  const id = randomBytes(16).toString("hex");
  const req: Envelope = {
    version: ProtocolVersion,
    id,
    traceId: id,
    kind: MessageKind.Request,
    route: Routes.Bus.Handshake,
    timeoutMs,
    payload: { token },
  };

  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      unsubscribe();
      reject(new Error(`bus.handshake timed out after ${timeoutMs}ms`));
    }, timeoutMs);

    const unsubscribe = transport.onMessage((env: Envelope) => {
      if (env.kind !== MessageKind.Response || env.correlationId !== id) return;
      clearTimeout(timer);
      unsubscribe();
      if (env.error) {
        reject(new Error(`${env.error.code}: ${env.error.message}`));
        return;
      }
      resolve();
    });

    transport.send(req);
  });
}
