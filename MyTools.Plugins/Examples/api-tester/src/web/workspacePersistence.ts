import type { Workspace } from '../shared/model.js';

// Retain collection/environment identities: queued UI actions can hold these objects.
function restore(workspace: Workspace, previous: Workspace, identities: Pick<Workspace, 'collections' | 'environments'>): void {
  const collections = previous.collections.map(saved => {
    const current = identities.collections.find(item => item.id === saved.id);
    return current ? Object.assign(current, saved) : saved;
  });
  const environments = previous.environments.map(saved => {
    const current = identities.environments.find(item => item.id === saved.id);
    return current ? Object.assign(current, saved) : saved;
  });
  Object.assign(workspace, previous, { collections, environments });
}

export function createWorkspaceMutator(
  state: { value: Workspace },
  persist: (workspace: Workspace) => Promise<unknown>,
): (change: () => void) => Promise<void> {
  let pending = Promise.resolve();
  return change => {
    const operation = pending.catch(() => {}).then(async () => {
      const previous: Workspace = JSON.parse(JSON.stringify(state.value));
      const identities = { collections: [...state.value.collections], environments: [...state.value.environments] };
      try {
        change();
        await persist(state.value);
      } catch (error) {
        restore(state.value, previous, identities);
        throw error;
      }
    });
    pending = operation;
    return operation;
  };
}
