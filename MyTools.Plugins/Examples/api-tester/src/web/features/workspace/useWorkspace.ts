import { DialogKind, Choice, type Tab } from './workspaceTypes.js';
import { useDialogs } from './useDialogs.js';
import { useRuns } from '../runs/useRuns.js';
import { createWorkspaceMutator } from './workspacePersistence.js';

import { computed, ref } from 'vue';
import { HostEvents } from '@qping/plugin-bus/web';
import { bus } from '../../localization/i18n.js';
import { useText } from '../../localization/locale.js';
import { rpc, notification, errorText } from '../../services/rpc.js';
import {
  Routes,
  HttpMethod,
  Limits,
  BodyKind,
  AuthKind,
  KeyLocation,
  AssertionKind,
  ExecutionState,
  TestState,
  ErrorKind,
  emptyWorkspace,
  newRequest,
  parseQuery,
  queryUrl,
  type Workspace,
  type ApiRequest,
  type Collection,
  type Environment,
  type Settings,
  type CookieRecord,
} from '../../../shared/model.js';

export function useWorkspace() {
  const DefaultExpectedStatus = '200';
  const uid = () => crypto.randomUUID();
  const HttpMethods = Object.values(HttpMethod);
  const t = useText();
  const workspace = ref<Workspace>(emptyWorkspace());
  const workspaceReady = ref(false);
  const loadingWorkspace = ref(false);
  const workspaceLoadFailed = ref(false);
  const tabs = ref<Tab[]>([]);
  const active = ref('');
  const collectionId = ref('');
  const search = ref('');
  const selected = ref(new Set<string>());
  const tab = computed(() =>
    tabs.value.find((item) => item.request.id === active.value),
  );
  const collection = computed(() =>
    workspace.value.collections.find((item) => item.id === collectionId.value),
  );
  const environment = computed(() =>
    workspace.value.environments.find(
      (item) => item.id === workspace.value.environmentId,
    ),
  );
  const cookies = ref<CookieRecord[]>([]);
  const { modal, choose, name, confirm, finishModal } = useDialogs();
  const {
    batch,
    batchId,
    expandedResults,
    stopOnFailure,
    startingBatch,
    runInput,
    monitor,
    send,
    runBatch,
    executionCounts,
    testCounts,
    assertionCounts,
  } = useRuns(workspace, tabs, collection, environment, selected);
  const notificationText = computed(() => {
    t.value;
    return typeof notification.value === 'string'
      ? notification.value
      : errorText(notification.value);
  });
  const authOptions = computed(() => [
    [AuthKind.Inherit, t.value.InheritAuth()],
    [AuthKind.None, t.value.None()],
    [AuthKind.Basic, t.value.Basic()],
    [AuthKind.Bearer, t.value.Bearer()],
    [AuthKind.ApiKey, t.value.ApiKey()],
  ]);
  const bodyOptions = computed(() => [
    [BodyKind.None, t.value.None()],
    [BodyKind.Json, t.value.Json()],
    [BodyKind.Text, t.value.Text()],
    [BodyKind.Form, t.value.Form()],
    [BodyKind.Multipart, t.value.Multipart()],
    [BodyKind.Binary, t.value.Binary()],
  ]);
  const assertionOptions = computed(() => [
    [AssertionKind.Status, t.value.Status()],
    [AssertionKind.Time, t.value.Time()],
    [AssertionKind.Header, t.value.HeaderExists()],
    [AssertionKind.Text, t.value.Contains()],
    [AssertionKind.Exists, t.value.JsonExists()],
    [AssertionKind.Value, t.value.JsonValue()],
    [AssertionKind.Type, t.value.JsonType()],
  ]);
  const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value));
  const dirty = (item: Tab) => JSON.stringify(item.request) !== item.baseline;

  function execution(state: ExecutionState): string {
    return {
      [ExecutionState.Complete]: t.value.Complete,
      [ExecutionState.Failed]: t.value.Failed,
      [ExecutionState.Cancelled]: t.value.Cancelled,
      [ExecutionState.Skipped]: t.value.Skipped,
    }[state]();
  }

  function test(state: TestState): string {
    return {
      [TestState.Passed]: t.value.Passed,
      [TestState.Failed]: t.value.TestFailed,
      [TestState.Untested]: t.value.Untested,
      [TestState.NotApplicable]: t.value.NotApplicable,
    }[state]();
  }

  const mutate = createWorkspaceMutator(workspace, value => rpc(Routes.save, value));

  async function addCollection() {
    const value = await name(t.value.NewCollection());
    if (value) {
      const id = crypto.randomUUID();
      await mutate(() =>
        workspace.value.collections.push({ id, name: value, requests: [] }),
      );
      collectionId.value = id;
    }
  }

  async function renameCollection(owner: Collection) {
    const value = await name(owner.name);
    if (value) await mutate(() => (owner.name = value));
  }

  async function duplicateCollection(owner: Collection) {
    const value = await name(owner.name);
    if (value)
      await mutate(() =>
        workspace.value.collections.push({
          ...clone(owner),
          id: crypto.randomUUID(),
          name: value,
          requests: owner.requests.map((request) => ({
            ...clone(request),
            id: crypto.randomUUID(),
          })),
        }),
      );
  }

  async function deleteCollection(owner: Collection) {
    if (
      !(await confirm(() =>
        t.value.DeleteCollection({ count: owner.requests.length }),
      ))
    )
      return;
    for (const item of [...tabs.value].filter(
      (item) => item.collectionId === owner.id,
    ))
      if (!(await closeTab(item))) return;
    await mutate(
      () =>
        (workspace.value.collections = workspace.value.collections.filter(
          (item) => item.id !== owner.id,
        )),
    );
    collectionId.value = workspace.value.collections[0]?.id || '';
    selected.value.clear();
  }

  function open(request: ApiRequest, owner = '') {
    if (!tabs.value.some((item) => item.request.id === request.id)) {
      tabs.value.push({
        request: clone(request),
        collectionId: owner,
        baseline: owner ? JSON.stringify(request) : '',
        runId: '',
        running: false,
      });
    }
    active.value = request.id;
    if (owner) collectionId.value = owner;
  }

  async function saveTab(item: Tab): Promise<boolean> {
    let owner = workspace.value.collections.find(
      (owner) => owner.id === item.collectionId,
    );
    if (!owner) {
      if (!workspace.value.collections.length) await addCollection();
      if (!workspace.value.collections.length) return false;
      const id = await choose<string | null>(
        DialogKind.Collection,
        t.value.SaveTarget,
        collectionId.value || workspace.value.collections[0].id,
      );
      if (!id) return false;
      owner = workspace.value.collections.find((owner) => owner.id === id);
    }
    if (!owner) return false;
    const target = owner;
    const saved = clone(item.request);
    await mutate(() => {
      const index = target.requests.findIndex(
        (request) => request.id === item.request.id,
      );
      if (index < 0) target.requests.push(saved);
      else target.requests[index] = saved;
    });
    item.collectionId = target.id;
    item.baseline = JSON.stringify(saved);
    collectionId.value = target.id;
    return true;
  }

  async function closeTab(item: Tab): Promise<boolean> {
    if (dirty(item)) {
      const choice = await choose<Choice>(DialogKind.Unsaved, () =>
        t.value.Unsaved({ name: item.request.name }),
      );
      if (
        choice === Choice.Cancel ||
        (choice === Choice.Save && !(await saveTab(item)))
      )
        return false;
    }
    if (item.runId) await rpc(Routes.release, { id: item.runId });
    tabs.value = tabs.value.filter((tab) => tab.request.id !== item.request.id);
    if (active.value === item.request.id)
      active.value = tabs.value.at(-1)?.request.id || '';
    return true;
  }

  async function deleteRequest(item: Tab) {
    if (
      !(await confirm(() => t.value.DeleteItem({ name: item.request.name }))) ||
      !(await closeTab(item))
    )
      return;
    await mutate(() => {
      for (const owner of workspace.value.collections)
        owner.requests = owner.requests.filter(
          (request) => request.id !== item.request.id,
        );
    });
    selected.value.delete(item.request.id);
  }

  async function move(item: Tab, offset: number) {
    const owner = workspace.value.collections.find(
      (owner) => owner.id === item.collectionId,
    );
    if (!owner) return;
    const index = owner.requests.findIndex(
      (request) => request.id === item.request.id,
    );
    const target = index + offset;
    if (index < 0 || target < 0 || target >= owner.requests.length) return;
    await mutate(() => {
      [owner.requests[index], owner.requests[target]] = [
        owner.requests[target],
        owner.requests[index],
      ];
    });
  }

  async function addEnvironment() {
    const draft: Environment = { id: uid(), name: '', variables: [] };
    if (await choose(DialogKind.Environment, t.value.NewEnvironment, '', { environment: draft })) {
      draft.name = draft.name.trim();
      await mutate(() => workspace.value.environments.push(draft));
    }
  }

  async function refreshCookies() {
    cookies.value = await rpc<CookieRecord[]>(Routes.listCookies);
  }

  async function clearCookies() {
    await rpc(Routes.cookies);
    await refreshCookies();
  }

  async function sendRequest(item: Tab) {
    try {
      await send(item);
    } finally {
      await refreshCookies();
    }
  }

  async function switchEnvironment(id: string) {
    await rpc(Routes.environment, { id });
    await mutate(() => (workspace.value.environmentId = id));
    await refreshCookies();
  }

  async function editEnvironment() {
    if (!environment.value) return;
    const copy = clone(environment.value);
    if (
      await choose(DialogKind.Environment, t.value.EditEnvironment, '', {
        environment: copy,
      })
    ) {
      copy.name = copy.name.trim();
      await mutate(
        () =>
          (workspace.value.environments[
            workspace.value.environments.findIndex(
              (item) => item.id === copy.id,
            )
          ] = copy),
      );
    }
  }

  async function duplicateEnvironment() {
    const owner = environment.value;
    if (!owner) return;
    const value = await name(owner.name);
    if (value)
      await mutate(() =>
        workspace.value.environments.push({
          ...clone(owner),
          id: crypto.randomUUID(),
          name: value,
        }),
      );
  }

  async function deleteEnvironment() {
    const owner = environment.value;
    if (
      !owner ||
      !(await confirm(() => t.value.DeleteItem({ name: owner.name })))
    )
      return;
    await rpc(Routes.environment, { id: '' });
    await mutate(() => {
      workspace.value.environments = workspace.value.environments.filter(
        (item) => item.id !== owner.id,
      );
      workspace.value.environmentId = '';
    });
    await refreshCookies();
  }

  async function defaults() {
    const copy = clone(workspace.value.defaults);
    if (
      await choose(DialogKind.Defaults, t.value.Defaults, '', {
        settings: copy,
      })
    )
      await mutate(() => (workspace.value.defaults = copy));
  }

  async function changeUrl(event: Event, item: Tab) {
    const value = (event.target as HTMLInputElement).value;
    if (
      item.request.params.some((pair) => !pair.enabled) &&
      !(await confirm(() => t.value.ReplaceParams()))
    ) {
      (event.target as HTMLInputElement).value = item.request.url;
      return;
    }
    try {
      const parsed = parseQuery(value);
      item.request.url = value;
      item.request.params = parsed;
    } catch {
      notification.value = { kind: ErrorKind.Configuration, field: 'url' };
      (event.target as HTMLInputElement).value = item.request.url;
    }
  }

  function changeParams(item: Tab) {
    item.request.url = queryUrl(item.request.url, item.request.params);
  }

  function formatBody(item: Tab) {
    try {
      item.request.body.text = JSON.stringify(
        JSON.parse(item.request.body.text),
        null,
        2,
      );
    } catch {
      notification.value = t.value.BadJson();
    }
  }

  async function pickBinary(item: Tab) {
    const file = await rpc<{
      path: string;
      size: number;
      contentType: string;
    } | null>(Routes.file);
    if (file) {
      item.request.body.file = file.path;
      item.request.body.contentType = file.contentType;
      item.request.body.fileSize = file.size;
    }
  }

  function override(item: Tab, event: Event) {
    item.request.settings = (event.target as HTMLInputElement).checked
      ? clone(workspace.value.defaults)
      : undefined;
  }

  function filtered(owner: Collection) {
    return owner.requests.filter((request) =>
      (request.name + ' ' + request.url)
        .toLowerCase()
        .includes(search.value.toLowerCase()),
    );
  }

  function selectCollection(id: string) {
    collectionId.value = id;
    selected.value.clear();
  }

  let closing = false;

  async function beforeClose(): Promise<boolean> {
    if (closing) return false;
    closing = true;
    try {
      for (const item of tabs.value) {
        if (!dirty(item)) continue;
        const choice = await choose<Choice>(DialogKind.Unsaved, () =>
          t.value.Unsaved({ name: item.request.name }),
        );
        if (
          choice === Choice.Cancel ||
          (choice === Choice.Save && !(await saveTab(item)))
        )
          return false;
      }
      for (const item of tabs.value) {
        item.baseline = JSON.stringify(item.request);
        if (item.runId) await rpc(Routes.release, { id: item.runId });
      }
      if (batchId.value) {
        const id = batchId.value;
        batchId.value = '';
        await rpc(Routes.release, { id });
      }
      return true;
    } finally {
      closing = false;
    }
  }

  (
    window as unknown as { mytoolsBeforeClose: () => Promise<boolean> }
  ).mytoolsBeforeClose = beforeClose;
  window.addEventListener('beforeunload', (event) => {
    if (tabs.value.some(dirty)) {
      event.preventDefault();
      event.returnValue = '';
    }
  });
  document.addEventListener('keydown', (event) => {
    if (event.ctrlKey && event.key.toLowerCase() === 's') {
      event.preventDefault();
      if (tab.value) void saveTab(tab.value).catch(console.error);
    }
  });
  async function loadWorkspace() {
    if (workspaceReady.value || loadingWorkspace.value) return;
    loadingWorkspace.value = true;
    workspaceLoadFailed.value = false;
    try {
      const value = await rpc<Workspace>(Routes.load);
      workspace.value = value;
      collectionId.value = value.collections[0]?.id || '';
      workspaceReady.value = true;
      notification.value = '';
    } catch (error) {
      workspaceLoadFailed.value = true;
      console.error(error);
    } finally {
      loadingWorkspace.value = false;
    }
    if (workspaceReady.value) await refreshCookies().catch(console.error);
  }
  bus.on(HostEvents.Initialize, () => { void loadWorkspace(); });
  bus.on(HostEvents.LanguageChanged, () => {
    if (typeof notification.value === 'string') notification.value = '';
  });

  return {
    DefaultExpectedStatus,
    HttpMethods,
    t,
    workspace,
    workspaceReady,
    loadingWorkspace,
    workspaceLoadFailed,
    loadWorkspace,
    tabs,
    active,
    collectionId,
    search,
    selected,
    tab,
    collection,
    environment,
    cookies,
    refreshCookies,
    clearCookies,
    modal,
    batch,
    batchId,
    expandedResults,
    stopOnFailure,
    startingBatch,
    notificationText,
    authOptions,
    bodyOptions,
    assertionOptions,
    dirty,
    executionCounts,
    testCounts,
    assertionCounts,
    execution,
    test,
    name,
    confirm,
    mutate,
    finishModal,
    addCollection,
    renameCollection,
    duplicateCollection,
    deleteCollection,
    open,
    saveTab,
    closeTab,
    deleteRequest,
    move,
    addEnvironment,
    switchEnvironment,
    editEnvironment,
    duplicateEnvironment,
    deleteEnvironment,
    defaults,
    changeUrl,
    changeParams,
    formatBody,
    pickBinary,
    override,
    runInput,
    monitor,
    send: sendRequest,
    runBatch,
    filtered,
    selectCollection,
    beforeClose,
    uid,
    clone,
    DialogKind,
    Choice,
    Routes,
    HttpMethod,
    BodyKind,
    AuthKind,
    KeyLocation,
    AssertionKind,
    ExecutionState,
    TestState,
  };
}
