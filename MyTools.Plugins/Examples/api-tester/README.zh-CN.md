

# API Tester

[English](README.md)

在 MyTools 中调试 HTTP/HTTPS API，保存请求并按顺序执行一轮简单测试。Vue 3 详情页通过 v3 消息总线与独立 Node 后端通信。

## 功能与使用

界面采用左右工作区：在左侧集合树新增、复制、删除和调整请求顺序；悬停或键盘聚焦时显示图标操作。分隔线可拖动，也支持方向键调整、Home/End 和双击复位。URL 下方通过标签切换参数、请求头、认证、请求体、断言、变量提取和设置。顶部管理环境，底部查看响应与运行结果。

从 MyTools 打开 API Tester（别名 `api-tester`），创建集合与请求，配置 URL、参数、请求头、认证及正文后发送。请求需要显式保存，标签上的蓝点表示未保存编辑。支持 Basic/Bearer/API Key 认证、JSON/文本/表单/Multipart/二进制正文和流式文件上传；已保存文件引用须仍可读取。

创建并选择环境，编辑变量，通过 `{{baseUrl}}` 引用。登录关联可将 JSON Pointer `/token` 提取到 `token`，后续 Bearer 认证引用 `{{token}}`。手动标签共享临时变量，切换环境或重启后清空；每次批量运行使用独立临时变量及 Cookie，结束后丢弃。

可添加状态码、时间、响应头、文本、JSON 存在/值/类型断言。勾选部分请求，或不勾选以运行整个当前集合。已打开标签使用当前编辑，不自动保存。支持失败后继续/停止及取消。HTTP 错误码是有效响应，无断言时为未测试。结果只保留在当前会话；不包含报告、历史、脚本、导入导出和多轮迭代。

响应支持原文/JSON 格式化、重复响应头、完整正文复制和保存。默认超时 30 秒，最多十次重定向；预览上限 1 MiB、完整响应上限 20 MiB，八次运行跨正文缓存上限 40 MiB、预览缓存上限 8 MiB，最多十二个标签及每批 1000 个请求。最多保留十六个环境的 Cookie 容器。旧正文可能释放，但摘要仍可查看。界面支持英文、简体中文、实时语言切换、宿主主题及键盘操作。

## 开发

```sh
npm install
npm run check
npm test
npm run test:ui
npm run build
npm run watch
```

使用已发布的 `@qping/plugin-bus@0.9.0` 和 Create Plugin 的 esbuild 脚手架，通过 `@vue/compiler-sfc` 编译组件。`App.vue` 仅组装界面；`useWorkspace`、`useDialogs`、`useRuns` 分别管理工作区、弹窗和执行状态，各配置编辑器独立成组件。保留模板的 TypeScript 7，另用 `typescript-vue` 别名单独安装 TypeScript 5，为 vue-tsc 提供 JavaScript 编译器 API。浏览器冒烟测试使用已安装的 Microsoft Edge 无头模式。本地 HTTP 集成测试输出到 `bin/AgentVerification/`。

宿主须设置 `MYTOOLS_PLUGIN_DATA_DIR`，插件在其中原子保存 `workspace.json`。配置值按输入保存，本版不考虑凭据脱敏或安全存储。

Desktop 通过 `window.mytoolsBeforeClose()` 在窗口关闭和正常托盘退出时提供保存/放弃/取消提示，使用此行为需重新构建宿主；没有此钩子的插件保持原有行为。

## 发布

明确需要发布时先构建，再运行 `npm run publish:hub`。脚本来自 Create Plugin 模板，需要登录 Hub；后续发布前同步递增 manifest 与 package 版本。本次实现未发布插件。

