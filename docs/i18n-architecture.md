# MyTools 国际化（i18n）架构设计

## 1. 目的与结论

MyTools 是一个 WPF 宿主，同时支持内置 .NET 插件、Node 后端和 WebView2 中的 HTML/JavaScript 插件，并计划支持第三方插件。单一资源格式无法自然覆盖所有运行时，因此本设计采用：

> **统一消息描述符、语言标识、回退规则和提取/翻译流水线；不强制统一运行时资源文件格式。**

最终资源格式为：

1. MyTools 宿主及其内置 WPF/.NET 文案：`.resx`。
2. 插件（Node、HTML、JavaScript）文案：JSON。
3. JavaScript/Node 使用 `i18next`；调用必须提供稳定 key 和英文 `defaultValue`。
4. MyTools 负责提取、缓存和补齐插件缺失语言；插件作者不必在每次发布时提供所有语言。 //QQ: 仅支持node web插件,即html/js/ts

本设计只处理面向最终用户的文本。诊断日志、异常堆栈和开发者日志默认不要求本地化；若日志内容会直接展示给用户，必须转化为本地化用户消息。

---

## 2. 范围与非目标

### 2.1 范围

1. WPF、ViewModel、宿主服务和内置 .NET 插件中的用户可见文本。
2. 插件 manifest、搜索结果、动作名称/说明、动作结果、设置项和 Web 详情页。
3. Node JSON-RPC 和 WebView2 消息中携带的语言上下文。
4. 文案提取、人工翻译、插件翻译、AI 翻译缓存、回退和质量校验。
5. 第三方插件的最小 i18n 作者契约。

### 2.2 非目标

1. 不要求将第三方插件所有源代码转换为 C# 或 WPF 资源格式。
2. 不要求插件作者支持宿主支持的全部语言。
3. 不将 AI 翻译视为人工审核后的正式翻译真源。
4. 不通过翻译改变稳定标识、配置键、插件 ID、关键字路由、存储路径或缓存键。
5. 第一阶段不要求对任意动态拼接字符串做到 100% 静态提取；此类文本必须逐步改造为规范调用。

---

## 3. 当前状态与需要解决的问题

当前桌面端使用 `MyTools.Desktop/Resources/Strings.en-US.xaml`、`Strings.zh-CN.xaml` 和 `LanguageService.RefreshResourceDictionaries()` 替换 WPF `ResourceDictionary`。这只解决了部分 XAML 字符串，仍存在以下问题：

1. `LanguageService`、`AppConfigService`、ViewModel、窗口 code-behind、动作执行逻辑和内置插件中仍存在硬编码用户文本。
2. `LanguageService.GetCaption` 直接依赖 `Application.Current.Resources`，无法被非 WPF 后端、内置插件或第三方运行时自然复用。
3. 现有 `Result.ErrorMessage`、`ActionResult.Message`、`ResultItem.Title/SubTitle` 主要传递最终字符串，缺少稳定的本地化语义。
4. `PluginBase.Name` 当前参与配置分类和设置完整路径；若直接将其翻译，会改变 `Plugins.{Name}.{Setting}` 的持久化路径。
5. Node RPC 的 `search`、`invokeAction`、`detailEvent` 参数未携带 locale；WebView2 的 `initialize-detail` 消息也未携带 locale 或翻译资源。
6. HTML 示例存在裸文本节点和 JS 硬编码字符串，无法稳定提取，也无法在语言切换后同步更新。

---

## 4. 架构原则

1. **稳定标识不翻译。** 插件 ID、配置路径 Name、设置 Name、协议字段、关键字、结果键和数据库键均保持稳定。
2. **所有用户可见文本可描述。** 文案必须具备 key、英文 fallback；运行时参数必须可验证。
3. **宿主主导语言状态。** 当前语言来自 MyTools 设置，所有运行时收到同一个 BCP 47 locale，例如 `en-US`、`zh-CN`、`fr-FR`。
4. **插件自治但平台兜底。** 插件可自带人工翻译；缺失语言由 MyTools 按需自动补齐并缓存。
5. **人工优先，AI 可替换。** 自动翻译不得覆盖人工翻译或插件作者明确提供的目标语言翻译。
6. **可提取优先。** key、英文 fallback 与可选上下文必须是静态可分析值；禁止将 key 或 fallback 通过字符串拼接动态生成。
7. **翻译可追溯。** 每个译文必须记录来源、源文本 hash、目标 locale、生成时间、翻译器/模型版本和校验结果。
8. **最小化外发。** 对外部 AI 服务发送插件文本前必须遵循用户授权、隐私策略和插件许可；应允许关闭云翻译或改用本地/企业模型。

---

## 5. 总体架构

```text
WPF / 内置 .NET 代码  ── ILocalizationService ──> 宿主 .resx（人工翻译）
                                                └─> LocalizedMessage 描述符

Node / JS / HTML 插件 ── i18next + JSON ───────> 插件自带 locales/*.json
                                                └─> i18n catalog（key + 英文 fallback）

提取器（C# / JS / HTML） ──> 标准化 Catalog ──> 翻译协调器
                                                   ├─> 人工覆盖
                                                   ├─> 插件提供翻译
                                                   ├─> AI 翻译缓存
                                                   └─> fallback / key

MyTools Locale Service ──> Node RPC locale 上下文
                        └─> WebView2 locale、资源合并与 language-changed 消息
```

### 5.1 语言资源来源优先级

对于任意消息 `(scope, key, locale)`，最终显示值严格按以下顺序解析：

1. **人工翻译**：MyTools 官方/管理员/用户维护的覆盖翻译。
2. **插件翻译**：插件包中 `locales/{locale}.json` 的作者提供翻译。
3. **AI 翻译缓存**：由 MyTools 生成且已通过自动校验的缓存译文。
4. **英文 fallback**：调用点或 catalog 声明的 `defaultValue`；规范默认语言为 `en-US`。
5. **稳定 key**：仅用于明确暴露缺失翻译，不应作为常态 UI。

解析某个区域性语言时，先查精确 locale，再查中性语言，再进入上述来源的下一层。例如 `fr-CA`：`fr-CA` -> `fr` -> `en-US` fallback。

### 5.2 作用域（scope）

为防止不同插件 key 冲突，所有翻译均位于作用域内：

1. `host`：MyTools 宿主及内置 .NET 功能。
2. `builtin:{pluginId}`：内置插件；建议仍可由宿主 `.resx` 提供人工翻译。
3. `plugin:{pluginId}`：第三方插件；每个插件独立 JSON namespace。

插件 ID 必须稳定且不可翻译，例如 `hello-search`、`dll-interface-reader`。

---

## 6. 统一消息描述符

### 6.1 运行时语义

所有语言运行时均表达相同的消息语义：

```text
MessageDescriptor
- key: string                 // 稳定且唯一的语义键
- defaultValue: string        // 英文 fallback，必须存在
- values/args: object?        // 命名动态参数，可选
- context: string?            // 消歧上下文，可选，通常供提取/翻译使用
```

`key` 不是展示文本，不能随语言、产品文案调整或重构而改变。修改 `defaultValue` 时必须使源文本 hash 变化，以触发 AI 缓存失效和重新翻译。

### 6.2 Key 规范

建议采用 PascalCase 层级命名，按稳定功能域划分：

```text
Host.Search.Status.Searching
Host.Action.ExecuteFailed
Plugin.DllInterfaceReader.Name
Plugin.DllInterfaceReader.Result.Class
Plugin.HelloSearch.Detail.Refresh
```

规则：

1. key 必须是字符串字面量。
2. key 不得使用用户数据、文件名、语言名或运行时拼接值。
3. 一个 key 对应一个稳定语义；同词不同义必须使用不同 key，例如按钮 `Open` 与文件状态 `Open`。
4. 不得以英文文本本身作为 key。

### 6.3 参数与格式化规则

只允许命名占位符，例如 `{{count}}`、`{{name}}`、`{{path}}`；不再新增 `{{0}}`、`{{1}}` 形式。

规则：

1. 运行时参数使用对象/字典传递，而不是位置数组。
2. 翻译前后占位符**集合必须完全一致**；允许不同顺序，不允许删除、改名、增加或将占位符翻译为自然语言。
3. 格式化失败时记录诊断信息并回退到未格式化文本或英文 fallback，不能抛出导致 UI 失败。
4. 计数、日期、金额等格式由当前 `CultureInfo`/`Intl` 格式化，不能让 AI 生成区域格式。

---

## 7. 宿主与内置 .NET 文案：RESX

### 7.1 资源组织

MyTools 宿主使用独立 RESX 资源。建议目录：

```text
MyTools.Desktop/
  Localization/
    HostStrings.resx              # 默认英文（en-US）
    HostStrings.zh-CN.resx
    HostStrings.fr-FR.resx
    HostStrings.fr.resx           # 可选中性语言覆盖
```
//QQ: 内置插件使用宿主resx,但是key前缀必须位 Plugin.{PluginId}.，后期可能大部分内置插件都会改为web node插件, 便于后期提取到插件目录
内置插件可以根据规模选择：
1. 小型插件使用宿主 `HostStrings*.resx` 的 `Plugin.{PluginId}.*` key；或
2. 大型独立插件使用自己的 `.resx`，但必须通过同一个 `ILocalizationService` 和 stable scope 解析。

`.resx` 默认资源文件必须是英文 fallback 真源。其资源名称与第 6 节 key 一致。

### 7.2 服务边界
// ILocalizationService 是给 内置插件和host 使用的, 都是从host的 resx中获取翻译, 所以不需要传入scope
在 `MyTools.Common`（或未来独立契约项目）定义不依赖 WPF 的本地化抽象，例如：

```csharp
public interface ILocalizationService
{
    string CurrentLocale { get; }
    string GetCaption(string key, string defaultValue, object? values = null, string? context = null);
    event EventHandler<LocaleChangedEventArgs>? LocaleChanged;
}
```

说明：

1. 具体 C# 方法名可以是 `GetCaption`，但语义必须等同于插件端 `i18next.t(key, { defaultValue, ... })`。
2. `ILocalizationService` 不得依赖 `Application.Current`、`ResourceDictionary`、WPF 控件或 WebView2。
3. WPF 适配器使用 `ResourceManager` 和当前 `CultureInfo` 读取 `.resx`。
4. `LanguageService` 演进为语言状态协调器：验证 locale、保存设置、切换 `CultureInfo`、通知 `ILocalizationService`、通知 Web 插件。它不再负责替换 `Strings.{locale}.xaml`。

### 7.3 WPF 使用方式

WPF 不要求所有调用在语法上写成完全相同的函数形式，但必须调用统一服务语义：

1. XAML：提供 `LocExtension`/绑定代理，将 `key` 和 `defaultValue` 交给 `ILocalizationService`；语言变更时自动刷新。
2. ViewModel、服务、code-behind：注入 `ILocalizationService` 后调用 `GetCaption`。
3. `MessageBox` 标题、状态栏、托盘菜单、模板文本、窗口标题均使用本地化服务或 `LocExtension`。
4. `Result.ErrorMessage`、`ActionResult.Message` 等跨层模型应逐步支持 `LocalizedMessage`，以保留 key/defaultValue/args，而非只在后端过早固化字符串。

### 7.4 迁移与退役旧 XAML 字典

现有 `Resources/Strings.en-US.xaml`、`Strings.zh-CN.xaml` 和 `LanguageService.RefreshResourceDictionaries()` 是过渡实现，不能与 RESX 长期作为两个独立真源。

迁移规则：

1. 将现有 key 和英文/中文内容迁入 `HostStrings.resx` / `HostStrings.zh-CN.resx`；修正重复、失效或未使用 key。
2. 对每个 XAML 文案引用和 `LanguageService.GetCaption` 调用改用新的 RESX 适配器。
3. 清理 WPF XAML、code-behind、ViewModel、服务层及内置插件中所有用户可见硬编码文本；注释和日志不在强制范围。
4. 建立扫描/测试，禁止新增裸露的用户可见字符串。
5. 仅当所有调用点迁移、资源覆盖测试通过、动态切语言验证完成后，删除旧 `Strings.*.xaml`、`App.xaml` 中的合并字典和 `RefreshResourceDictionaries()`。

特别注意：`PluginBase.Name`、`ConfigurationCategory.Name`、`ConfigurationSetting.Name` 当前参与配置路径。迁移时必须新增或明确稳定的 `PluginId`/`ConfigurationKey`，而不是将本地化显示名写回这些 Name 字段。

---

## 8. 插件资源：JSON 与 i18next

### 8.1 插件目录约定

每个第三方插件的资源建议如下：

```text
my-plugin/
  plugin.json
  i18n/
    catalog.en-US.json           # 提取产物，必须随包发布
    locales/
      en-US.json                 # 可选；通常与 defaultValue 等价
      zh-CN.json                 # 作者可选提供
      fr-FR.json                 # 作者可选提供
  backend/
  web/
```

`catalog.en-US.json` 是插件的可审计源文案清单，不等同于最终显示语言包；它至少记录所有 key、英文 defaultValue 和提取元信息。作者可只发布 catalog 和英文 defaultValue，也可发布部分人工语言包。

### 8.2 JSON locale 文件格式

插件自带 locale 文件采用扁平 key -> caption 的 JSON：

```json
{
  "Plugin.HelloSearch.Name": "Hello Search",
  "Plugin.HelloSearch.Detail.Refresh": "Request refresh from Node runtime",
  "Plugin.HelloSearch.Result.Found": "Found {{count}} matching results" //QQ: 
}
```

i18next 默认插值使用 `{{count}}`、`{{name}}`。Catalog 中的标准占位符也应采用该形式；C# 适配器需将其映射为等价命名参数，或在共享描述符中统一标准并在边界转换。

### 8.3 JavaScript 与 Node 规范

Web 前端和 Node 后端都使用 `i18next`。所有可见文案必须提供 `defaultValue`：

```js
// 正确：key 与 defaultValue 均为静态字面量。
i18next.t("Plugin.HelloSearch.Detail.Refresh", {
  defaultValue: "Request refresh from Node runtime"
});

i18next.t("Plugin.HelloSearch.Result.Found", {
  defaultValue: "Found {{count}} matching results",
  count
});
```

禁止以下形式：

```js
// 禁止：动态 key 和/或无 defaultValue，无法可靠提取。
i18next.t(`Plugin.${feature}.Title`);
i18next.t(key, { defaultValue: getFallback() });
```

要求：

1. `i18next.t` 的第一个参数必须为静态 key。
2. options 必须包含静态字符串 `defaultValue`；`defaultValue` 不能省略。
3. 调用可带 `context` 元数据，例如由 SDK 辅助函数提供，但 context 不参与最终用户展示。
4. Node 后端返回给宿主的用户文案也必须经过同一 i18next 实例解析。
5. 插件启动和语言切换时，i18next 的 `lng` 必须由宿主传递的 locale 决定，不使用浏览器默认语言替代宿主设置。

为减少开发者漏写 `defaultValue`，插件 SDK 应提供等价包装器，例如 `mytoolsI18n.t(key, { defaultValue, ...values })`，并在 lint/提取阶段对裸 `i18next.t` 实施检查。

### 8.4 HTML 规范

HTML 不得依赖扫描裸文本节点。静态 DOM 文案使用 `data-i18n`，并提供英文 fallback：

```html
<button
  data-i18n="[text]Plugin.HelloSearch.Detail.Refresh"
  data-i18n-default-value="Request refresh from Node runtime">
</button>
```

也允许由 JavaScript 动态注入：

```js
document.querySelector("#refresh").textContent = i18next.t(
  "Plugin.HelloSearch.Detail.Refresh",
  { defaultValue: "Request refresh from Node runtime" }
);
```

提取器必须支持：

1. `data-i18n` 与 `data-i18n-default-value` 组合。
2. `data-i18n` 的属性前缀，例如 `[placeholder]`、`[title]`、`[aria-label]`。
3. 规范的 i18next JS/TS 调用。

裸文本节点只允许用于用户数据或非用户可见内容；任何静态 UI 文案都必须改为上述两种形式之一。

---

## 9. 插件协议与语言同步

### 9.1 Node RPC

现有 JSON-RPC 请求的参数增加兼容字段：

```json
{
  "locale": "fr-FR",
  "fallbackLocale": "en-US"
}
```

以下请求都必须支持该上下文：

1. `search`
2. `invokeAction`
3. `detailEvent`
4. `initialize`（新增或扩展）

//QQ: 不用考虑兼容旧插件, 软件还未发布
协议新增字段必须可选，以兼容旧插件；旧插件仍可运行，但将只能显示其自身默认语言或英文 fallback。

### 9.2 WebView2 详情页

`initialize-detail` 的 payload 增加：

```json
{
  "locale": "fr-FR",
  "fallbackLocale": "en-US",
  "translationRevision": "...",
  "messages": {
    "Plugin.HelloSearch.Detail.Refresh": "Demander une actualisation depuis le runtime Node"
  }
}
```
//QQ: 是不是哪里要传入参数scope?
`messages` 是宿主按来源优先级合并后的有效字典，可只包含当前插件/页面 namespace。插件页面将其加载到 i18next，再渲染 DOM。

当用户在 MyTools 中切换语言时，宿主向已加载的 WebView2 页面发送：

```json
{
  "type": "language-changed",
  "payload": {
    "locale": "zh-CN",
    "fallbackLocale": "en-US",
    "translationRevision": "...",
    "messages": { }
  }
}
```

页面必须切换 i18next language，并重新应用 `data-i18n` 和动态视图状态。宿主同时将新的 locale 传递给仍在运行的 Node 插件；必要时重载其语言资源，但不得因语言切换丢失搜索状态。

### 9.3 Manifest 建议扩展

`plugin.json` 可增加：

```json
{
  "i18n": {
    "defaultLocale": "en-US",
    "catalog": "i18n/catalog.en-US.json",
    "localesPath": "i18n/locales",
    "supportedLocales": ["en-US", "zh-CN"]
  }
}
```

`defaultLocale` 和 `catalog` 对采用新 i18n SDK 的插件应为必填。`supportedLocales` 表示作者提供的人工包，不表示 MyTools 不可为其他语言生成 AI 缓存。

---

## 10. Catalog、提取与静态分析

### 10.1 标准 catalog 格式

提取器输出标准 JSON catalog；每项保存足够的翻译上下文：

```json
{
  "schemaVersion": 1,
  "scope": "plugin:hello-search",
  "pluginId": "hello-search",
  "sourceLocale": "en-US",
  "entries": [
    {
      "key": "Plugin.HelloSearch.Detail.Refresh",
      "defaultValue": "Request refresh from Node runtime",
      "placeholders": [],
      "references": [
        {
          "filePath": "web/main.js",
          "line": 48,
          "column": 1,
          "symbol": "DetailPage.handleRefreshClick",
          "comment": "Requests current detail state from the Node backend"
        }
      ],
      "existingTranslations": {
        "zh-CN": "请求 Node 运行时刷新"
      },
      "sourceHash": "sha256:..."
    }
  ]
}
```

字段要求：

1. `key`、`defaultValue`、`sourceHash`、`references` 必填。
2. `filePath` 相对插件根目录或仓库根目录，不能包含用户机器绝对路径。
3. `pluginId` 对插件必须存在；宿主文本使用 `host` scope。 //QQ: pluginId 是否改成scope,帮我先确认哪个好
4. `symbol` 是类/方法/函数/DOM selector 等可读定位信息；无法识别时使用空字符串。
5. `comment` 为作者可选消歧注释，提取器应尽可能读取临近注释或显式 `context`。
6. `existingTranslations` 收集插件自带翻译，供 AI 避免重复翻译和术语学习。

### 10.2 提取器实现边界
1. **C#**：使用 Roslyn 语法树识别 `ILocalizationService.GetCaption`、`LocalizedMessage` 和指定 `LocExtension`；只接受常量 key/defaultValue。
2. **JS/TS**：使用 AST 识别 `i18next.t` 和 MyTools SDK 包装函数；校验 options 中的静态 `defaultValue`。
3. **HTML**：使用 HTML 解析器识别 `data-i18n` 和 `data-i18n-default-value`；不得依赖正则扫描整个 HTML。
4. **XAML**：迁移期识别旧资源引用并统计，目标状态识别 `LocExtension` 的 key/defaultValue。
5. 发现动态 key、缺失 defaultValue、重复 key 对应不同 fallback、非法占位符或裸 HTML 静态文本时，构建必须报错或至少在严格 CI 模式失败。

### 10.3 短文本歧义处理

`Open`、`Save`、`Change`、`Record` 等短文本不能仅凭 fallback 自动翻译。提取器和 SDK 必须传递上下文：

1. `filePath`
2. `pluginId`/scope
3. 类、方法、函数或 DOM 位置（`symbol`）
4. 作者提供的 `comment`/`context`
5. 相同 key 的既有其他语言翻译
6. 领域术语表命中项
//QQ: 在 i18next 中，context 是运行时选择机制，通常会查找带 context 后缀的资源 key，并非纯元数据. (确认这点对吗,查询官网), 将翻译注释改名为 translatorComment、description 或 meaning；SDK wrapper 在调用 i18next 前移除该字段；context 保留给真正的 i18next grammatical context；提取器分别处理“运行时 context”和“翻译注释”。
推荐对歧义高的文案强制要求 `context`，例如：

```js
i18next.t("Plugin.Example.Action.OpenFile", {
  defaultValue: "Open",
  context: "Verb: open the selected file in the operating system"
});
```

`context` 不应改变 i18next key 或最终显示值；它是提取、审核和 AI 翻译提示的一部分。

---

## 11. 自动翻译、术语与质量控制

### 11.1 触发时机

翻译协调器支持以下触发方式：

1. **构建/发布预提取**：生成或校验 catalog，不强制翻译全部目标语言。
2. **插件安装/升级**：导入 catalog，比较 sourceHash，标记缺失。 // 安装后不自动翻译, 仅标记缺失, 用户可在设置页手动触发AI翻译
3. **首次切换到某 locale**：仅翻译当前插件/宿主缺失且用户实际需要的条目。 //QQ: 首次切换如果发现部分未翻译,提示用户是否使用AI翻译,用户同意后进入locale 翻译界面, 定位到那个语言的条目, 用户点击 AI 翻译后执行翻译
4. **后台批处理**：在用户允许的前提下，增量补齐常用语言。 //QQ: 暂时不做后台批处理
5. **管理员或用户手动触发**：重新生成指定插件、语言或 key。

第三方作者仅需维护英文 fallback 和可选部分人工翻译；无需在每次发布时支持所有 MyTools 语言。

### 11.2 AI 请求输入

每个待翻译条目应提交：

1. source locale 与 target locale。
2. key 与英文 defaultValue。
3. 占位符集合及“不得修改占位符”的硬约束。
4. `filePath`、`pluginId`、`symbol`、`comment/context`。
5. 同 scope 下相关人工译文和 `existingTranslations`。
6. 匹配的术语表条目。
7. 所需输出 JSON schema；模型只返回译文，不返回解释。

批处理时按插件、功能域和上下文分组，避免把完全无关的短文本混在同一提示中。

### 11.3 翻译结果校验

AI 输出写入缓存前必须自动校验：

1. key 一一对应，不能遗漏、重复或增加未知 key。
2. 翻译文本非空，且不等于错误/拒绝信息。
3. 源、目标占位符集合完全相同。
4. 不包含未转义控制字符或无效 JSON。
5. 术语表中标记为强制的术语必须符合目标语言译法。
6. 不覆盖人工翻译或插件作者翻译。
7. `sourceHash` 必须与当前 defaultValue 一致；不一致的旧 AI 缓存立即失效。

校验失败时不得使用该 AI 文本；运行时回退到下一层，并记录可诊断的失败原因。

### 11.4 术语表与翻译记忆

MyTools 维护两类共享资产：

1. **Glossary（术语表）**：例如 `Plugin`、`Keyword`、`Action`、`Table Code`、`Interface` 的目标语言约定，以及是否禁止翻译的产品名/技术名。
2. **Translation Memory（翻译记忆）**：以 `(scope, key, sourceHash, locale)` 和可复用的 `(defaultValue, context, locale)` 保存审核过的译文。

术语表优先于 AI 自由生成；翻译记忆可减少重复调用并提高同一术语在不同插件中的一致性。不同第三方插件不得在未授权的情况下共享敏感文本；公共翻译记忆只保存可共享的通用短语。

### 11.5 缓存位置与失效

建议 AI 缓存保存在用户数据目录，而不回写第三方插件安装包：

```text
%LocalAppData%/MyTools/i18n/
  host/
    fr-FR.generated.json
  plugins/
    hello-search/
      0.2.0/
        fr-FR.generated.json
        metadata.json
```

缓存键至少包含：`pluginId`、插件版本、scope、locale、key、sourceHash、术语表版本和翻译器配置版本。插件升级、fallback 修改、术语表变化或人工覆盖更新后，相关缓存必须失效或重算。

---

## 12. 隐私、安全与可用性

1. 云 AI 翻译默认应由用户或管理员明确启用；设置页面需说明会发送的内容类型、服务提供商和数据处理方式。
2. 支持禁用自动翻译、仅使用插件自带翻译、使用本地/企业模型、清空翻译缓存。
3. AI 调用失败、离线、超时或配额耗尽时不得阻断插件加载或搜索；应继续使用插件翻译或英文 fallback。
4. 第三方插件 catalog 被视为不可信输入：限制文件大小、条目数、文本长度、locale 数量和 JSON 解析深度。
5. HTML 插件传入的 `messages` 必须按普通文本处理；禁止把翻译文本作为 HTML 注入，避免 XSS。
6. 用户可查看某条译文的来源（人工/插件/AI/fallback），并可报告或覆盖错误翻译。
7. 插件只能声明自己 scope 下的 key；禁止插件覆盖 host 或其他插件 namespace；
8. catalog、localesPath 必须限制在插件根目录；防止 ..、绝对路径、符号链接逃逸；
9. 

---

## 13. 迁移计划

### Phase 0：规范和基线

1. 批准本文的 key、locale、默认英文、命名占位符和优先级规则。
2. 盘点 `Strings.*.xaml`、XAML、C#、内置插件、Node 示例和 HTML 中用户可见硬编码。
3. 为所有内置插件定义稳定 `PluginId`；将其与显示名、配置路径和搜索历史标识解耦。

### Phase 1：宿主 RESX 基础

1. 新建 `HostStrings.resx` 和已有语言对应的 `.resx`。
2. 新建无 WPF 依赖的 `ILocalizationService`、RESX 适配器和 locale 协调器。
3. 为 WPF 实现 `LocExtension` 或等价可刷新绑定。
4. 迁移宿主公共 UI、状态栏、错误弹窗、配置页和 code-behind 文案。
5. 加入 resource completeness 测试：默认 RESX 是 key 真源，目标语言允许缺失但需报告。

### Phase 2：内置插件与结果模型

1. 迁移内置插件的名称、描述、动作、设置标题/描述、结果固定前缀和用户可见错误。
2. 将配置持久化路径迁移到稳定 ID/Key，避免使用本地化显示名。
3. 为 `Result`、`ActionResult` 等跨层用户消息引入 `LocalizedMessage` 或等价描述符；保留字符串兼容层，逐步替换。

### Phase 3：插件 SDK、Catalog 和协议

1. 定义 `plugin.json.i18n`，实现 catalog 装载和 locale JSON 合并。
2. 在 Node RPC 和 WebView2 `initialize-detail` 增加可选 locale 字段，并实现 `language-changed`。
3. 发布 Node/JS SDK：初始化 i18next、`defaultValue` 约束、HTML `data-i18n` 初始化工具。
4. 将 `hello-search` 示例迁移为规范参考实现。

### Phase 4：提取与 CI

1. 实现 C# Roslyn、JS/TS AST、HTML 解析器提取器。
2. 生成标准 catalog；验证 key 冲突、defaultValue、占位符和裸 HTML 文案。
3. 先以 warning 运行，完成现有代码清理后在 CI 中升级为 error。

### Phase 5：翻译协调器与 AI 缓存

1. 导入人工覆盖、插件 locale 和 catalog。
2. 实现按需翻译、缓存、sourceHash 失效和校验。
3. 接入术语表、翻译记忆、隐私开关和来源可视化。
4. 先在非关键示例插件和单一目标语言灰度验证，再扩展至所有插件和语言。

### Phase 6：退役旧实现

1. 确认所有 WPF 页面和代码均使用 RESX 本地化服务。
2. 删除 `Strings.*.xaml`、`App.xaml` 中对应合并字典、`LanguageService.RefreshResourceDictionaries()` 和基于 `Application.Current.Resources` 的 `GetCaption` 实现。
3. 不用保留旧插件协议兼容分支

---

## 14. 验收标准

### 14.1 宿主

1. `en-US`、`zh-CN`、`fr-FR` 之间切换时，允许提示用户重启应用；重启后 WPF 窗口、托盘、配置页、状态栏、弹窗和内置 Actions 必须统一使用所选语言，不要求当前进程动态刷新全部界面。
2. 宿主用户可见文本不再依赖旧 `Strings.*.xaml`；旧资源文件已删除或不再被编译/加载。
3. 新增用户可见 C# 文案未通过 `ILocalizationService`/`LocalizedMessage` 时，CI 能检测并报告。

### 14.2 插件

1. 仅提供英文 catalog 的插件在切换到 `fr-FR` 时，能够从 AI 缓存得到已校验的法语；AI 不可用时稳定回退英文。
2. 插件提供 `zh-CN` 时，该人工插件翻译优先于 AI 缓存。
3. 用户/管理员人工覆盖优先于插件翻译。
4. HTML 页面收到 `language-changed` 后重新渲染静态和动态文案；Node 后端后续返回文本与当前 locale 一致。
5. 旧插件未声明 i18n 时仍能加载，不因新增协议字段失败。

### 14.3 质量与安全

1. 任意翻译结果若丢失、增加或重命名占位符，不得进入有效缓存。
2. 相同 `scope + key` 出现不同英文 fallback 时，构建失败。
3. 翻译缓存能在插件升级或 sourceHash 变化后正确失效。
4. 禁用云翻译后不发生外部文本发送，且产品仍可用。
5. 翻译文本通过安全的文本渲染路径显示，不产生 HTML 注入。

---

## 15. 待决策项

1. 默认是否启用云 AI 翻译，还是必须显式 opt-in。
2. 首批支持的目标语言及是否提供中性语言包（如 `fr`）。
3. 人工覆盖的管理入口：仅官方、管理员配置，还是允许终端用户编辑。
4. `LocalizedMessage` 是否在第一阶段进入 `Result`/`ActionResult` 公共 API，或先通过适配层渐进引入。
5. C# 与 i18next 占位符的最终统一表示：建议规范层采用命名参数，边界适配 i18next 的 `{{name}}`；需在 SDK 中固定实现。
6. 提取器的发布形态：独立 CLI、MSBuild/Roslyn Analyzer、Node CLI，或三者组合。

---

## 16. 第三方插件作者最小契约

第三方插件作者只需要做到：

1. 使用稳定、不翻译的 `plugin.json.id`。
2. 对每个用户可见文本使用稳定 key 和英文 `defaultValue`。
3. Node/JS 使用 i18next，并且每次 `t` 调用都提供 `defaultValue`。
4. HTML 静态文本使用 `data-i18n` + `data-i18n-default-value`，或通过规范 JS 调用设置。
5. 打包 catalog；可选提供任意数量的人工 JSON locale 文件。
6. 为含糊短文本提供 `context`/注释。
7. 不动态构造 key/defaultValue，不修改占位符语义。

作者**不需要**：

1. 支持所有 MyTools 语言。
2. 自己调用 AI 或维护 AI 凭据。
3. 将翻译缓存提交回插件包。
4. 了解 WPF、RESX 或 MyTools 内部 `ResourceManager` 实现。

