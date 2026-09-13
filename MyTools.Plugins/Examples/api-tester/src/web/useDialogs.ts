import { ref } from 'vue';
import { DialogKind, Choice, type Modal } from './workspaceTypes.js';
import { ErrorKind } from '../shared/model.js';
import { notification } from './rpc.js';
import { useText } from './locale.js';
export function useDialogs() {
  const t = useText();
  const modal = ref<Modal | null>(null);
  function choose<T>(
    kind: DialogKind,
    title: () => string,
    value = '',
    extra: Partial<Modal> = {},
  ): Promise<T> {
    return new Promise((resolve) => {
      modal.value = {
        kind,
        title,
        value,
        ...extra,
        finish: (result) => {
          modal.value = null;
          resolve(result as T);
        },
      };
    });
  }

  async function name(initial: string): Promise<string | null> {
    return choose(DialogKind.Name, t.value.NamePrompt, initial);
  }

  async function confirm(title: () => string): Promise<boolean> {
    return choose(DialogKind.Confirm, title);
  }

  function finishModal(save: boolean, discard = false) {
    const value = modal.value;
    if (!value) return;
    if (value.kind === DialogKind.Unsaved) {
      value.finish(
        discard ? Choice.Discard : save ? Choice.Save : Choice.Cancel,
      );
      return;
    }
    if (!save) {
      value.finish(value.kind === DialogKind.Confirm ? false : null);
      return;
    }
    if (value.kind === DialogKind.Name && !value.value.trim()) return;
    if (
      value.kind === DialogKind.Defaults &&
      (!Number.isFinite(value.settings?.timeoutMs) ||
        value.settings!.timeoutMs <= 0)
    ) {
      notification.value = {
        kind: ErrorKind.Configuration,
        field: 'timeoutMs',
      };
      return;
    }
    value.finish(
      value.kind === DialogKind.Name || value.kind === DialogKind.Collection
        ? value.value.trim()
        : true,
    );
  }

  return { modal, choose, name, confirm, finishModal };
}
