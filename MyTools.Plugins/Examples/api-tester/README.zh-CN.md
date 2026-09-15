

# API Tester

[English](README.md)

在 MyTools 中调试 HTTP/HTTPS API，保存请求并按顺序执行一轮简单测试。Vue 3 详情页通过 v3 消息总线与独立 Node 后端通信。

## 功能与使用

界面采用左右工作区：在左侧集合树新增请求；请求列表通过右键菜单或 Shift+F10 上移、下移、复制和删除，集合通过右键菜单或 Shift+F10 打开集合设置、重命名、复制和删除。右键菜单支持方向键导航和 Escape 关闭。分隔线可拖动，也支持方向键调整、Home/End 和双击复位。URL 下方通过标签切换参数、请求头、认证、请求体、断言、变量提取和设置。顶部管理环境，底部查看响应与运行结果。

从 MyTools 打开 API Tester（别名 `api-tester`），创建集合与请求，配置 URL、参数、请求头、认证及正文后发送。请求需要显式保存，标签上的蓝点表示未保存编辑。支持 Basic/Bearer/API Key 认证、JSON/文本/表单/Multipart/二进制正文和流式文件上传；已保存文件引用须仍可读取。

创建并选择环境，编辑变量，通过 `{{baseUrl}}` 引用。登录关联可将 JSON Pointer `/token` 提取到 `token`，后续 Bearer 认证引用 `{{token}}`。手动标签共享临时变量，切换环境或重启后清空；每次批量运行使用独立临时变量及 Cookie，结束后丢弃。

可添加状态码、时间、响应头、文本、JSON 存在/值/类型断言。勾选部分请求，或不勾选以运行整个当前集合。已打开标签使用当前编辑，不自动保存。支持失败后继续/停止及取消。HTTP 错误码是有效响应，内置成功检查要求 HTTP 200。暂不包含报告和多轮迭代。

响应支持原文/JSON 格式化/树视图、语法配色、折叠、不区分大小写的搜索和匹配跳转、重复响应头、完整正文复制和保存。默认超时 30 秒，最多十次重定向；预览上限 1 MiB、完整响应上限 20 MiB，八次运行跨正文缓存上限 40 MiB、预览缓存上限 8 MiB，每批最多 1000 个请求。最多保留十六个环境的 Cookie 容器。旧正文可能释放，但摘要仍可查看。界面支持英文、简体中文、实时语言切换、宿主主题及键盘操作。

## 导入、导出与历史

顶部提供带图标的“导入”“导出”“历史记录”三个独立按钮，分别打开各自的弹出框。可以粘贴浏览器 cURL，或者导入 API Tester JSON、Postman Collection v2.x、Postman Environment JSON。导入会生成新 ID 并追加数据，不覆盖已有集合；导入脚本默认禁用，需手动启用。不支持的 cURL 选项、认证或正文模式会使导入失败，避免悄悄改变请求语义。

支持导出整个工作区、单个集合或单个环境；集合也可导出为 Postman v2.1。原生 JSON 保留断言、提取规则、脚本和请求设置；Postman 导出不包含本插件的断言、提取规则及请求设置。导出使用已保存的数据，请先保存标签页编辑。

请求保存按钮旁的复制图标可复制 POSIX Shell 格式 cURL，会解析集合配置、当前环境及会话变量，但不会执行请求前脚本。支持常见浏览器参数、引号正文、重复及原始编码查询参数、表单、Multipart 文件和二进制文件路径。文件路径仍是引用，只在发送时读取文件。

历史原子保存在工作区旁的 `history.json`，保留最近 100 次实际请求、环境/变量快照、请求及响应头、测试结果、日志和每次最多 16 KiB 的响应预览，不保留完整响应体。重新打开已发送的请求时使用当时实际发送的值，并禁用脚本，避免重复修改请求；发送失败的记录可能保留原脚本配置。历史和导出可能包含凭据，与保存请求一致。

## 集合公共配置与断言

在集合操作中点击设置图标，可以编辑公共 Headers、认证和脚本。请求需要选择“继承集合认证”才能使用集合认证；选择“无”会显式禁用认证。请求 Headers 按名称不区分大小写覆盖公共 Headers，包括禁用的条目。手动发送和批量执行都应用公共配置。

两个阶段均先执行集合脚本，再执行请求脚本；两者拥有独立的局部变量作用域，共享会话变量。

请求的“断言”页签支持状态码、最大响应时间（毫秒）、响应头存在、正文包含、JSON Pointer 存在、JSON 值及类型检查。路径使用 `/data/id`，JSON 预期值按 JSON 语法填写。结果与脚本测试统一显示在响应的断言页签。

## Scripts 脚本

“脚本”页签分别编辑“请求前”和“响应后”的 JavaScript，支持顶层 `await`。脚本在独立 Worker 内的 VM 执行，限时 2 秒，老生代堆上限 64 MiB。VM 仅接收 JSON 数据，不提供 Node 对象、文件/网络 API、npm 导入或动态代码生成。它是有限制的本地脚本环境，不完整兼容 Postman Sandbox。脚本返回的请求/响应状态上限 2 MiB，日志和测试数量也有限制。

支持以下 API：

- `pm.variables.get/set/unset/has/toObject/replaceIn`；`replaceIn` 还支持 `{{$timestamp}}`、`{{$randomInt}}`。
- `pm.environment`、`pm.collectionVariables` 是会话变量的别名，不修改已保存的环境值。手动请求共享变量，切换环境或重启后清空；批量运行隔离变量与 Cookie。
- `pm.request.method/url`、`pm.request.headers.get/has/add/upsert/remove/toObject`、用于原始正文的 `pm.request.body.raw/update`。
- 响应后的 `pm.response.code/status/responseTime/headers`、`pm.response.text()/json()`、`pm.response.to.have.status(code)`。
- `pm.test(name, callback)`（回调必须同步）、`pm.expect(value)`，支持 `equal/eql/deep.equal/include/above/below/within/a/an/property/match/lengthOf/not/ok/true/false/null/undefined`。
- `pm.crypto.sha256(text)`、`pm.crypto.hmacSha256(text, secret)` 返回十六进制；`pm.crypto.base64(text)` 使用 UTF-8 编码。
- `console.log/info/warn/error/debug`，输出显示在“脚本控制台”。

```js
// 请求前
pm.variables.set('timestamp', String(Date.now()));
const signature = pm.crypto.hmacSha256(pm.variables.get('timestamp'), pm.variables.get('secret'));
pm.request.headers.upsert({ key: 'X-Signature', value: signature });

// 响应后
pm.test('HTTP 200', () => pm.response.to.have.status(200));
pm.test('token', () => pm.expect(pm.response.json().token).to.be.a('string'));
pm.variables.set('token', pm.response.json().token);
```

请求前脚本异常会阻止发送；响应后脚本异常保留收到的响应，并标记运行失败。只有执行、解码、提取及脚本均成功时才提交变量修改；测试断言失败会参与批量执行的失败后停止判断。

## 开发

源码按职责归类：

- `src/backend/execution`、`scripting`、`persistence`：分别负责 HTTP 请求与运行、脚本 worker 与加密辅助函数、工作区与历史存储；`index.mts` 保留为插件入口。
- `src/shared`：前后端共用的领域模型、工作区校验与导入导出格式。
- `src/web/components/common`、`layout`：可复用控件与编辑器、分割布局组件。
- `src/web/features/workspace`、`sidebar`、`request`、`response`、`runs`：各功能的组件、状态及相关类型与辅助函数。
- `src/web/services`、`localization`：宿主 RPC 与通知、翻译资源访问与响应式语言状态。
- `src/web`：应用组装、入口、HTML、样式与 Vue 类型声明。

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

