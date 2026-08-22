import { ANKI_CARDS_PATH, normalizeText, readJsonFile, writeJsonFile } from "./storage.mjs";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

type JsonRecord = Record<string, unknown>;

export type AnkiCard = JsonRecord & {
  id: string;
  sourceText: string;
  sourceKey: string;
  type: string;
  direction: string;
  front: string;
  back: string;
  prompt: string;
  answer: string;
  options: string[];
  phonetic: string;
  createdAt: string;
  updatedAt: string;
  dueAt: string;
  stability: number;
  difficulty: number;
  elapsedDays: number;
  scheduledDays: number;
  reps: number;
  lapses: number;
  state: string;
  lastReviewAt?: string;
  lastRating?: number;
};

const DECAY = -0.5;
const FACTOR = 19 / 81;
const DESIRED_RETENTION = 0.9;
const FSRS_W = [
  0.4072, 1.1829, 3.1262, 15.4722, 7.2102, 0.5316, 1.0651, 0.0234, 1.616, 0.1544,
  1.0824, 1.9813, 0.0953, 0.2975, 2.2042, 0.2407, 2.9466, 0.5034, 0.6567,
];

export function readAnkiCards(): { cards: AnkiCard[] } {
  const data = readJsonFile(ANKI_CARDS_PATH, { cards: [] });
  return {
    cards: Array.isArray(data.cards) ? data.cards.map(normalizeCard).filter(Boolean) : [],
  };
}

export function writeAnkiCards(cards: AnkiCard[]): void {
  writeJsonFile(ANKI_CARDS_PATH, { cards });
}

export function getCardsForSource(sourceText: unknown): AnkiCard[] {
  const normalized = normalizeText(sourceText).toLowerCase();
  return readAnkiCards().cards.filter((card) => card.sourceKey === normalized);
}

export function upsertGeneratedCards(sourceText: unknown, generatedCards: JsonRecord[]): AnkiCard[] {
  const normalizedSource = normalizeText(sourceText);
  const sourceKey = normalizedSource.toLowerCase();
  if (!normalizedSource || !Array.isArray(generatedCards) || generatedCards.length === 0) {
    return readAnkiCards().cards;
  }

  const existing = readAnkiCards().cards.filter((card) => card.sourceKey !== sourceKey);
  const now = new Date().toISOString();
  const cards = generatedCards
    .map((card, index) => createCard(normalizedSource, sourceKey, card, index, now))
    .filter((card): card is AnkiCard => card !== null);
  const allCards = [...existing, ...cards];
  writeAnkiCards(allCards);
  return allCards;
}

export function getNextDueCard(now = new Date()) {
  const cards = readAnkiCards().cards;
  const dueCards = cards
    .filter((card) => new Date(card.dueAt).getTime() <= now.getTime())
    .sort((left, right) => new Date(left.dueAt).getTime() - new Date(right.dueAt).getTime());
  return dueCards[0] || null;
}

export function getCardPage(pageValue: unknown) {
  const cards = readAnkiCards().cards
    .slice()
    .sort((left, right) => new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime());
  const total = cards.length;
  const page = constrain(Math.trunc(Number(pageValue) || 0), 0, Math.max(0, total - 1));
  return {
    card: cards[page] || null,
    page,
    total,
    hasPrevious: page > 0,
    hasNext: page < total - 1,
  };
}

export function deleteCard(cardId: unknown): AnkiCard[] {
  const data = readAnkiCards();
  const cards = data.cards.filter((card) => card.id !== cardId);
  if (cards.length === data.cards.length) {
    throw new Error(mytoolsI18n.t("Plugin.DeepSeekTranslator.Anki.Error.CardNotFound", {
      defaultValue: "Card not found.",
    }));
  }

  writeAnkiCards(cards);
  return cards;
}

export function saveCard(cardPayload: unknown): AnkiCard {
  const card = normalizeEditableCard(cardPayload);
  const data = readAnkiCards();
  const index = data.cards.findIndex((item) => item.id === card.id);
  const now = new Date().toISOString();
  if (index >= 0) {
    const existing = data.cards[index];
    data.cards[index] = {
      ...existing,
      ...card,
      createdAt: existing.createdAt,
      dueAt: existing.dueAt,
      stability: existing.stability,
      difficulty: existing.difficulty,
      elapsedDays: existing.elapsedDays,
      scheduledDays: existing.scheduledDays,
      reps: existing.reps,
      lapses: existing.lapses,
      state: existing.state,
      lastReviewAt: existing.lastReviewAt,
      lastRating: existing.lastRating,
      updatedAt: now,
    };
  } else {
    data.cards.push(card);
  }

  writeAnkiCards(data.cards);
  return card;
}

export function reviewCard(cardId: unknown, ratingValue: unknown, now = new Date()): AnkiCard {
  const rating = normalizeRating(ratingValue);
  const data = readAnkiCards();
  const index = data.cards.findIndex((card) => card.id === cardId);
  if (index < 0) {
    throw new Error(mytoolsI18n.t("Plugin.DeepSeekTranslator.Anki.Error.CardNotFound", {
      defaultValue: "Card not found.",
    }));
  }

  const reviewed = scheduleWithFsrs(data.cards[index], rating, now);
  data.cards[index] = reviewed;
  writeAnkiCards(data.cards);
  return reviewed;
}

export function buildDeckSummary(now = new Date()) {
  const cards = readAnkiCards().cards;
  return {
    total: cards.length,
    due: cards.filter((card) => new Date(card.dueAt).getTime() <= now.getTime()).length,
    newCards: cards.filter((card) => card.reps === 0).length,
  };
}

function createCard(sourceText: string, sourceKey: string, generatedCard: JsonRecord, index: number, now: string): AnkiCard | null {
  const type = normalizeCardType(generatedCard?.type || generatedCard?.direction);
  const front = normalizeText(generatedCard?.front || generatedCard?.prompt);
  const back = normalizeText(generatedCard?.back || generatedCard?.explanation || generatedCard?.answer);
  const answer = normalizeText(generatedCard?.answer || (type === "basic" ? back : ""));
  const options = normalizeOptions(generatedCard?.options);
  const phonetic = normalizeText(generatedCard?.phonetic);
  if (!sourceText || !isValidCardContent(type, front, back, answer, options)) {
    return null;
  }

  return {
    id: `${sourceKey}:${type}:${index}:${hashText(`${front}|${back}|${answer}|${options.join("|")}`)}`,
    sourceText,
    sourceKey,
    type,
    direction: type === "choice-zh-to-en" ? "zh-to-en" : "en-to-zh",
    front,
    back,
    prompt: front,
    answer,
    options,
    phonetic,
    createdAt: now,
    updatedAt: now,
    dueAt: now,
    stability: 0,
    difficulty: 0,
    elapsedDays: 0,
    scheduledDays: 0,
    reps: 0,
    lapses: 0,
    state: "new",
  };
}

function normalizeEditableCard(payload: unknown): AnkiCard {
  const data = asRecord(payload);
  const sourceText = normalizeText(data.sourceText);
  const type = normalizeCardType(data.type || data.direction);
  const front = normalizeText(data.front || data.prompt);
  const back = normalizeText(data.back || (type === "basic" ? data.answer : ""));
  const answer = normalizeText(data.answer || (type === "basic" ? back : ""));
  const options = normalizeOptions(data.options);
  const phonetic = normalizeText(data.phonetic);
  if (!isValidCardContent(type, front, back, answer, options)) {
    throw new Error(type === "basic"
      ? mytoolsI18n.t("Plugin.DeepSeekTranslator.Anki.Error.InvalidBasicCard", {
        defaultValue: "Basic cards require source, front, and back.",
      })
      : mytoolsI18n.t("Plugin.DeepSeekTranslator.Anki.Error.InvalidChoiceCard", {
        defaultValue: "Choice cards require source, front, back, answer, and at least two options including the answer.",
      }));
  }

  const now = new Date().toISOString();
  const sourceKey = normalizeText(data.sourceKey || sourceText).toLowerCase();
  return {
    id: normalizeText(data.id) || `${sourceKey}:${type}:manual:${hashText(`${front}|${back}|${answer}|${options.join("|")}|${now}`)}`,
    sourceText,
    sourceKey,
    type,
    direction: type === "choice-zh-to-en" ? "zh-to-en" : "en-to-zh",
    front,
    back,
    prompt: front,
    answer,
    options,
    phonetic,
    createdAt: normalizeText(data.createdAt) || now,
    updatedAt: now,
    dueAt: normalizeText(data.dueAt) || now,
    stability: numberOrZero(data.stability),
    difficulty: numberOrZero(data.difficulty),
    elapsedDays: numberOrZero(data.elapsedDays),
    scheduledDays: numberOrZero(data.scheduledDays),
    reps: integerOrZero(data.reps),
    lapses: integerOrZero(data.lapses),
    state: normalizeText(data.state) || "new",
  };
}

function normalizeCard(card: unknown): AnkiCard | null {
  const data = asRecord(card);
  if (!normalizeText(data.id)) {
    return null;
  }

  const type = normalizeCardType(data.type || data.direction);
  const front = normalizeText(data.front || data.prompt);
  const back = normalizeText(data.back || data.answer);
  const answer = normalizeText(data.answer || (type === "basic" ? back : ""));
  const options = normalizeOptions(data.options);
  const phonetic = normalizeText(data.phonetic);
  if (!isValidCardContent(type, front, back, answer, options)) {
    return null;
  }

  return {
    ...data,
    id: normalizeText(data.id),
    createdAt: normalizeText(data.createdAt) || new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    sourceText: normalizeText(data.sourceText),
    sourceKey: normalizeText(data.sourceKey || data.sourceText).toLowerCase(),
    type,
    direction: type === "choice-zh-to-en" ? "zh-to-en" : "en-to-zh",
    front,
    back,
    prompt: front,
    answer,
    options,
    phonetic,
    dueAt: normalizeText(data.dueAt) || new Date().toISOString(),
    stability: numberOrZero(data.stability),
    difficulty: numberOrZero(data.difficulty),
    elapsedDays: numberOrZero(data.elapsedDays),
    scheduledDays: numberOrZero(data.scheduledDays),
    reps: integerOrZero(data.reps),
    lapses: integerOrZero(data.lapses),
    state: normalizeText(data.state) || "new",
  };
}

function normalizeCardType(value: unknown): string {
  const type = normalizeText(value);
  if (type === "basic" || type === "original" || type === "anki") {
    return "basic";
  }

  if (type === "choice-zh-to-en" || type === "zh-to-en") {
    return "choice-zh-to-en";
  }

  return "choice-en-to-zh";
}

function normalizeOptions(value: unknown): string[] {
  return Array.isArray(value) ? [...new Set(value.map(normalizeText).filter(Boolean))] : [];
}

function isValidCardContent(type: string, front: string, back: string, answer: string, options: string[]): boolean {
  if (!front || !back) {
    return false;
  }

  if (type === "basic") {
    return true;
  }

  return Boolean(answer && options.length >= 2 && options.includes(answer));
}

function scheduleWithFsrs(card: AnkiCard, rating: number, now: Date): AnkiCard {
  const reviewedAt = normalizeText(card.lastReviewAt);
  const elapsedDays = reviewedAt
    ? Math.max(0, (now.getTime() - new Date(reviewedAt).getTime()) / 86400000)
    : 0;

  const firstReview = card.reps === 0 || card.stability <= 0 || card.difficulty <= 0;
  const difficulty = firstReview ? initDifficulty(rating) : nextDifficulty(card.difficulty, rating);
  const stability = firstReview
    ? initStability(rating)
    : nextStability(card, rating, elapsedDays);
  const intervalDays = nextIntervalDays(stability, rating);
  const dueAt = new Date(now.getTime() + intervalDays * 86400000);

  return {
    ...card,
    updatedAt: now.toISOString(),
    lastReviewAt: now.toISOString(),
    dueAt: dueAt.toISOString(),
    stability,
    difficulty,
    elapsedDays,
    scheduledDays: intervalDays,
    reps: card.reps + 1,
    lapses: card.lapses + (rating === 1 ? 1 : 0),
    state: rating === 1 ? "relearning" : "review",
    lastRating: rating,
  };
}

function initStability(rating: number): number {
  return Math.max(0.1, FSRS_W[rating - 1]);
}

function initDifficulty(rating: number): number {
  return constrain(FSRS_W[4] - Math.exp((rating - 1) * FSRS_W[5]) + 1, 1, 10);
}

function nextDifficulty(difficulty: number, rating: number): number {
  const delta = FSRS_W[6] * (rating - 3);
  const next = difficulty - delta;
  return constrain(meanReversion(FSRS_W[4], next), 1, 10);
}

function nextStability(card: AnkiCard, rating: number, elapsedDays: number): number {
  const retrievability = Math.pow(1 + FACTOR * elapsedDays / Math.max(card.stability, 0.1), DECAY);
  if (rating === 1) {
    return Math.max(0.1, FSRS_W[11] * Math.pow(card.difficulty, -FSRS_W[12]) * (Math.pow(card.stability + 1, FSRS_W[13]) - 1) * Math.exp((1 - retrievability) * FSRS_W[14]));
  }

  const hardPenalty = rating === 2 ? FSRS_W[15] : 1;
  const easyBonus = rating === 4 ? FSRS_W[16] : 1;
  return Math.max(0.1, card.stability * (1 + Math.exp(FSRS_W[8]) * (11 - card.difficulty) * Math.pow(card.stability, -FSRS_W[9]) * (Math.exp((1 - retrievability) * FSRS_W[10]) - 1) * hardPenalty * easyBonus));
}

function nextIntervalDays(stability: number, rating: number): number {
  if (rating === 1) {
    return 0.02;
  }

  if (rating === 2) {
    return Math.max(0.25, Math.round(stability * 0.4));
  }

  const interval = stability / FACTOR * (Math.pow(DESIRED_RETENTION, 1 / DECAY) - 1);
  return Math.max(1, Math.round(rating === 4 ? interval * 1.3 : interval));
}

function meanReversion(init: number, current: number): number {
  return FSRS_W[7] * init + (1 - FSRS_W[7]) * current;
}

function normalizeRating(value: unknown): number {
  const rating = Number(value);
  if (![1, 2, 3, 4].includes(rating)) {
    throw new Error(mytoolsI18n.t("Plugin.DeepSeekTranslator.Anki.Error.InvalidRating", {
      defaultValue: "Invalid review rating.",
    }));
  }

  return rating;
}

function constrain(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function hashText(value: string): string {
  let hash = 0;
  for (let index = 0; index < value.length; index++) {
    hash = ((hash << 5) - hash + value.charCodeAt(index)) | 0;
  }

  return Math.abs(hash).toString(36);
}

function asRecord(value: unknown): JsonRecord {
  return typeof value === "object" && value !== null ? value as JsonRecord : {};
}

function numberOrZero(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function integerOrZero(value: unknown): number {
  return typeof value === "number" && Number.isInteger(value) ? value : 0;
}
