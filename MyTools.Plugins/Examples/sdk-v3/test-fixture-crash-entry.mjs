// Crash fixture for NodeProcessController: complete handshake, then die with a
// distinctive stderr line and exit code so the host can surface FailureDetails.
import { runPlugin } from "./src/bootstrap.ts";

await runPlugin({
  "plugin.call.echo": async (payload) => ({ echoed: payload }),
});

console.error("fixture backend crashed");
process.exit(23);
