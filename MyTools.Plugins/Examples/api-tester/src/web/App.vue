<script setup lang="ts">
import { provide } from 'vue';
import { useWorkspace } from './useWorkspace.js';
import { WorkspaceKey } from './context.js';
import Sidebar from './Sidebar.vue';
import SplitPane from './SplitPane.vue';
import RequestResponseSplit from './RequestResponseSplit.vue';
import EnvironmentBar from './EnvironmentBar.vue';
import RequestTabs from './RequestTabs.vue';
import RequestEditor from './RequestEditor.vue';
import ResponsePanel from './ResponsePanel.vue';
import RunResults from './RunResults.vue';
import WorkspaceDialogs from './WorkspaceDialogs.vue';
import Icon from './Icon.vue';
import IconButton from './IconButton.vue';
import { notification } from './rpc.js';
const controller = useWorkspace();
provide(WorkspaceKey, controller);
const { t, workspaceReady, tab, notificationText } = controller;
</script>
<template>
  <main class="app-shell" :inert="!workspaceReady" :aria-busy="!workspaceReady">
    <header class="app-header">
      <div class="brand">
        <span class="brand-icon"><Icon name="terminal" /></span>
        <h1>{{ t.Name() }}</h1>
      </div>
      <EnvironmentBar />
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
          <section class="response">
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
              <p>{{ t.NoResults() }}</p>
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
