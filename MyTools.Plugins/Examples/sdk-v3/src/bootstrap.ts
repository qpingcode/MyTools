/**
 * v3 Node SDK bootstrap entry. Reads the bootstrap line from stdin (pipePath\ttoken), connects to
 * the named pipe, completes bus.handshake (presenting the token and receiving bound identity),
 * and starts a HandlerRouter stamped with that identity.
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
  EndpointIds,
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

const SUPPORTED_VERSIONS = [ProtocolVersion];

/**
 * Connects to the host pipe (reading the bootstrap line from stdin), completes handshake, and
 * returns a runtime whose router dispatches inbound plugin.call.* requests to the given handlers.
 */
export async function runPlugin(handlers: PluginHandlers): Promise<PluginRuntime> {
  const { pipePath, token } = await readBootstrapLine();

  const transport = new NodeTransport();
  await transport.connect(pipePath);

  const identity = await completeHandshake(transport, token);
  const router = new HandlerRouter({ send: (env: Envelope) => transport.send(env) });
  router.setIdentity(identity);

  // Passive host-liveness watchdog: exit if Host stops sending bus.ping (orphan process).
  // Threshold is well above the host ping interval (~2s) to tolerate brief host stalls.
  const HOST_LOST_MS = 15_000;
  let lastPingAt = Date.now();
  const watchdog = setInterval(() => {
    if (Date.now() - lastPingAt > HOST_LOST_MS) {
      clearInterval(watchdog);
      process.exit(1);
    }
  }, 1_000);
  watchdog.unref?.();

  transport.onDisconnect(() => {
    clearInterval(watchdog);
    process.exit(1);
  });

  transport.onMessage((env) => {
    if (env.route === Routes.Bus.Ping) lastPingAt = Date.now();
    router.dispatch(env);
  });

  for (const [route, handler] of Object.entries(handlers)) {
    router.handle(route, handler);
  }

  return {
    transport,
    router,
    close: async () => {
      clearInterval(watchdog);
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
 * Sends bus.handshake with the bootstrap token and waits for the host response that binds
 * plugin/entry/session/endpoint identity. Rejects on HandshakeFailed / ProtocolMismatch / timeout.
 */
export async function completeHandshake(
  transport: NodeTransport,
  token: string,
  timeoutMs = 10000,
): Promise<{ pluginId: string; entryId: string; sessionId: string; endpointId: string }> {
  const id = randomBytes(16).toString("hex");
  const req: Envelope = {
    version: ProtocolVersion,
    id,
    traceId: id,
    sessionId: "",
    pluginId: "",
    entryId: "",
    endpointId: EndpointIds.NodeMain,
    kind: MessageKind.Request,
    route: Routes.Bus.Handshake,
    timeoutMs,
    payload: {
      version: ProtocolVersion,
      supportedVersions: SUPPORTED_VERSIONS,
      token,
    },
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
      const p = (env.payload ?? {}) as Record<string, unknown>;
      const pluginId = String(p.pluginId ?? "");
      const entryId = String(p.entryId ?? "");
      const sessionId = String(p.sessionId ?? "");
      const endpointId = String(p.endpointId ?? EndpointIds.NodeMain);
      if (!pluginId || !entryId || !sessionId) {
        reject(new Error("bus.handshake success response missing bound identity"));
        return;
      }
      resolve({ pluginId, entryId, sessionId, endpointId });
    });

    transport.send(req);
  });
}
