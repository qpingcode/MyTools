import {createPlugin} from '@qping/plugin-bus/node';
import {mytoolsI18n} from '@qping/plugin-bus/i18n';
import {stat} from 'node:fs/promises';
import path from 'node:path';
import {ContentType, Routes, ErrorKind, type Workspace} from '../shared/model.js';
import {WorkspaceStore, HistoryStore} from './persistence/storage.mjs';
import {Runner, type RunInput} from './execution/runner.mjs';
import {RequestError} from './execution/engine.mjs';

const FilePickCapability = 'path.pick';
const SearchItemId = 'api-tester-workspace';
const plugin = createPlugin();
const store = new WorkspaceStore(process.env.MYTOOLS_PLUGIN_DATA_DIR);
const history = new HistoryStore(process.env.MYTOOLS_PLUGIN_DATA_DIR);
const runner = new Runner(entry => history.append(entry));
const guarded = <T, R>(handler: (payload: T) => Promise<R> | R) => async (payload: T) => {
    try {
        return {ok: true, value: await handler(payload)};
    } catch (error) {
        return {
            ok: false,
            error: {
                kind: error instanceof RequestError ? error.kind : ErrorKind.Storage,
                field: error instanceof RequestError ? error.field : '',
                detail: (error as Error).message
            }
        };
    }
};
plugin.initialize(params => {
    mytoolsI18n.configure(params);
    return {};
})
    .search(params => ({
        items: [{
            id: SearchItemId,
            title: mytoolsI18n.t('Plugin.ApiTester.Name', {defaultValue: 'API Tester'}),
            subtitle: mytoolsI18n.t('Plugin.ApiTester.Subtitle', {defaultValue: 'Debug HTTP requests and run simple API tests'}),
            icon: {kind: 'mdi', value: 'mdi-api'},
            actions: [],
            detail: {
                type: 'web-detail',
                htmlEntry: 'web/index.html',
                title: mytoolsI18n.t('Plugin.ApiTester.Name', {defaultValue: 'API Tester'}),
                initialState: {query: params.query}
            }
        }]
    }))
    .handle(Routes.load, guarded(async () => {
        const workspace = await store.load();
        runner.switchEnvironment(workspace.environmentId);
        return workspace;
    }))
    .handle(Routes.save, guarded(async (workspace: Workspace) => {
        await store.save(workspace);
        return true;
    }))
    .handle(Routes.history, guarded(() => history.list()))
    .handle(Routes.curl, guarded((input: Parameters<Runner['curl']>[0]) => runner.curl(input)))
    .handle(Routes.clearHistory, guarded(() => history.clear()))
    .handle(Routes.start, guarded((input: RunInput) => runner.start(input)))
    .handle(Routes.poll, guarded((input: { id: string; from?: number }) => {
        const view = runner.poll(input.id);
        return { ...view, results: view.results.slice(input.from || 0).map(({ sentRequest, ...result }) => result), evictedBodies: view.results.flatMap((result, index) => !result.bodyAvailable ? [index] : []), evictedPreviews: view.results.flatMap((result, index) => result.previewAvailable === false ? [index] : []) };
    }))
    .handle(Routes.cancel, guarded((input: { id: string }) => runner.cancel(input.id)))
    .handle(Routes.release, guarded((input: { id: string }) => runner.release(input.id)))
    .handle(Routes.environment, guarded((input: { id: string }) => runner.switchEnvironment(input.id)))
    .handle(Routes.cookies, guarded(() => runner.clearCookies()))
    .handle(Routes.listCookies, guarded(() => runner.listCookies()))
    .handle(Routes.download, guarded((input: { id: string; index: number }) => runner.download(input.id, input.index)))
    .handle(Routes.file, guarded(async () => {
        const picked = await plugin.hostCall(FilePickCapability, {
            kind: 'file',
            filter: mytoolsI18n.t('Plugin.ApiTester.FileFilter', {defaultValue: 'All files (*.*)|*.*'}),
            title: mytoolsI18n.t('Plugin.ApiTester.SelectFile', {defaultValue: 'Select a file'})
        }) as { cancelled: boolean; path: string };
        if (picked.cancelled || !picked.path) return null;
        const info = await stat(picked.path);
        if (!info.isFile()) throw new RequestError(ErrorKind.File, picked.path);
        return {
            path: picked.path,
            name: path.basename(picked.path),
            size: info.size,
            contentType: ContentType.Binary
        };
    }))
    .start();

