/**
 * v3 Node SDK bootstrap entry. Reads the pipe path from stdin, connects to the named pipe, and
 * starts a HandlerRouter.
 *
 * This mirrors the C# NodeProcessController's spawn contract: the host writes one line to the
 * Node process's stdin — the pipe path — then waits for the Node side to connect before
 * promoting the session to Ready.
 */

import readline from "node:readline/promises";
import { NodeTransport } from "./transport.ts";
import { HandlerRouter } from "./router.ts";
import { type Envelope } from "./protocol.ts";

export interface PluginHandlers {
  [route: string]: (payload: any) => Promise<any> | any;
}

export interface PluginRuntime {
  transport: NodeTransport;
  router: HandlerRouter;
  close(): Promise<void>;
}

/**
 * Connects to the host pipe (reading the bootstrap line from stdin) and returns a runtime
 * whose router dispatches inbound plugin.call.* requests to the given handlers.
 */
export async function runPlugin(handlers: PluginHandlers): Promise<PluginRuntime> {
  const pipePath = await readBootstrapLine();

  const transport = new NodeTransport();
  await transport.connect(pipePath);

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

/** Reads line 1 of stdin: the named-pipe path. */
async function readBootstrapLine(): Promise<string> {
  const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
  try {
    const line = await new Promise<string>((resolve, reject) => {
      rl.once("line", (l) => resolve(l.trim()));
      rl.once("close", () => reject(new Error("stdin closed before bootstrap line")));
    });
    if (!line) {
      throw new Error(`malformed bootstrap line: ${JSON.stringify(line)}`);
    }
    return line;
  } finally {
    rl.close();
  }
}
