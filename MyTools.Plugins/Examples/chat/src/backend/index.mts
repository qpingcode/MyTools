import { createPlugin } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const CHAT_HOST_TIMEOUT_MS = 20 * 60_000;

type InteractionAnswer = { questionId: string; prompt: string; values: string[]; text: string };
type InteractionResponse = { interactionId: string; answers: InteractionAnswer[] };
type SendPayload = {
  sessionId: string;
  message: string;
  model: string;
  interactionResponse?: InteractionResponse;
};

const plugin = createPlugin();

async function sendInBackground(payload: SendPayload): Promise<void> {
  try {
    await plugin.hostCall("ai.chat.send", payload, CHAT_HOST_TIMEOUT_MS);
  } catch {
    // The host state contains the user-visible error or cancellation state.
  }
}

plugin
  .initialize((params) => { mytoolsI18n.configure(params); return {}; })
  .handle("status", async () => plugin.hostCall("ai.chat.status"))
  .handle("send", (payload: SendPayload) => {
    void sendInBackground(payload);
    return { accepted: true };
  })
  .handle("poll", async (payload: { sessionId: string; model?: string }) =>
    plugin.hostCall("ai.chat.state", payload))
  .handle("list", async () => plugin.hostCall("ai.chat.list"))
  .handle("cancel", async (payload: { sessionId: string }) =>
    plugin.hostCall("ai.chat.cancel", payload))
  .handle("clear", async (payload: { sessionId?: string }) =>
    plugin.hostCall("ai.chat.clear", payload))
  .start();
