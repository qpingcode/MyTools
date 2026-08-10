# Settings 插件 — Plugins 分类（热键/关键词/启用状态管理）设计

## 目标

在 settings 插件中新增 **Plugins** 分类，以插件为行统一管理：

1. **插件启用/禁用**：包含 Node 插件（目前 `NodePlugin.IsEnabled` 硬编码为 `true`，无法关闭）
2. **热键覆盖**：用户设置的热键优先于 `plugin.json` 中的默认值
3. **关键词覆盖**：同上
4. **冲突检测**：设置时检测是否与其他插件的热键/关键词重复，提示和谁重复

## 现状分析

### 热键

- 插件热键在 `AppBootstrapper.RegisterNodePluginHotKeys` 中从 `plugin.json` 注册一次，注册后 ID **被丢弃**，无法取消重注。
- `HotKeyManager.RegisterHotKey` 返回 `int` ID，`UnregisterHotKey(id)` 可取消。搜索热键已有这个 swap 模式（`RegisterySearchHotKey`）。
- Win32 `RegisterHotKey` 冲突时返回 `false`，`HotKeyMessageHandler` 抛 `InvalidOperationException`。

### 关键词

- `PluginRegistry._keywordMap` 是 `Dictionary<string, IPlugin>`，`Register` 用 `.Add`（重复会抛异常）。
- **没有** `Unregister` / `Clear` 方法。
- `PluginLoader.RegisterPlugins` 注册时遍历 `nodePlugin.Keywords`。

### 插件启用

- `PluginBase.IsEnabled` 读 `PluginState.IsEnabled`（默认 true），可被 `DisablePlugin()` 关闭。
- `NodePlugin.IsEnabled` **硬编码 `true`**，没有 `PluginState`，`RegisterSettings` 是空方法。
- ~~`Plugins.{id}.IsEnabled` 设置项被注册了但从未读回——toggle 是纯装饰性的。~~ **已移除**：`PluginBase.AddPluginSettings` 不再注册默认的 `IsEnabled` 设置项。启用状态统一在 Plugins 分类（本设计）中管理。
- **Plugins 分类树变化**：`PluginBase.RegisterSettings` 现在在注册后检查子分类是否为空，无额外设置的插件（如 Calculator、ClipBoard）不再出现在 Plugins 树下。只有有额外设置的插件（如 DllInterfaceReader 的 ILSpyPath）保留子分类。
- `Searcher` 和 `SearchViewModel` 中有 `IsEnabled` 检查逻辑，只是从没被正确驱动。

## 设计方案

### 核心思路：覆盖层（Override Provider）

引入一个轻量的覆盖配置文件 `Keymap.json`，存储用户对插件热键/关键词/启用状态的自定义覆盖。宿主在注册热键、注册关键词、检查启用状态时，先查覆盖层，有覆盖就用覆盖值，否则用 `plugin.json` 默认值。

### 数据结构

`%AppData%\MyTools.Desktop\Keymap.json`：

```json
{
  "settings:main": {
    "hotKey": "Ctrl+Comma",
    "keywords": ["set", "config"],
    "isEnabled": true
  },
  "deepseek-chat:chat": {
    "hotKey": null,
    "keywords": ["chat"],
    "isEnabled": false
  }
}
```

- key 是 `plugin.PluginId`（如 `"settings:main"`、`"deepseek-chat:chat"`）
- `hotKey` 为 `null` 表示清除热键（不注册）
- `keywords` 为 `null` 表示用默认值；为空数组 `[]` 表示清除所有关键词
- `isEnabled` 为 `false` 表示禁用；省略/`true` 表示启用

### 宿主侧改动

#### 1. 新增 `Services/KeymapOverrideProvider.cs`

```csharp
public sealed class KeymapOverrideProvider
{
    private Dictionary<string, KeymapOverride> overrides = new();
    // 加载/保存 Keymap.json

    public string? GetHotKey(string pluginId);          // null = 用默认
    public List<string>? GetKeywords(string pluginId);   // null = 用默认
    public bool? GetIsEnabled(string pluginId);           // null = 用默认(=true)
    public IReadOnlyDictionary<string, KeymapOverride> GetAll();
    void Save(Dictionary<string, KeymapOverride> newOverrides);
}

public sealed class KeymapOverride
{
    public string? HotKey { get; init; }
    public List<string>? Keywords { get; init; }
    public bool? IsEnabled { get; init; }
}
```

注册为 Singleton。

#### 2. 新增 `Services/KeymapService.cs`

负责热键/关键词的**运行时重注册**。核心方法：

```csharp
public sealed class KeymapService
{
    // 注册所有插件热键（启动时调用），存储 pluginId→hotKeyId 映射
    void RegisterAllHotKeys(IEnumerable<NodePlugin> plugins);

    // 热键重注册：unregister 旧的 → register 新的
    void ReRegisterHotKey(NodePlugin plugin, string? newHotKey);

    // 关键词重注册：clear 所有 → 重新注册全部（含覆盖）
    void ReRegisterKeywords(IEnumerable<IPlugin> allPlugins);

    // 冲突检测
    List<KeymapConflict> ValidateHotKey(string pluginId, string? hotKey, IEnumerable<NodePlugin> allPlugins);
    List<KeymapConflict> ValidateKeywords(string pluginId, List<string>? keywords, IEnumerable<NodePlugin> allPlugins);
}
```

#### 3. 修改 `AppBootstrapper.cs`

- `RegisterNodePluginHotKeys` 改为调用 `KeymapService.RegisterAllHotKeys`，内部存储 `Dictionary<string, int>` pluginId→hotKeyId。
- 注册时查 `KeymapOverrideProvider.GetHotKey`，有覆盖就用覆盖值。
- 跳过 `IsEnabled == false` 的插件。

#### 4. 修改 `PluginRegistry.cs`（`IKeywordRegistry` 实现）

- 新增 `Unregister(string keyword)` 和 `Clear()` 方法。
- `Register` 改为用索引赋值 `_keywordMap[keyword] = plugin`（而非 `.Add`），避免重复异常。

#### 5. 修改 `NodePlugin.cs`

- `IsEnabled` 从硬编码 `true` 改为 backing field，由 `KeymapOverrideProvider` 初始化。
- `HotKey` 属性也查覆盖层（或由 `AppBootstrapper` 在注册时直接用覆盖值，不改 `NodePlugin.HotKey` 本身——后者更简洁）。

#### 6. `SettingsPluginHostCallHandler` 新增 3 个 hostCall 方法

```csharp
"getKeymap" => GetKeymap()
"saveKeymap" => SaveKeymap(request.Params)
"validateKeymap" => ValidateKeymap(request.Params)
```

**`getKeymap`** 返回：

```json
{
  "plugins": [
    {
      "pluginId": "settings:main",
      "name": "Settings",
      "defaultHotKey": "Ctrl+Comma",
      "currentHotKey": "Ctrl+P",
      "defaultKeywords": ["settings", "config"],
      "currentKeywords": ["set"],
      "isEnabled": true,
      "isNodePlugin": true
    }
  ]
}
```

**`saveKeymap`** 接收：

```json
{
  "overrides": {
    "settings:main": { "hotKey": "Ctrl+P", "keywords": ["set"], "isEnabled": true }
  }
}
```

保存后热应用：
1. `KeymapOverrideProvider.Save` 持久化
2. `KeymapService.ReRegisterHotKey` 重新注册所有受影响的热键
3. `KeymapService.ReRegisterKeywords` 重新注册关键词
4. 更新 `NodePlugin.IsEnabled` backing field

**`validateKeymap`** 接收待保存的 overrides，返回冲突信息：

```json
{
  "conflicts": [
    {
      "pluginId": "settings:main",
      "field": "hotKey",
      "value": "Alt+V",
      "conflictsWith": "deepseek-chat:chat"
    }
  ]
}
```

### Node 插件前端改动

#### Plugins 分类的 UI

```
┌─────────────────────────────────────────────────────────────┐
│  [🔍 Search settings...                                   ] │
├──────────────┬──────────────────────────────────────────────┤
│  General     │  Plugins                                     │
│   Language   │                                              │
│   Theme      │  ┌──────────────────────────────────────────┐│
│   ...        │  │ ☑ DeepSeek Chat    [Alt+V ⌨]  chat       ││
│              │  │   Default: Alt+V, keywords: chat, ds     ││
│  Plugins  ◀  │  ├──────────────────────────────────────────┤│
│   Dll Inter. │  │ ☑ Settings         [Ctrl+Comma ⌨] config ││
│              │  │   Default: Ctrl+Comma, keywords: settings││
│              │  ├──────────────────────────────────────────┤│
│              │  │ ☐ Translator       [Alt+T ⌨]  translate  ││
│              │  │   ⚠ HotKey conflicts with DeepSeek Chat  ││
│              │  └──────────────────────────────────────────┘│
│              │                                              │
│              │                          [Validate] [Save]   │
└──────────────┴──────────────────────────────────────────────┘
```

> 左侧侧边栏：Plugins 现在是一个**可选分类**（点击直接展示插件列表）。
> 只有有额外设置的插件（如 Dll Interface Reader 的 ILSpyPath）才在 Plugins 下显示子分类。
> 无额外设置的插件（Calculator、ClipBoard 等）不再出现子分类——它们的启用/热键/关键词直接在 Plugins 页面统一管理。

每行（插件为行）：
- **启用 checkbox**：左对齐
- **插件名**：加粗，高亮搜索匹配
- **热键录制器**：显示当前热键，点击后进入录制模式（"按下快捷键..."），捕捉下一次 Key+Modifiers 组合。右侧有清除按钮(×)。
- **关键词输入框**：逗号分隔，placeholder 显示默认关键词
- **冲突提示**：如检测到冲突，在该行下方标红显示"⚠ HotKey conflicts with {pluginName}"

#### 热键录制器交互

1. 点击热键显示区域 → 变为录制状态（灰底，显示 "Press shortcut..."）
2. 用户按下任意键组合 → 捕捉 `e.Key` + `Keyboard.Modifiers`，格式化为 `"Alt+V"` 样式
3. 按 Escape 取消录制，恢复原值
4. 录制完成即时触发 `validateKeymap` 检查冲突

#### 关键词输入

- 单行文本框，逗号分隔（如 `chat, ds`）
- 失焦时触发 `validateKeymap` 检查冲突

#### 保存流程

1. 点击 Save → 先调 `validateKeymap` 检查所有改动
2. 如有冲突 → 标红显示，阻止保存（或允许保存但警告）
3. 无冲突 → 调 `saveKeymap`，宿主热应用（重注热键、重注关键词、更新启用状态）

## 涉及的文件清单

### 新增

| 文件 | 说明 |
|------|------|
| `MyTools.Desktop/Services/KeymapOverrideProvider.cs` | 覆盖配置读写（Keymap.json） |
| `MyTools.Desktop/Services/KeymapService.cs` | 热键/关键词运行时重注册 + 冲突检测 |

### 修改

| 文件 | 说明 | 状态 |
|------|------|------|
| `MyTools.Common/Plugins/PluginBase.cs` | `AddPluginSettings` 不再注册默认 IsEnabled；`RegisterSettings` 移除空子分类 | 待实现 |
| `MyTools.Plugins/Plugins/DllInterfaceReader/DllInterfaceReaderPlugin.cs` | `AddPluginSettings` 去掉 `base` 调用（不再继承 IsEnabled） | 待实现 |
| `MyTools.Desktop/AppBootstrapper.cs` | `RegisterNodePluginHotKeys` 改为通过 `KeymapService` 注册，存 pluginId→hotKeyId；启动时查覆盖层 | 待实现 |
| `MyTools.Desktop/Services/SettingsPluginHostCallHandler.cs` | 新增 `getKeymap` / `saveKeymap` / `validateKeymap` 三个 hostCall 方法 | 待实现 |
| `MyTools.Desktop/DesktopServiceCollectionExtensions.cs` | 注册 `KeymapOverrideProvider`、`KeymapService` | 待实现 |
| `MyTools.Plugins/PluginRegistry.cs` | `IKeywordRegistry` 新增 `Unregister` / `Clear`；`Register` 改为索引赋值 | 待实现 |
| `MyTools.Common/IKeywordRegistry.cs` | 接口新增 `Unregister` / `Clear` | 待实现 |
| `MyTools.Plugins/NodePlugins/NodePlugin.cs` | `IsEnabled` 改为可读写 backing field | 待实现 |
| `MyTools.Plugins/Examples/settings/src/backend/index.mts` | 新增 `getKeymap` / `saveKeymap` / `validateKeymap` handle 转发 | 待实现 |
| `MyTools.Plugins/Examples/settings/src/web/main.ts` | Plugins 分类的 UI 渲染、热键录制器、冲突检测、保存逻辑 | 待实现 |
| `MyTools.Plugins/Examples/settings/src/web/style.css` | keymap 行样式、热键录制器样式、冲突提示样式 | 待实现 |
| `MyTools.Plugins/Examples/settings/i18n/locales/en-US.json` | 新增 keymap 相关文案 | 待实现 |
| `MyTools.Plugins/Examples/settings/i18n/locales/zh-CN.json` | 同上 | 待实现 |

## 实施步骤建议

1. **覆盖层 + 服务**：`KeymapOverrideProvider` + `KeymapService` + DI 注册
2. **关键词注册改造**：`IKeywordRegistry` 加 `Unregister`/`Clear`；`PluginRegistry` 实现
3. **NodePlugin 启用状态**：`IsEnabled` 改为 backing field + `SetEnabled` 方法
4. **AppBootstrapper 改造**：热键注册通过 KeymapService，存 pluginId→hotKeyId
5. **hostCall 方法**：`getKeymap`/`saveKeymap`/`validateKeymap`
6. **前端 UI**：Plugins 分类渲染、热键录制器、冲突检测
7. **i18n 文案**
8. **编译验证 + 测试**

## 验收标准

1. settings 的 Plugins 分类列出所有插件（含 Node 插件），每行显示插件名 + 启用开关 + 热键 + 关键词
2. 热键录制器可捕捉按键组合，Escape 取消
3. 设置重复热键时，提示和哪个插件冲突
4. 设置重复关键词时，提示和哪个插件冲突
5. 保存后热键立即生效（无需重启）
6. 保存后关键词立即生效
7. 禁用某插件后，该插件不出现在搜索结果中，热键不响应
8. 重启后覆盖配置仍然生效

## 风险与边界

1. **Win32 热键全局冲突**：即使应用内不冲突，其他应用可能已注册同一热键。`RegisterHotKey` 会失败，需 catch 并提示用户。
2. **关键词变更需重注全局**：`Clear()` + 重新 `Register` 全部关键词是最简洁的方案，但如果未来关键词很多可考虑增量更新。
3. **NodePlugin.IsEnabled 和 PluginBase.IsEnabled 的统一**：`PluginBase` 有自己的 `PluginState`，改造时应统一为从覆盖层读取，避免两套逻辑。
4. **覆盖与 plugin.json 的优先级**：覆盖文件中存在的值优先于 plugin.json；不存在的字段回退到默认值。
