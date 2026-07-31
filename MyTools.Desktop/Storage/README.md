# 配置存储系统

本项目支持两种配置存储方式：

## 1. SQLite配置存储 (SqliteConfigurationStorage)

- **文件位置**: `ConfigPath.DatabasePath/settings.db`
- **特点**: 使用SQLite数据库存储，支持事务和并发访问
- **适用场景**: 需要高性能、并发访问的场景

## 2. JSON配置存储 (JsonConfigurationStorage) ⭐ 推荐

- **文件位置**: `ConfigPath.Base/Settings.json`
- **特点**: 使用JSON文件存储，人类可读，易于编辑和版本控制，基于Newtonsoft.Json
- **适用场景**: 开发环境、配置调试、版本控制

## 使用方法

### 在依赖注入中注册

```csharp
// 使用SQLite存储
services.AddSingleton<IConfigurationStorage, SqliteConfigurationStorage>();

// 或使用JSON存储（推荐）
services.AddSingleton<IConfigurationStorage, JsonConfigurationStorage>();
```

### 配置文件格式

JSON配置文件采用以下格式：

```json
[
  {
    "name": "配置项名称",
    "value": "配置值",
    "lastModified": "2024-01-01T00:00:00.000Z"
  }
]
```

### 配置项命名规范

建议使用点分隔的层次结构：

- `App.General.Language` - 应用程序通用语言设置
- `App.HotKey.GlobalSearch` - 全局搜索快捷键
- `App.Search.MaxResults` - 搜索结果最大数量
- `App.UI.WindowOpacity` - UI窗口透明度
- `App.Plugins.EnabledPlugins` - 启用的插件列表

## 优势对比

| 特性 | SQLite | JSON |
|------|--------|------|
| 性能 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| 可读性 | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| 可编辑性 | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| 版本控制 | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| 并发安全 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| 文件大小 | 小 | 中等 |

## 注意事项

1. **JSON存储**: 所有值都存储为字符串，类型转换由`ConfigurationService`处理
2. **JSON库**: 使用Newtonsoft.Json进行序列化和反序列化，支持丰富的配置选项
3. **并发访问**: JSON存储使用文件锁确保线程安全
4. **错误处理**: 如果JSON文件损坏，会自动创建新的空配置文件
5. **备份**: 建议定期备份`Settings.json`文件

## 迁移指南

从SQLite迁移到JSON：

1. 停止应用程序
2. 备份SQLite数据库文件
3. 修改依赖注入配置，使用`JsonConfigurationStorage`
4. 启动应用程序，配置会自动迁移

## 示例配置

参考 `Settings.json.example` 文件查看完整的配置示例。
