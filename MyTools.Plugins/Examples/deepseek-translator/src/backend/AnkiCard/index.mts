import { buildDeckSummary, deleteCard, getCardPage, getNextDueCard, reviewCard, saveCard } from "../common/anki.mjs";
import { createPlugin } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

function createReviewState() {
  return {
    status: "ready",
    summary: buildDeckSummary(),
    card: getNextDueCard(),
    error: "",
  };
}

function createBrowseState(page: unknown) {
  return {
    status: "browse",
    summary: buildDeckSummary(),
    browse: getCardPage(page),
    error: "",
  };
}

function createDetail() {
  return {
    type: "web-detail",
    htmlEntry: "web/AnkiCard/index.html",
    title: mytoolsI18n.t("Plugin.DeepSeekTranslator.Anki.Name", { defaultValue: "DeepSeek Anki Cards" }),
    initialState: createReviewState(),
  };
}

function createErrorState(error: unknown) {
  return {
    status: "error",
    summary: buildDeckSummary(),
    card: null as unknown,
    error: error instanceof Error ? error.message : String(error),
  };
}

function payloadRecord(payload: unknown): Record<string, unknown> {
  return typeof payload === "object" && payload !== null ? payload as Record<string, unknown> : {};
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .action(() => ({
    message: mytoolsI18n.t("Plugin.DeepSeekTranslator.Anki.Action.Review.Success", {
      defaultValue: "Opened Anki cards",
    }),
    actionType: "none",
    detail: createDetail(),
  }))
  .handle("load", () => createReviewState())
  .handle("review", (payload) => {
    try {
      const data = payloadRecord(payload);
      reviewCard(data.cardId, data.rating);
      return createReviewState();
    } catch (error) {
      return createErrorState(error);
    }
  })
  .handle("browse", (payload) => {
    try {
      const data = payloadRecord(payload);
      return createBrowseState(data.page);
    } catch (error) {
      return createErrorState(error);
    }
  })
  .handle("deleteCard", (payload) => {
    try {
      const data = payloadRecord(payload);
      deleteCard(data.cardId);
      return createBrowseState(data.page);
    } catch (error) {
      return createErrorState(error);
    }
  })
  .handle("saveCard", (payload) => {
    try {
      const data = payloadRecord(payload);
      saveCard(data.card);
      return createBrowseState(data.page);
    } catch (error) {
      return createErrorState(error);
    }
  })
  .start();
