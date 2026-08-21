import { test } from "node:test";
import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";
import { readFileSync } from "node:fs";
import { createServer } from "node:net";
import { join } from "node:path";
import { tmpdir } from "node:os";

import { requestDevelopmentPluginRefresh } from "../src/dev.ts";
import {
  DEVELOPMENT_REFRESH_PIPE_PATH,
  requestDevelopmentPluginRefreshWithOptions,
} from "../src/developmentRefresh.ts";

test("development refresh pipe matches the desktop host contract", () => {
  const serviceUrl = new URL(
    "../../../../MyTools.Desktop/Services/DevelopmentPluginService.cs",
    import.meta.url,
  );
  const source = readFileSync(serviceUrl, "utf8");
  const match = source.match(/RefreshPipeName = "([^"]+)"/);

  assert.ok(match, "C# RefreshPipeName constant not found");
  assert.equal(DEVELOPMENT_REFRESH_PIPE_PATH, `\\\\.\\pipe\\${match[1]}`);
});

test("development refresh sends the plugin ID as one newline-terminated line", async () => {
  const pipePath = uniquePipePath();
  let received = "";
  const server = createServer((socket) => {
    socket.setEncoding("utf8");
    socket.on("data", (chunk) => { received += chunk; });
  });
  await listen(server, pipePath);

  try {
    await requestDevelopmentPluginRefreshWithOptions("hello-search", {
      pipePath,
      maxAttempts: 1,
    });
    await waitFor(() => received === "hello-search\n");
    assert.equal(received, "hello-search\n");
  } finally {
    await close(server);
  }
});

test("development refresh retries when the endpoint becomes available", async () => {
  const pipePath = uniquePipePath();
  let received = "";
  const server = createServer((socket) => {
    socket.setEncoding("utf8");
    socket.on("data", (chunk) => { received += chunk; });
  });

  const request = requestDevelopmentPluginRefreshWithOptions("hello-search", {
    pipePath,
    retryDelayMs: 100,
    maxAttempts: 2,
  });
  await new Promise((resolve) => setTimeout(resolve, 25));
  await listen(server, pipePath);

  try {
    await request;
    await waitFor(() => received === "hello-search\n");
  } finally {
    await close(server);
  }
});

test("development refresh rejects IDs that could break the line protocol", async () => {
  await assert.rejects(
    () => requestDevelopmentPluginRefresh("hello\nother-plugin"),
    /Invalid MyTools plugin ID/,
  );
});

test("development refresh reports the final failure after all attempts", async () => {
  const pipePath = uniquePipePath();

  await assert.rejects(
    () => requestDevelopmentPluginRefreshWithOptions("hello-search", {
      pipePath,
      retryDelayMs: 0,
      maxAttempts: 2,
      requestTimeoutMs: 100,
    }),
    (error) => {
      assert.match(error.message, /after 2 attempts/);
      assert.ok(error.cause instanceof Error);
      return true;
    },
  );
});

test("development refresh validates request timeout options", async () => {
  await assert.rejects(
    () => requestDevelopmentPluginRefreshWithOptions("hello-search", {
      requestTimeoutMs: 0,
    }),
    /requestTimeoutMs must be a positive number/,
  );
});

function uniquePipePath() {
  const name = `mytools-dev-refresh-test-${randomBytes(8).toString("hex")}`;
  return process.platform === "win32" ? `\\\\.\\pipe\\${name}` : join(tmpdir(), `${name}.sock`);
}

function listen(server, pipePath) {
  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(pipePath, () => {
      server.off("error", reject);
      resolve();
    });
  });
}

function close(server) {
  return new Promise((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  });
}

async function waitFor(predicate, timeoutMs = 1000) {
  const startedAt = Date.now();
  while (Date.now() - startedAt < timeoutMs) {
    if (predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error("waitFor timed out");
}
