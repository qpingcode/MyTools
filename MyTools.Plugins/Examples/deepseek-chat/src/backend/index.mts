import { createTool } from "@qping/plugin-common/server";
import { mytoolsI18n } from "@qping/plugin-common/i18n";

const DEEPSEEK_API_URL = process.env.DEEPSEEK_API_URL || "https://api.deepseek.com/chat/completions";
const DEEPSEEK_MODEL = process.env.DEEPSEEK_MODEL || "deepseek-chat";
const DEEPSEEK_API_KEY = process.env.DEEPSEEK_API_KEY || "";

type ChatMessage = {
  role: "user" | "assistant";
  content: string;
  createdAt: string;
};

type Conversation = {
  id: string;
  messages: ChatMessage[];
  streaming: boolean;
  error: string;
  updatedAt: string;
};

type ChatState = {
  status: "ready" | "error";
  conversationId: string;
  messages: ChatMessage[];
  streaming: boolean;
  error: string;
};

const conversations = new Map<string, Conversation>();

function normalizeText(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function createConversation(): Conversation {
  const conversation: Conversation = {
    id: crypto.randomUUID(),
    messages: [],
    streaming: false,
    error: "",
    updatedAt: new Date().toISOString(),
  };
  conversations.set(conversation.id, conversation);
  return conversation;
}

function getConversation(id: unknown): Conversation {
  const normalized = normalizeText(id);
  return normalized && conversations.has(normalized)
    ? conversations.get(normalized)!
    : createConversation();
}

function toState(conversation: Conversation): ChatState {
  return {
    status: conversation.error ? "error" : "ready",
    conversationId: conversation.id,
    messages: conversation.messages,
    streaming: conversation.streaming,
    error: conversation.error,
  };
}

function buildSearchItem(query: unknown) {
  const text = normalizeText(query);
  return {
    id: "deepseek-chat",
    title: text
      ? mytoolsI18n.t("Plugin.DeepSeekChat.Result.Title", { defaultValue: "Chat: {{text}}", text })
      : mytoolsI18n.t("Plugin.DeepSeekChat.Name", { defaultValue: "DeepSeek Chat" }),
    subtitle: mytoolsI18n.t("Plugin.DeepSeekChat.Result.Subtitle", {
      defaultValue: "Talk with DeepSeek using a streaming chat view",
    }),
    priority: 100,
    icon: {
      kind: "emoji",
      value: "💬",
    },
    actions: [
      {
        id: "open-detail",
        title: mytoolsI18n.t("Plugin.DeepSeekChat.Action.OpenChat.Title", { defaultValue: "Open Chat" }),
        kind: "detail",
        description: mytoolsI18n.t("Plugin.DeepSeekChat.Action.OpenChat.Description", {
          defaultValue: "Open DeepSeek chat",
        }),
      },
    ],
  };
}

function createDetail(query: unknown) {
  const conversation = createConversation();
  const initialText = normalizeText(query);
  if (initialText) {
    addUserMessage(conversation, initialText);
    startAssistantStream(conversation);
  }

  return {
    type: "web-detail",
    htmlEntry: "web/index.html",
    title: mytoolsI18n.t("Plugin.DeepSeekChat.Name", { defaultValue: "DeepSeek Chat" }),
    initialState: toState(conversation),
  };
}

function addUserMessage(conversation: Conversation, content: string): void {
  conversation.messages.push({
    role: "user",
    content,
    createdAt: new Date().toISOString(),
  });
  conversation.updatedAt = new Date().toISOString();
}

function addAssistantPlaceholder(conversation: Conversation): number {
  conversation.messages.push({
    role: "assistant",
    content: "",
    createdAt: new Date().toISOString(),
  });
  conversation.updatedAt = new Date().toISOString();
  return conversation.messages.length - 1;
}

function modelMessages(conversation: Conversation) {
  return conversation.messages
    .filter((message) => normalizeText(message.content))
    .map((message) => ({
      role: message.role === "assistant" ? "assistant" : "user",
      content: message.content,
    }));
}

function startAssistantStream(conversation: Conversation): void {
  if (conversation.streaming) {
    return;
  }

  const assistantIndex = addAssistantPlaceholder(conversation);
  conversation.streaming = true;
  conversation.error = "";
  void streamAssistantResponse(conversation, assistantIndex);
}

async function streamAssistantResponse(conversation: Conversation, assistantIndex: number): Promise<void> {
  try {
    if (!DEEPSEEK_API_KEY) {
      throw new Error(mytoolsI18n.t("Plugin.DeepSeekChat.Error.MissingApiKey", {
        defaultValue: "Missing DEEPSEEK_API_KEY environment variable.",
      }));
    }

    const response = await fetch(DEEPSEEK_API_URL, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${DEEPSEEK_API_KEY}`,
      },
      body: JSON.stringify({
        model: DEEPSEEK_MODEL,
        stream: true,
        messages: modelMessages(conversation),
      }),
    });

    if (!response.ok) {
      const body = await response.text();
      throw new Error(mytoolsI18n.t("Plugin.DeepSeekChat.Error.ApiFailed", {
        defaultValue: "DeepSeek chat failed ({{status}}): {{body}}",
        status: response.status,
        body,
      }));
    }

    if (!response.body) {
      throw new Error(mytoolsI18n.t("Plugin.DeepSeekChat.Error.NoStream", {
        defaultValue: "DeepSeek chat response did not include a stream.",
      }));
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8");
    let buffer = "";

    while (true) {
      const { value, done } = await reader.read();
      if (done) {
        break;
      }

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split(/\r?\n/);
      buffer = lines.pop() || "";
      for (const line of lines) {
        appendStreamLine(conversation, assistantIndex, line);
      }
    }

    if (buffer) {
      appendStreamLine(conversation, assistantIndex, buffer);
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    conversation.error = message;
    conversation.messages[assistantIndex].content ||= message;
  } finally {
    conversation.streaming = false;
    conversation.updatedAt = new Date().toISOString();
  }
}

function appendStreamLine(conversation: Conversation, assistantIndex: number, line: string): void {
  const trimmed = line.trim();
  if (!trimmed.startsWith("data:")) {
    return;
  }

  const payload = trimmed.slice(5).trim();
  if (!payload || payload === "[DONE]") {
    return;
  }

  const data = JSON.parse(payload);
  const delta = data?.choices?.[0]?.delta?.content;
  if (typeof delta !== "string" || !delta) {
    return;
  }

  conversation.messages[assistantIndex].content += delta;
  conversation.updatedAt = new Date().toISOString();
}

function handleSend(payload: unknown): ChatState {
  const data = isRecord(payload) ? payload : {};
  const conversation = getConversation(data.conversationId);
  const content = normalizeText(data.text);
  if (!content || conversation.streaming) {
    return toState(conversation);
  }

  addUserMessage(conversation, content);
  startAssistantStream(conversation);
  return toState(conversation);
}

function createErrorState(error: unknown): ChatState {
  return {
    status: "error",
    conversationId: "",
    messages: [],
    streaming: false,
    error: error instanceof Error ? error.message : String(error),
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

const tool = createTool();

tool
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search((params) => ({
    items: [buildSearchItem(params.query || "")],
  }))
  .action((params) => ({
    message: mytoolsI18n.t("Plugin.DeepSeekChat.Action.OpenChat.Success", {
      defaultValue: "Opened DeepSeek chat",
    }),
    actionType: "none",
    detail: createDetail(params.query || ""),
  }))
  .handle("send", (payload) => {
    try {
      return handleSend(payload);
    } catch (error) {
      return createErrorState(error);
    }
  })
  .handle("poll", (payload) => {
    const data = isRecord(payload) ? payload : {};
    return toState(getConversation(data.conversationId));
  })
  .handle("newChat", () => toState(createConversation()))
  .start();
