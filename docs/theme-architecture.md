# MyTools 主题（Theme）架构设计

> 配置持久化说明：本文中关于 `AppConfig` / `AppConfigService` 的内容是早期设计记录，
> 已不再对应当前实现。当前以 `Settings.json` 的 `General.Theme` 为唯一持久化来源。

## 1. 目的与结论

MyTools 当前只有一套硬编码的深色界面：WPF 窗口里散落着 `#1e1e1e`、`#292929`、`#333333`、`White` 等颜色字面量，Web 插件详情页的 CSS 也写死了深色色值。本设计的目标是引入**白天主题（Light）**与**黑夜主题（Dark）**两套主题，并满足：

1. 宿主主体（WPF）与所有插件都支持主题切换。
2. 插件的主题切换由插件自身实现；宿主只负责下发当前主题与切换事件。
3. 支持**运行时热切换**，切换后即时生效，不要求重启。
4. 解决 Web 插件在黑夜主题下「先白后黑」的首帧闪烁。

本设计完全复用已有 i18n 架构（见 `docs/i18n-architecture.md`）的成熟模式：

> **统一主题类型、主题标识、切换事件与下发通道；不强制统一各运行时的样式实现。**

最终落地形态：

1. `MyTools.Common` 定义与 WPF 无关的 `IThemeService`、`ThemeKind`、`ThemeChangedEventArgs`。
2. 宿主实现 `ThemeService`，作为唯一的主题状态真源。
3. WPF 通过 `DynamicResource` 绑定到主题 `ResourceDictionary`，支持热刷新（`ThemeExtension` 为未来可选扩展，本轮不实现，见 §9.4）。
4. Web 插件通过 WebView2 的「注入主题 token + CSS 变量」消除首帧闪烁，并通过 `initialize-detail` / `theme-changed` 消息支持热切换。
5. Node 插件后端通过 RPC 上下文感知当前主题（仅用于返回与主题相关的结构化数据，不负责渲染）。

本设计只处理**视觉主题（配色）**，不处理字号、密度、圆角等更广义的「外观」；这些可在后续阶段以同一套 token 机制扩展。

---

## 2. 范围与非目标

### 2.1 范围

1. WPF 宿主窗口、ViewModel、code-behind、模板、托盘菜单中的颜色与深浅相关资源。
2. 内置 .NET 插件中由宿主统一渲染的颜色（主列表、动作栏、状态点等）。
3. Node 插件的 Web 详情页（HTML/CSS/JS）配色。
4. Node RPC 与 WebView2 消息中携带的主题上下文。
5. 主题设置项（设置页入口）与持久化。
6. 第三方插件的最小主题作者契约。

### 2.2 非目标

1. **不引入 Auto（跟随系统）主题。** 本轮只做 Light / Dark 两个显式主题；`ThemeKind` 预留扩展位，但本轮不监听系统主题变化。
2. 不要求插件作者支持两套完全独立的视觉设计；插件可只依赖宿主下发的 token。
3. 不改变任何稳定标识（插件 ID、配置路径、关键字、协议字段名）。
4. 不处理字体、间距、圆角等非颜色外观；主题 token 第一轮只覆盖颜色。
5. 不替换图标资源为「主题化图标」；如确有需要，后续单独迭代。

---

## 3. 当前状态与需要解决的问题

### 3.1 WPF 侧

1. `App.xaml` 的 `Application.Resources` 是一个空 `ResourceDictionary`，没有任何主题资源定义。
2. `SearchWindow.xaml` 中大量颜色硬编码：窗口背景 `#1e1e1e`、输入框 `#292929`、按钮 `#333333`/`#3a3a3a`、文字 `White`/`#cccccc`/`#aaaaaa` 等，分散在 `Style`、`Border.Background`、`Foreground`、`Trigger` 中。
3. `NodePluginDetailView.xaml` 的外层 `Border Background="#292929"` 也是硬编码。
4. 内置插件列表、动作栏、状态栏、配置页模板均存在同类硬编码。
5. 不存在任何「当前主题」概念，也没有切换事件。

### 3.2 Web 插件侧

1. 示例插件 `chat/src/web/style.css` 中 `body { background:#1e1e1e; color:#ffffff }` 等全部硬编码深色。
2. WebView2 容器 `NodePluginDetailView` 外层 `Border` 背景为固定深色 `#292929`。
3. **首帧闪烁根因**：HTML 加载后，浏览器先按 CSS 默认值（深色）渲染；而宿主的 `initialize-detail` 消息（携带主题上下文）要等 `NavigationCompleted` 之后才通过 `PostWebMessageAsJson` 到达。两者之间存在一段「HTML 已渲染、主题消息未到」的窗口，导致白色主题下出现先黑后白、或黑夜主题下从默认白底短暂闪烁。
4. SDK `MyTools.Plugins/Examples/common/web-tool.ts` 的 `dispatch` 当前只识别 `tool-response`、`tool-event`、`language-changed` 三类消息，没有 `theme-changed`。

### 3.3 配置与状态

1. 主题需要持久化到用户配置，但现有 `AppConfig` 没有 `Theme` 字段。
2. 设置页 `General` 分类下有 `Language`、`AutoStart` 等设置项，但无主题项；`SettingValueTypes` 无 `Theme` 类型。

---

## 4. 架构原则

1. **宿主主导主题状态。** 当前主题来自 MyTools 设置，所有运行时收到同一个 `ThemeKind`（`light` / `dark`），不存在插件自行决定全局主题。
2. **稳定标识不随主题变化。** 插件 ID、配置路径、协议字段名、关键字、结果键均与主题无关。
3. **token 优先于具体色值。** 无论 WPF 还是 Web，颜色一律通过命名 token 引用（如 `BackgroundBrush` / `--mt-bg`），具体色值由当前主题提供；禁止新增直接颜色字面量。
4. **插件自治但宿主兜底。** 插件可自行实现两套样式；不实现时，宿主下发的默认 token 集合应足以让插件呈现合理的深/浅外观。
5. **热切换不丢状态。** 主题切换不应触发插件重新搜索、不应清空详情页交互状态。
6. **首帧零闪烁。** Web 插件在第一帧渲染时就必须使用正确主题，不依赖异步消息到达。
7. **复用 i18n 通道与模式。** 主题的消息下发、热刷新、设置项注册尽量与 i18n 对齐，降低插件作者与维护者认知成本。

---

## 5. 总体架构

```text
用户设置（Theme: light / dark）
        │
        ▼
ThemeService（IThemeService，宿主唯一真源）
        │
        ├─> WPF：ThemeResourceDictionary 切换 → DynamicResource 自动刷新
        │                  └─> ThemeExtension（可选，用于 code-behind/非资源化颜色）
        │
        ├─> WebView2 详情页：
        │      1) 导航前注入引导脚本（首帧 token，零闪烁）
        │      2) initialize-detail 消息携带 theme
        │      3) theme-changed 消息（热切换）
        │
        └─> Node RPC：search / invokeAction / detailEvent / initialize 携带 theme
```

### 5.1 主题标识与默认值

1. 全局只有一个枚举 `ThemeKind { Light, Dark }`，序列化为小写字符串 `"light"` / `"dark"`。
2. **默认主题为 `Dark`**，与当前外观保持一致，避免迁移期视觉跳变。 //QQ: 默认 Dark 还是跟随首次启动时的系统偏好？本轮决定默认 Dark。
3. 主题标识不区分大小写地比较；未知值回退到 `Dark`。
4. 持久化键建议为 `Theme`，存于 `AppConfig` 与设置系统 `General.Theme`，两者必须保持一致（参照 `Language` 的处理：`AppConfigService` 为权威源）。

### 5.2 颜色 token 体系

为避免 WPF 与 Web 各自定义互不一致的色值，两者共用同一套**语义 token 名**，但允许各自的表示形式不同（WPF 用 `Brush`/`Color` 资源键，Web 用 CSS 自定义属性）。

核心 token（第一轮最小集，按需扩展）：

| Token 语义              | WPF 资源键建议            | Web CSS 变量        | 用途                     |
|------------------------|--------------------------|---------------------|--------------------------|
| 窗口/面板背景           | `SurfaceBackgroundBrush` | `--mt-surface-bg`   | 最外层窗口、面板底色     |
| 卡片/输入框背景         | `SurfaceBrush`           | `--mt-surface`      | 输入框、卡片、状态栏     |
| 悬浮/次级表面           | `SurfaceAltBrush`        | `--mt-surface-alt`  | 按钮、悬浮卡片           |
| 主文字                 | `TextPrimaryBrush`       | `--mt-text`         | 标题、主要文字           |
| 次要文字               | `TextSecondaryBrush`     | `--mt-text-muted`   | 副标题、状态文本         |
| 占位/禁用文字          | `TextDisabledBrush`      | `--mt-text-disabled`| placeholder、禁用项      |
| 边框                   | `BorderBrush`            | `--mt-border`       | 分割线、卡片边框         |
| 强调色（选中/聚焦）     | `AccentBrush`            | `--mt-accent`       | 选中态、聚焦边框、链接   |
| 选中项背景             | `SelectionBrush`         | `--mt-selection`    | 列表选中行               |

约定：

1. 新增颜色必须先新增 token，再引用 token；禁止直接写颜色字面量。
2. token 数量保持克制，避免每个组件一个 token；语义相近的复用同一 token。
3. 状态色（成功/失败/进行中，如 `StatusDotStyle` 的 Green/Red/Orange）建议保持跨主题一致，第一轮不纳入主题 token；如需调整再单独引入 `--mt-success` 等。

---

## 6. Common 契约（与 WPF 无关）

在 `MyTools.Common` 新增主题抽象，参照 `ILocalizationService`。位置建议：

```text
MyTools.Common/
  Theming/
    ThemeKind.cs
    IThemeService.cs
    ThemeChangedEventArgs.cs
```

### 6.1 ThemeKind

```csharp
namespace MyTools.Common.Theming;

public enum ThemeKind
{
    Light,
    Dark
}

public static class ThemeKindExtensions
{
    public static string ToWireString(this ThemeKind kind) => kind == ThemeKind.Light ? "light" : "dark";

    public static ThemeKind Parse(string? value) =>
        string.Equals(value, "light", StringComparison.OrdinalIgnoreCase) ? ThemeKind.Light : ThemeKind.Dark;
}
```

说明：

1. 序列化/协议传输统一用小写字符串。
2. `Parse` 对任何非 `light` 的值（含 `null`、空、未知）回退 `Dark`，保证健壮性。

### 6.2 IThemeService

```csharp
namespace MyTools.Common.Theming;

public interface IThemeService
{
    ThemeKind CurrentTheme { get; }
    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
    void SetTheme(ThemeKind theme);
}
```

说明：

1. 不依赖 `Application.Current`、`ResourceDictionary`、WPF 控件或 WebView2。
2. `SetTheme` 同步更新 `CurrentTheme` 并触发 `ThemeChanged`；与当前主题相同时为幂等，不触发事件。
3. 初始化时从配置读取主题并设置 `CurrentTheme`，但不触发事件（无监听者）。

### 6.3 ThemeChangedEventArgs

```csharp
namespace MyTools.Common.Theming;

public sealed class ThemeChangedEventArgs(ThemeKind previousTheme, ThemeKind currentTheme) : EventArgs
{
    public ThemeKind PreviousTheme { get; } = previousTheme;
    public ThemeKind CurrentTheme { get; } = currentTheme;
}
```

形态与 `Localization/LocalizedMessage` 所配的 `LocaleChangedEventArgs` 完全一致。

//QQ: ThemeChangedEventArgs 是否需要携带 token 字典（如 Web 端 CSS 变量）？
//   建议：不携带。token 由各端自行从主题派生；事件只传 ThemeKind，保持 Common 不依赖任何具体配色。

---

## 7. 宿主实现：ThemeService

在 `MyTools.Desktop/Services/ThemeService.cs` 实现 `IThemeService`，参照 `LanguageService`。

```csharp
public class ThemeService : IThemeService
{
    private readonly AppConfigService appConfigService;

    public ThemeKind CurrentTheme { get; private set; }

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public ThemeService(AppConfigService appConfigService) // 在构造函数中读取
    {
        this.appConfigService = appConfigService;
        CurrentTheme = ThemeKind.Parse(appConfigService.AppConfig.Theme);
    }

    public void SetTheme(ThemeKind theme)
    {
        if (theme == CurrentTheme) return;
        var previous = CurrentTheme;
        CurrentTheme = theme;
        appConfigService.SetTheme(theme.ToWireString());
        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(previous, theme));
    }
}
```

落点与注意事项（基于当前 DI 架构）：

1. **DI 注册**：在 `DesktopServiceCollectionExtensions.AddDesktopServices` 注册，紧挨 `LanguageService`，与现有 i18n 服务完全对称：

   ```csharp
   services.AddSingleton<ThemeService>();
   services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
   ```

   注意：`AppBootstrapper.ConfigureServices` 已不存在（见 `59202e3` 重构），所有服务注册统一走 `AddApplicationServices` → `AddDesktopServices`。
2. `AppConfig` 新增 `Theme` 字段（见第 8 节）。
3. `AppConfigService` 新增 `SetTheme(string)`，参照现有 `SetLanguage`。
4. **依赖获取方式**：`AppBootstrapper` 已改为纯构造函数注入（不再用 `ServiceLocator.GetRequiredService<...>()`）。因此 `ThemeService` / `IThemeService` 应加入 `AppBootstrapper` 的构造函数参数列表，与 `localization`、`registry` 并列；`Init()` 内通过字段访问，不通过 `ServiceLocator`。
5. **与 i18n 的关键区别**：主题支持热切换，`SetTheme` 直接生效并触发事件，**不**走 `SetLanguageForNextStartup` + 重启路径。
6. 在 `AppBootstrapper.Init` 中，初始化配置后立即应用一次主题到 WPF 资源字典（调用 `ThemeManager.ApplyTheme(themeService.CurrentTheme)`，不触发事件）。
7. ⚠️ **`ValidateOnBuild` 约束**：`App.OnStartup` 现以 `ValidateOnBuild = true` / `ValidateScopes = true` 构建容器（`App.xaml.cs:48-52`）。`ThemeService` 仅依赖 `AppConfigService`（已是 singleton），依赖图可解析、无环路，符合校验要求。但务必保持 `ThemeManager.ApplyTheme(ThemeKind)` 为**纯静态方法**——由 `ThemeService` 的 `ThemeChanged` 订阅调用它，**不要**让静态助手反向依赖 `IThemeService` 的运行时实例，否则会破坏容器校验与作用域。

---

## 8. 配置与设置入口

### 8.1 AppConfig

`MyTools.Desktop/Models/AppConfig.cs` 新增：

```csharp
[JsonProperty("Theme")]
public string Theme { get; set; } = "dark";
```

`IAppConfig` 同步新增 `Theme` 只读暴露。

### 8.2 AppConfigService

新增方法，参照 `SetLanguage`：

```csharp
public void SetTheme(string theme)
{
    AppConfig.Theme = theme;
    SaveConfig(AppConfig);
}
```

### 8.3 设置项注册

在 `AppBootstrapper.InitializeConfigurationData` 的 `General` 分类下，紧随 `Language` 之后新增。注意 `registry` 与 `localization` 现已通过构造函数注入（见 §7 第 4 点），新增的 `themeService` 同样取自构造函数字段：

```csharp
registry.AddSetting(generalCategory, "Theme",
    localization.GetCaption("Configuration.General.Theme.Title", "Theme"),
    localization.GetCaption("Configuration.General.Theme.Description", "Choose the application color theme"),
    themeService.CurrentTheme.ToWireString(),
    valueType: SettingValueTypes.Theme);
```

并参照 `Language` 的处理：`registry.Reload()` 后用 `themeService.CurrentTheme.ToWireString()` 覆盖一次（`InitValueWithoutNotify`），避免 `Settings.json` 中的陈旧副本与 `AppConfig` 不一致。

//QQ: 主题设置项是否需要 RequiresRestart？建议**不需要**（热切换）。

### 8.4 SettingValueTypes.Theme 与设置模板

1. `SettingValueTypes` 枚举新增 `Theme` 成员。
2. 新增 `MyTools.Desktop/Views/Templates/ThemeSettingTemplate.xaml`，参照 `LanguageSettingTemplate.xaml`：

```xml
<DataTemplate x:Key="ThemeSettingTemplate">
    <ComboBox SelectedValue="{Binding CurrentValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
              SelectedValuePath="Tag" Width="200">
        <ComboBoxItem Tag="light"
                      Content="{loc:Loc Theme.light, DefaultValue=Light}" />
        <ComboBoxItem Tag="dark"
                      Content="{loc:Loc Theme.dark, DefaultValue=Dark}" />
    </ComboBox>
</DataTemplate>
```

3. `SettingTypeToTemplateConverter` 增加 `Theme -> ThemeSettingTemplate` 映射。
4. 设置变更回调：监听该 setting 的 `CurrentValue` 变化，调用 `themeService.SetTheme(...)`。建议在 `ConfigurationViewModel` 或专门的设置应用协调处统一处理（参照 `LogLevel` 由 `LogLevelService.ApplyFromSettings` 处理的模式），新增 `ThemeService.ApplyFromSettings(registry)`。

---

## 9. WPF 主题资源与热刷新

### 9.1 主题 ResourceDictionary

新增两套资源字典：

```text
MyTools.Desktop/
  Themes/
    Light.xaml
    Dark.xaml
    Shared.xaml          （非颜色的共享样式：字号、圆角、间距，与主题无关）
```

`Light.xaml` / `Dark.xaml` 各自定义第 5.2 节所有 token 的 `SolidColorBrush`（和对应 `Color`），例如：

```xml
<!-- Dark.xaml -->
<ResourceDictionary ...>
    <SolidColorBrush x:Key="SurfaceBackgroundBrush" Color="#1e1e1e" />
    <SolidColorBrush x:Key="SurfaceBrush"           Color="#292929" />
    <SolidColorBrush x:Key="SurfaceAltBrush"        Color="#333333" />
    <SolidColorBrush x:Key="TextPrimaryBrush"       Color="#FFFFFF" />
    <SolidColorBrush x:Key="TextSecondaryBrush"     Color="#CCCCCC" />
    <SolidColorBrush x:Key="TextDisabledBrush"      Color="#666666" />
    <SolidColorBrush x:Key="BorderBrush"            Color="#292929" />
    <SolidColorBrush x:Key="AccentBrush"            Color="#60A5FA" />
    <SolidColorBrush x:Key="SelectionBrush"         Color="#3a3a3a" />
</ResourceDictionary>
```

`Light.xaml` 由设计给出对应浅色值。两套字典的 key 集合必须**完全一致**（构建期可加测试断言）。

### 9.2 应用与切换

1. `App.xaml` 的 `Application.Resources` 合并 `Shared.xaml`，并在最外层保留一个可替换的主题字典位置（通过 `MergedDictionaries` 索引访问）。
2. 在 `MyTools.Desktop`（**不是** `MyTools.Common`）提供一个 `ThemeManager` 静态助手，提供：

```csharp
public static void ApplyTheme(ThemeKind kind)
{
    var dict = Application.Current.Resources.MergedDictionaries;
    var themeDict = dict.FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/") == true);
    var newSource = new Uri($"pack://application:,,,/Themes/{kind}.xaml");
    if (themeDict == null)
        dict.Add(new ResourceDictionary { Source = newSource });
    else
        themeDict.Source = newSource;
}
```

   > 放在 Desktop 而非 Common 的原因：`ApplyTheme` 操作 `Application.Current.Resources`，是 WPF 专属逻辑；`IThemeService`/`ThemeService` 必须保持与 WPF 无关（见 §6.2、§7 第 7 点）。
3. 由于所有控件用 `DynamicResource`，改变 `MergedDictionaries` 中字典的 `Source` 会自动触发全部 `DynamicResource` 刷新，**无需手动遍历窗口**。这是热切换的关键机制。
4. 在 `AppBootstrapper.Init`（构造函数注入的 `themeService` 字段）里订阅 `ThemeChanged`，回调中调用 `ThemeManager.ApplyTheme(e.CurrentTheme)`；并在 `Init` 末尾直接调用一次 `ThemeManager.ApplyTheme(themeService.CurrentTheme)` 应用初始主题（不触发事件）。`ThemeManager` 不持有 `IThemeService` 引用，避免与 `ValidateOnBuild` 校验冲突（见 §7 第 7 点）。

### 9.3 XAML 改造规则

把现有硬编码颜色改为 `DynamicResource`：

```xml
<!-- 改造前 -->
<Border Background="#1e1e1e" ...>
<Style x:Key="ModernTextBox" TargetType="TextBox">
    <Setter Property="Background" Value="#292929" />
    <Setter Property="Foreground" Value="White" />
</Style>

<!-- 改造后 -->
<Border Background="{DynamicResource SurfaceBackgroundBrush}" ...>
<Style x:Key="ModernTextBox" TargetType="TextBox">
    <Setter Property="Background" Value="{DynamicResource SurfaceBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
</Style>
```

要点：

1. **必须用 `DynamicResource` 而非 `StaticResource`**，否则热切换不生效。
2. `Trigger`/`DataTrigger` 中的颜色 `Setter` 同样改为 `DynamicResource`。
3. `DropShadowEffect` 的 `Color`、`Opacity` 不强求主题化（黑底投影在浅色下仍可接受）；如需调整，可新增 `--mt-shadow` 类 token。
4. `StatusDotStyle` 的 Green/Red/Orange 状态色第一轮保留，不改。
5. 需改造的文件清单（首轮回顾，按实际仓库为准）：
   - `Views/SearchWindow.xaml`（ModernTextBox / ModernButton / StatusDotStyle / 各 Border）
   - `Views/ConfigurationWindow.xaml` 及 `Views/Templates/*.xaml`
   - `Views/UpdateCheckWindow.xaml`
   - `Views/HotKeyEditorWindow.xaml`
   - `Components/NodePluginDetailView.xaml`（外层 Border）
   - `Views/Components/Search/BasicListView.xaml`、`DetailedListView.xaml`

### 9.4 ThemeExtension（未来扩展，本轮不实现）

> **本轮不实现**（见 §15 决策 3）。本节保留为后续扩展说明。

对于必须在 code-behind 取色的场景（如非 XAML 构造的控件、`Drawing` 等），可参照 `LocExtension` 提供 `ThemeExtension`：

```xml
<Border Background="{th:Theme SurfaceBackgroundBrush}" />
```

其实现是一个返回 `DynamicResource` 的 `MarkupExtension`，内部用 `DynamicResourceExtension` 转发，保证热刷新。多数情况下直接写 `{DynamicResource Key}` 即可，`ThemeExtension` 主要是缩短拼写与统一前缀。待出现 code-behind 取色需求时再补。

---

## 10. Web 插件：消除首帧闪烁（核心）

### 10.1 根因复述

WebView2 加载 HTML 的时序是：**导航开始 → HTML 解析 → 首帧渲染 → `NavigationCompleted` → 宿主 `PostWebMessageAsJson` 到达**。任何依赖「收到消息后才设置主题」的方案（包括 `initialize-detail` 里带 theme）都必然在首帧之后，从而产生闪烁。因此必须在 **HTML 解析之前、首帧之前** 就把主题写进文档环境。

### 10.2 方案：导航前注入引导脚本

WebView2 提供 `CoreWebView2.AddScriptToExecuteOnDocumentCreationAsync(script)`，注入的脚本满足：

1. 在**文档创建时**同步执行，早于 HTML 解析和首帧。
2. 持久化在环境上，对后续每次导航都生效。

利用它，宿主在导航前注入一段「主题引导脚本」，内容随当前主题变化：

```js
(() => {
  const theme = "dark";                       // 由宿主在注入时替换
  const tokens = { /* 见 10.3 */ };           // 由宿主在注入时替换
  const root = document.documentElement;
  root.setAttribute("data-theme", theme);
  root.style.colorScheme = theme;             // 让原生控件(滚动条/表单)也跟随
  for (const [k, v] of Object.entries(tokens)) {
    root.style.setProperty(k, v);
  }
})();
```

宿主侧实现要点（`NodePluginDetailView`）：

1. 在 `NavigateAsync` 中、调用 `EnsureCoreWebView2Async` 之后、设置 `Source` 之前，先 `AddScriptToExecuteOnDocumentCreationAsync(BuildThemeBootstrapScript(themeService.CurrentTheme))`。
2. `BuildThemeBootstrapScript` 把 `theme` 字符串和当前主题的 token 字典**字面量内联**进脚本（避免脚本再去异步读取，否则又回到首帧之后）。
3. **热切换时**：`ThemeService.ThemeChanged` 触发后，对已加载的页面用 `ExecuteScriptAsync` 重新执行同样的 token 设置逻辑（此时 DOM 已存在，直接更新 `:root` 变量即可），保证热切换生效。同时为下一次导航重新注入更新后的引导脚本。

> 为什么不直接注入 `prefers-color-scheme` 媒体查询？因为 WebView2 默认不暴露系统主题，且我们要的是用户在 MyTools 中显式选择的主题，与系统无关。

### 10.3 Token 下发格式

宿主下发给 Web 的 token 字典与第 5.2 节语义对齐，但用 CSS 变量名：

```json
{
  "theme": "dark",
  "tokens": {
    "--mt-surface-bg": "#1e1e1e",
    "--mt-surface": "#292929",
    "--mt-surface-alt": "#333333",
    "--mt-text": "#ffffff",
    "--mt-text-muted": "#cccccc",
    "--mt-text-disabled": "#666666",
    "--mt-border": "#292929",
    "--mt-accent": "#60a5fa",
    "--mt-selection": "#3a3a3a"
  }
}
```

实现建议：在 `MyTools.Desktop` 维护一个 `WebThemeTokens` 静态映射 `ThemeKind -> Dictionary<string,string>`，**与 `Themes/Light.xaml`、`Dark.xaml` 同源**（可以从 xaml 反查，或两边都由同一份 C# 数据生成；第一轮手写并加测试保证 key 一致）。

### 10.4 宿主 WebView2 容器默认色

除引导脚本外，`NodePluginDetailView.xaml` 的外层 `Border` 背景也要改为 `DynamicResource SurfaceBrush`，使容器底色与主题一致，作为兜底（即便引导脚本异常，也不会出现刺眼的纯白边框）。

---

## 11. 插件协议与主题同步

完全复用 i18n 已建立的两条通道：`initialize-detail`（首帧之后的状态初始化）与 `language-changed`（运行时切换）。主题走平行的两个字段/消息。

### 11.1 WebView2 详情页消息

`initialize-detail` 的 payload 增加（与现有 `locale`、`messages` 并列）：

```json
{
  "theme": "dark",
  "themeTokens": { "--mt-surface-bg": "#1e1e1e", ... }
}
```

新增热切换消息 `theme-changed`（与 `language-changed` 平行）：

```json
{
  "type": "theme-changed",
  "payload": {
    "theme": "light",
    "themeTokens": { ... }
  }
}
```

注意：

1. `initialize-detail` 中的 theme 是**冗余兜底**——首帧主题已由引导脚本设置；这里再次提供是为了让插件 JS 在初始化逻辑里能读到当前主题，不依赖 CSS 变量解析。
2. `theme-changed` 到达时，引导脚本已经把首帧画对了；此消息用于热切换时更新 `:root` 变量并通知插件业务逻辑（如插件按主题切换图标、图表配色等）。

### 11.2 NodePluginDetailView 落点改造

参照现有 `SendInitializeMessage` 与 `OnLocaleChanged`：

1. 构造函数注入 `IThemeService`（已有 `ILocalizationService` 的模式）。
2. `SendInitializeMessage` 的 payload 增加前述 `theme` / `themeTokens`。
3. 新增 `OnThemeChanged` 事件处理，向当前页面 `SendMessage` 一个 `theme-changed`，并在 `OnLoaded`/`OnUnloaded` 期间挂/解 `ThemeService.ThemeChanged`（与 `LocaleChanged` 完全对称）。
4. `NavigateAsync` 注入引导脚本（见 10.2）。

### 11.3 Node RPC

Node RPC 请求参数增加 `theme` 字段，与 `locale` 同级，适用于：

1. `initialize`
2. `search`
3. `invokeAction`
4. `detailEvent`

```json
{ "locale": "zh-CN", "fallbackLocale": "en-US", "theme": "dark", ... }
```

说明：

1. 主题对 Node 后端主要用于「按主题返回不同结构化数据」（极少见），大多数插件后端可忽略此字段。
2. 协议字段**可选**，兼容不处理主题的旧/第三方插件；缺失时插件按自身默认（深色）处理即可。
3. `NodePluginProcessHost`、`NodePlugin.SearchAsync`、`InitializeAsync` 等把 `themeService.CurrentTheme.ToWireString()` 透传，参照现有 `localizationService.CurrentLocale` 的传递路径。

### 11.4 manifest 扩展（可选）

`plugin.json` 可声明主题支持，便于宿主统计与未来校验：

```json
{
  "theme": {
    "supports": ["light", "dark"]
  }
}
```

第一轮**不强制**；未声明视为「依赖宿主默认 token，两主题均可」。`NodePluginManifest` 可暂不新增字段，待有需求再加。

---

## 12. 插件 SDK（web-tool.ts / events.ts）

### 12.1 events.ts

新增主题 subject：

```ts
export const MyToolsEventSubjects = {
  host: {
    initialize: "mytools.host.initialize",
    search: "mytools.host.search",
    key: "mytools.host.key",
    languageChanged: "mytools.host.language-changed",
    themeChanged: "mytools.host.theme-changed",   // 新增
  },
} as const;
```

### 12.2 web-tool.ts

`dispatch` 增加对 `theme-changed` 的识别，参照现有 `language-changed` 分支：

```ts
if (message.type === "theme-changed" && isRecord(message.payload)) {
    applyThemeTokens(message.payload);            // 更新 :root CSS 变量与 data-theme
    handleEvent({
        type: "tool-event",
        subjectId: events.host.themeChanged,
        payload: message.payload
    });
    return;
}
```

并在 `initialize` 事件处理里同样调用 `applyThemeTokens`（兜底）。

新增一个轻量的主题助手（与 `mytoolsI18n` 平级）：

```ts
export const mytoolsTheme = {
    current: "dark",                              // 由 initialize/theme-changed 更新
    tokens: {} as Record<string, string>,
    apply(payload) { /* 设置 data-theme + CSS 变量 */ },
    on(callback) { /* 订阅 themeChanged subject */ }
};
```

### 12.3 插件 CSS 改造

插件作者把硬编码色值改为 `var(--mt-...)`：

```css
/* 改造前 */
body { background: #1e1e1e; color: #ffffff; }

/* 改造后 */
body { background: var(--mt-surface-bg, #1e1e1e); color: var(--mt-text, #ffffff); }
```

要点：

1. **始终为 `var()` 提供 fallback**（如 `var(--mt-surface-bg, #1e1e1e)`），这样即便宿主引导脚本未注入（如脱离宿主单独调试），插件也能呈现合理外观。
2. fallback 值建议取深色，与默认主题一致。
3. 三个示例插件（`hello-search`、`chat`、`deepseek-translator`）需同步改造为参考实现。

---

## 13. 迁移计划

### Phase 0：契约与默认值

1. 在 `MyTools.Common/Theming` 定义 `ThemeKind`、`IThemeService`、`ThemeChangedEventArgs`、`ThemeKindExtensions`。
2. `AppConfig` / `IAppConfig` / `AppConfigService` 增加 `Theme`。
3. 实现 `ThemeService` 并注册到 DI。
4. 加单元测试：`SetTheme` 幂等、事件触发、未知值回退 Dark。

### Phase 1：WPF 主题基础设施

1. 新增 `Themes/Light.xaml`、`Dark.xaml`、`Shared.xaml`，定义第 5.2 节全部 token。
2. 实现 `ThemeManager.ApplyTheme`，在 `AppBootstrapper.Init` 应用初始主题。
3. 订阅 `ThemeService.ThemeChanged` 调用 `ApplyTheme`。
4. 加测试：两套字典 key 集合完全一致。

### Phase 2：WPF 硬编码颜色迁移

1. 按第 9.3 节清单，将 `SearchWindow.xaml` 等文件的硬编码颜色改为 `DynamicResource`。
2. 托盘菜单（`App.xaml.cs` 中 code-behind 构造）如有颜色一并处理。
3. 验证 Dark 主题外观与迁移前完全一致（无回归）。
4. 验证热切换：运行中切到 Light 再切回 Dark，所有窗口即时刷新，无残留。

### Phase 3：设置入口

1. `SettingValueTypes` 增加 `Theme`；新增 `ThemeSettingTemplate.xaml`；更新 `SettingTypeToTemplateConverter`。
2. `AppBootstrapper` 注册 `General.Theme` 设置项。
3. 新增 `ThemeService.ApplyFromSettings`，在 `Init` 中调用；设置变更时调用 `SetTheme`。
4. 托盘菜单增加「主题」快捷子菜单（参照「Language」菜单），下挂 Light / Dark，当前主题打勾，点击即时生效（见 §15 决策 4）。

### Phase 4：Web 主题注入与闪烁消除

1. 在 `MyTools.Desktop` 维护 `WebThemeTokens`（`ThemeKind -> token 字典`），与 xaml 同源、加测试。
2. `NodePluginDetailView`：导航前 `AddScriptToExecuteOnDocumentCreationAsync` 注入引导脚本；外层 `Border` 改为 `DynamicResource`。
3. `SendInitializeMessage` 增加 `theme` / `themeTokens`；新增 `OnThemeChanged` 发送 `theme-changed`。
4. 验证：黑夜主题下打开任意 Web 插件详情页，**首帧即为深色，无白屏闪烁**；切换主题即时生效。

### Phase 5：SDK 与示例插件

1. `events.ts` 增加 `themeChanged`；`web-tool.ts` 增加 `theme-changed` 分支与 `mytoolsTheme` 助手。
2. 把 `hello-search`、`chat`、`deepseek-translator` 的 CSS 改为 `var(--mt-..., fallback)`。
3. 验证三个示例插件在 Light/Dark 下均正确，且脱离宿主单独打开时 fallback 生效。

### Phase 6：Node RPC 主题透传

1. `NodePluginProcessHost`、`NodePlugin` 的 search/invoke/detail/initialize 透传 `theme`。
2. 文档化：Node 后端可忽略 theme；需要时按 theme 返回数据。

---

## 14. 验收标准

### 14.1 宿主

1. 设置页切换 Light ↔ Dark，所有 WPF 窗口（搜索窗、配置窗、更新窗、热键窗、托盘菜单）即时变色，**无需重启**。
2. 重启应用后主题保持上次选择。
3. WPF 中不再有新增的用户可见硬编码颜色（CI 可加简单扫描作为后续目标）。
4. Dark 主题与迁移前视觉一致（无回归）。

### 14.2 Web 插件

1. 在 Dark 主题下打开任意 Web 详情页，**首帧即深色，无白屏闪烁**（肉眼 + 录帧验证）。
2. Light 主题下首帧即浅色，无深色闪烁。
3. 详情页打开状态下切换主题，页面即时跟随，不丢失输入内容和滚动状态。
4. 插件 CSS 使用 `var(--mt-..., fallback)`；脱离宿主单独打开 HTML 时仍可读。

### 14.3 协议与健壮性

1. `theme` 协议字段对旧/第三方插件可选；缺失时插件不崩溃，按默认深色渲染。
2. 未知 theme 值统一回退 Dark，不抛异常。
3. Node 后端忽略 `theme` 字段时功能不受影响。

---

## 15. 已决策项

以下决策在评审中确定，作为本设计的既定约束：

1. **默认主题固定为 `Dark`。** 不读取系统偏好（不查 `AppsUseLightTheme` 注册表项），与当前外观保持一致，避免迁移期视觉跳变。用户首次手动切换前，始终为 Dark。
2. **WPF 状态色（成功/失败/进行中的 Green/Red/Orange）本轮不主题化。** `StatusDotStyle` 等状态色保持跨主题一致；后续若有跨主题可读性问题，再单独引入 `--mt-success` / `--mt-warning` / `--mt-error` 等 token。
3. **`ThemeExtension`（§9.4）本轮不实现。** XAML 中直接使用 `{DynamicResource Key}` 即可满足热刷新；待出现必须在 code-behind 取色的场景再补该 `MarkupExtension`。§9.4 保留为未来扩展说明，不纳入迁移计划。
4. **托盘菜单提供「主题」快捷子菜单。** 与现有「Language」菜单对称，便于快速切换。结构参照 `App.xaml.cs` 的 `languageMenu`：一个父 `MenuItem`，下挂 Light / Dark 两个子项，当前主题打勾；点击调用 `themeService.SetTheme(...)` 即时生效（不走重启路径）。
5. **`WebThemeTokens` 与 `Themes/*.xaml` 采用「手写两份 + 测试断言 key 一致」维护。** 第一轮不引入代码生成。测试断言：两套 xaml 的资源 key 集合与 `WebThemeTokens` 的 CSS 变量名集合（去掉前缀映射后）三者完全一致，新增 token 时若遗漏任一处则构建失败。
6. **主题切换不提供「下次启动生效」的降级路径。** 热切换是本设计的核心目标，与 i18n 当前采用的 `SetLanguageForNextStartup` + 重启路径刻意区分。若后续发现某控件热刷新异常，单独修复该控件，不为整体引入重启降级。

---

## 16. 第三方插件作者最小契约

第三方插件作者只需要做到：

1. Web 详情页 CSS 用 `var(--mt-..., fallback)` 引用颜色，并提供深色 fallback。
2. 如需在 JS 中读取/响应主题，使用 SDK 的 `mytoolsTheme`，不要自行解析 CSS 变量做关键逻辑。
3. 如插件后端确有「按主题返回不同数据」的需求，从 RPC `theme` 字段读取；否则可忽略。

作者**不需要**：

1. 自行定义两套完整配色；可完全依赖宿主下发的 token。
2. 关心 WPF/RESX/ResourceDictionary 实现。
3. 处理首帧闪烁——这由宿主的引导脚本保证。
4. 监听系统主题变化（本轮不支持 Auto）。
