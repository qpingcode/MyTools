import { inject, type InjectionKey } from 'vue';
import type { useWorkspace } from './useWorkspace.js';
export const WorkspaceKey: InjectionKey<ReturnType<typeof useWorkspace>> =
  Symbol('api-tester-workspace');
export function useWorkspaceContext() {
  const workspace = inject(WorkspaceKey);
  if (!workspace) throw new Error('API Tester workspace was not provided');
  return workspace;
}
