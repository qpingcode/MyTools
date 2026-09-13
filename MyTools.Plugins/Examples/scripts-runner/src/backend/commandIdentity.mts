import { randomUUID } from "node:crypto";

/** Assign identities before filtering; a hidden schema field preserves them on edits. */
export function ensureCommandIds(commands: { id?: string }[]): boolean {
  const seen = new Set<string>();
  let changed = false;
  for (const command of commands) {
    if (typeof command.id !== "string" || !command.id.trim() || seen.has(command.id)) {
      command.id = randomUUID();
      changed = true;
    }
    seen.add(command.id);
  }
  return changed;
}
