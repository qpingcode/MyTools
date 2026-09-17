import {mkdir, readFile, writeFile, rename} from 'node:fs/promises';
import path from 'node:path';
import {emptyWorkspace, Limits, type HistoryEntry, type Workspace} from '../../shared/model.js';

import {validateWorkspace} from '../../shared/workspaceValidation.js';

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
        const operation = this.writes.catch(() => {
        }).then(async () => {
            const file = this.file();
            await mkdir(path.dirname(file), {recursive: true});
            const temporary = file + '.pending';
            await writeFile(temporary, snapshot, 'utf8');
            await rename(temporary, file);
        });
        this.writes = operation;
        return operation;
    }
}

export {validateWorkspace} from '../../shared/workspaceValidation.js';

const HistoryFilename = 'history.json';

export class HistoryStore {
    private writes = Promise.resolve();

    constructor(private directory: string | undefined) {
    }

    async list(): Promise<HistoryEntry[]> {
        await this.writes.catch(() => {
        });
        return this.read();
    }

    private async read(): Promise<HistoryEntry[]> {
        if (!this.directory) throw new Error('MYTOOLS_PLUGIN_DATA_DIR is not set');
        try {
            return JSON.parse(await readFile(path.join(this.directory, HistoryFilename), 'utf8'));
        } catch (error) {
            if ((error as NodeJS.ErrnoException).code === 'ENOENT') return [];
            throw error;
        }
    }

    private update(change: (entries: HistoryEntry[]) => HistoryEntry[]): Promise<void> {
        const operation = this.writes.catch(() => {
        }).then(async () => {
            const entries = change(await this.read());
            const file = path.join(this.directory!, HistoryFilename);
            await mkdir(this.directory!, {recursive: true});
            await writeFile(file + '.pending', JSON.stringify(entries), 'utf8');
            await rename(file + '.pending', file);
        });
        this.writes = operation;
        return operation;
    }

    append(entry: HistoryEntry): Promise<void> {
        const snapshot = structuredClone(entry);
        snapshot.result.preview = Buffer.from(snapshot.result.preview).subarray(0, Limits.historyPreviewBytes).toString('utf8');
        snapshot.result.truncated ||= snapshot.result.size > Limits.historyPreviewBytes;
        snapshot.result.bodyAvailable = false;
        delete snapshot.result.sentRequest;
        return this.update(entries => [snapshot, ...entries].slice(0, Limits.historyEntries));
    }

    clear(requestId?: string): Promise<void> {
        return this.update(entries => requestId
            ? entries.filter(entry => entry.request.id !== requestId)
            : []);
    }
}
