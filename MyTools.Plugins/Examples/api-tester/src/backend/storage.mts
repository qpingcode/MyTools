import {mkdir, readFile, writeFile, rename} from 'node:fs/promises';
import path from 'node:path';
import {emptyWorkspace, Limits, type Workspace} from '../shared/model.js';

const WorkspaceFilename = 'workspace.json';

export class WorkspaceStore {
  private writes = Promise.resolve();

  constructor(private directory: string | undefined) {
  }

  private file(): string {
    if (!this.directory) throw new Error('MYTOOLS_PLUGIN_DATA_DIR is not set');
    return path.join(this.directory, WorkspaceFilename);
  }

  async load(): Promise<Workspace> {
    try {
      const value = JSON.parse(await readFile(this.file(), 'utf8'));
      validateWorkspace(value);
      return value;
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') return emptyWorkspace();
      throw error;
    }
  }

  save(workspace: Workspace): Promise<void> {
    validateWorkspace(workspace);
    const snapshot = JSON.stringify(workspace, null, 2);
    const operation = this.writes.catch(() => {}).then(async () => { const file = this.file(); await mkdir(path.dirname(file), { recursive: true }); const temporary = file + '.pending'; await writeFile(temporary, snapshot, 'utf8'); await rename(temporary, file); });
    this.writes = operation; return operation;
  }
}
export function validateWorkspace(value: Workspace): void {
  if (!value || value.version !== Limits.schemaVersion || !Array.isArray(value.collections) || !Array.isArray(value.environments) || !value.defaults) throw new Error('Unsupported workspace format');
  for (const collection of value.collections) { if (!collection.id || !Array.isArray(collection.requests)) throw new Error('Invalid collection'); for (const request of collection.requests) if (!request.id || !request.body || !request.auth || !Array.isArray(request.params) || !Array.isArray(request.headers) || !Array.isArray(request.assertions) || !Array.isArray(request.extractions)) throw new Error('Invalid request'); }
  for (const environment of value.environments) if (!environment.id || !Array.isArray(environment.variables)) throw new Error('Invalid environment');
}
