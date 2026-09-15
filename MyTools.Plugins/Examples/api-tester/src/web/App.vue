<script setup lang="ts">
import { provide } from 'vue';
import { useWorkspace } from './features/workspace/useWorkspace.js';
import { WorkspaceKey } from './features/workspace/context.js';
import Sidebar from './features/sidebar/Sidebar.vue';
import SplitPane from './components/layout/SplitPane.vue';
import RequestResponseSplit from './components/layout/RequestResponseSplit.vue';
import EnvironmentBar from './features/workspace/EnvironmentBar.vue';
import RequestTabs from './features/request/RequestTabs.vue';
import RequestEditor from './features/request/RequestEditor.vue';
import ResponsePanel from './features/response/ResponsePanel.vue';
import RunResults from './features/runs/RunResults.vue';
import WorkspaceDialogs from './features/workspace/WorkspaceDialogs.vue';
import Icon from './components/common/Icon.vue';
import IconButton from './components/common/IconButton.vue';
import { notification } from './services/rpc.js';
import WorkspaceTools from './features/workspace/WorkspaceTools.vue';
import { WorkspaceTool } from './features/workspace/workspaceToolTypes.js';
const controller = useWorkspace();
provide(WorkspaceKey, controller);
const { t, workspaceReady, workspaceLoadFailed, loadWorkspace, tab, notificationText } = controller;
</script>
<template>
  <div v-if="!workspaceReady" class="workspace-overlay" role="status" aria-live="polite">
    <div class="workspace-overlay-card">
      <span v-if="!workspaceLoadFailed" class="workspace-loading-spinner" aria-hidden="true" />
      <p>{{ workspaceLoadFailed ? t.WorkspaceLoadFailed() : t.WorkspaceLoading() }}</p>
      <button v-if="workspaceLoadFailed" class="primary" @click="loadWorkspace">{{ t.Retry() }}</button>
    </div>
  </div>
  <main class="app-shell" :inert="!workspaceReady" :aria-busy="!workspaceReady">
    <header class="app-header">
      <div class="header-actions"><WorkspaceTools :tool="WorkspaceTool.Import" /><WorkspaceTools :tool="WorkspaceTool.Export" /><WorkspaceTools :tool="WorkspaceTool.History" /><EnvironmentBar /></div>
    </header>
    <SplitPane
      ><template #sidebar><Sidebar /></template>
      <section class="workspace">
        <RequestTabs />
        <RequestResponseSplit>
          <template #request>
          <RequestEditor v-if="tab" />
          <div v-else class="empty-state">
            <Icon name="terminal" />
            <h2>{{ t.Ready() }}</h2>
            <p>{{ t.Subtitle() }}</p>
          </div>
          </template>
          <section class="response" :aria-busy="!!tab?.running">
            <div
              v-if="tab?.running"
              class="response-loading"
              role="progressbar"
              :aria-label="t.Running({ name: tab.request.name })"
            ><span /></div>
            <div class="section-heading">
              <h2>{{ t.Response() }}</h2>
              <span v-if="tab?.running" class="running-indicator">{{
                t.Running({ name: tab.request.name })
              }}</span>
            </div>
            <ResponsePanel
              v-if="tab?.result"
              :key="tab.runId"
              :result="tab.result"
              :run-id="tab.runId"
              :index="0"
            />
            <div v-else class="empty-response">
              <Icon name="inbox" />
              <p>{{ tab?.running ? t.Running({ name: tab.request.name }) : t.NoResults() }}</p>
            </div>
          </section>
          <RunResults />
        </RequestResponseSplit></section
    ></SplitPane>
  </main>
  <WorkspaceDialogs />
  <div v-if="notificationText" id="toast" role="status" aria-live="polite">
    {{ notificationText
    }}<IconButton icon="close" :label="t.Close()" @click="notification = ''" />
  </div>
</template>
