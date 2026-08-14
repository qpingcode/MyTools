// Minimal v3 plugin entry for integration testing: registers a single plugin.call.echo handler
// that returns its payload verbatim. Spawned by the C# NodeProcessController integration test.
import { runPlugin } from "./src/bootstrap.ts";

await runPlugin({
  "plugin.call.echo": async (payload) => ({ echoed: payload }),
  "plugin.call.search": async (payload) => ({ items: [{ id: "1", title: String(payload?.query ?? ""), subtitle: "", priority: 0 }] }),
});

// Keep the process alive; the host closes the pipe on stop which disconnects the transport.
process.stdin.resume();
