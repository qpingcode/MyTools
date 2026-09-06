# MyTools 多端配置同步架构设计

> 设计日期：2026-09-02  
> 状态：待实施  
> 涉及仓库：`D:\repos\MyTools`、`D:\repos\MyTools.Hub`

## 1. 背景、概要与整体设计路线

### 1.1 背景

MyTools Desktop 当前以 JSON 文件保存宿主和插件配置：

- 宿主设置：`%AppData%\MyTools.Desktop\Settings.json`；
- 插件设置：`%AppData%\MyTools.Desktop\pluginsData\{pluginId}\settings.json`；
- 手势、插件覆盖等数据另有独立 JSON 文件；
- `CompositeConfigurationStorage` 根据 `PluginId` 将设置路由到不同文件；
- `HubSyncService` 收集文件内容，调用 Hub 的 `GET/PUT /api/sync` 同步整份快照；
- Hub 使用 `SyncSnapshot.PayloadJson` 保存一个用户的整份同步数据。

这个实现适合单机配置，但不能可靠支持多客户端、离线修改和字段级冲突处理：

- 文件写入与同步日志无法在同一事务中提交；
- 整份快照只有一个 revision，不同插件、不同字段也会互相制造冲突；
- 后提交的整份文件可能覆盖另一台设备已经成功写入的修改；
- `FileSystemWatcher` 只能看到文件结果，无法还原一次业务操作的原始意图；
- 数组插入、删除、排序和对象更新无法可靠区分；
- 客户端崩溃或离线重启后，没有可靠的 outbox、checkpoint 和冲突现场；
- 文件名和目录结构进入远端协议后，本地目录调整会变成协议迁移；
- 后续配置 schema migration、审计和历史恢复缺少稳定基础。

启动阶段还存在一个直接问题：自动 Pull 曾以未观察的 fire-and-forget Task 执行，Hub 离线时异常进入全局 `UnobservedTaskException` 并弹窗。同步生命周期和异常策略需要由单一调度器统一负责。

### 1.2 概要

目标方案采用“数据库作为事实来源，JSON 作为文档表示，操作日志作为同步协议”：

- Desktop 使用 SQLite 保存当前配置文档、待上传操作、checkpoint、冲突和设备身份；
- Hub 使用 PostgreSQL 保存配置文档快照、不可变操作日志、revision 历史和设备游标；
- JSON 继续作为灵活配置内容和导入导出格式，但 JSON 文件不再是同步事实来源；
- 每个同步对象由逻辑键标识，不保存客户端或服务器物理文件路径；
- 每次增删改以不可变操作记录，多个操作可组成原子事务；
- 服务端自动合并目标不重叠的操作，同一路径真冲突时拒绝该事务；
- 客户端持久化冲突现场，用户解决后生成新的 resolution 操作；
- 每个插件可声明多个同步文档及各自策略，而不是把整个插件目录作为一个文件上传；
- checkpoint 用于增量拉取，快照与日志压缩用于控制长期存储规模；
- schema version 和确定性 migrator 支持未来配置演进；
- 旧 JSON 插件通过兼容适配器逐步迁移，新插件直接使用结构化配置 API。

### 1.3 整体设计路线

```text
插件 / 设置 UI / 宿主服务
        │
        │ 结构化事务（set/remove/upsert/delete/move）
        ▼
Desktop ConfigurationStore（SQLite，事实来源）
        ├── SyncDocuments：当前物化状态
        ├── SyncOperations：本地 outbox
        ├── SyncConflicts：冲突现场
        └── SyncState：device/checkpoint
        │
        │ SyncScheduler 单 worker：exchange / timeout / retry
        ▼
MyTools Hub Sync API v2
        │
        │ PostgreSQL 事务：校验、合并、追加日志、更新快照
        ▼
PostgreSQL
        ├── SyncDocuments：服务端当前状态
        ├── SyncDocumentRevisions：可回溯基础版本
        ├── SyncOperations：服务端有序操作日志
        └── SyncDevices：设备确认游标
```

实施顺序为：先建立数据库和契约，再迁移宿主配置写入口，然后接入 Hub v2 和调度器，最后开放插件多文档 API、冲突 UI 和日志压缩。旧接口在过渡期只做兼容，不与新旧两套写模型长期并存。

## 2. 目标与非目标

### 2.1 目标

- 两台或多台设备离线修改后能够最终收敛；
- 不同插件、不同文档、不同 JSON 路径的修改尽量自动合并；
- 同一路径真冲突绝不静默覆盖；
- 本地修改和 outbox 操作在一个 SQLite 事务内提交；
- 服务端操作日志和物化快照在一个 PostgreSQL 事务内提交；
- 网络请求可安全重试，不重复执行操作；
- 应用重启后保留待同步操作、checkpoint 和冲突；
- 配置存储不依赖文件路径作为远端身份；
- 插件可声明同步范围、schema、集合 ID、敏感字段和本机字段；
- 支持旧 JSON 配置一次性导入和一段时间的兼容运行；
- 支持数据库 schema migration 和插件配置 schema migration；
- Hub 离线、超时和后台冲突不触发全局异常弹窗。

### 2.2 非目标

- 不允许插件上传任意 SQL 或定义 Hub 数据库表；
- 第一阶段不实现通用 CRDT；
- 第一阶段不自动合并没有稳定 ID 的并发数组编辑；
- 不同步缓存、临时文件、索引、日志和 UI 窗口位置；
- 不以客户端时间戳决定冲突胜负；
- 不在普通同步文档中明文同步 Token、密码或 API Key；
- 不保证永久保存全部历史操作，历史按策略压缩；
- 插件市场 ZIP 包存储与配置同步是两个独立子系统，不进入配置操作日志。

## 3. 架构原则与不变量

1. **数据库是事实来源**：Desktop 的 SQLite 和 Hub 的 PostgreSQL 分别是本地与远端事实来源。
2. **路径不进入数据身份**：远端身份由 `userId + documentType + pluginId + documentId` 决定。
3. **操作不可变**：已经创建或确认的操作不得原地修改；撤销和冲突解决产生新操作。
4. **先持久化再同步**：UI 返回保存成功之前，当前状态与 outbox 必须在同一 SQLite 事务提交。
5. **服务端原子应用**：一个同步事务要么全部追加并更新快照，要么全部拒绝。
6. **幂等**：相同 `operationId` 或相同 `deviceId + deviceSequence` 重试只生效一次。
7. **服务端排序权威**：服务端分配 `serverSequence`；客户端时间只用于展示。
8. **无静默覆盖**：同一目标发生因果并发时返回冲突，不使用无条件 last-write-wins。
9. **删除可传播**：删除使用 tombstone 操作，不能仅依赖“当前快照里不存在”。
10. **迁移确定性**：同一输入和 schema version 必须产生相同迁移结果。
11. **后台失败可恢复**：离线、超时和 5xx 进入重试状态，不弹全局错误框。
12. **秘密默认不同步**：只有显式启用端到端加密的秘密文档才允许进入 Hub。

## 4. 同步对象模型

### 4.1 文档身份

同步单元是逻辑文档，不是文件。文档身份由以下字段组成：

```text
DocumentScope  = host | plugin
DocumentType   = preferences | gestures | plugin-overrides | collection | append-log | opaque
PluginId       = 插件文档必填，宿主文档为空
DocumentId     = scope 内稳定且不可变的标识，例如 preferences、prompts、history
```

示例：

```text
host / preferences
host / gestures
host / plugin-overrides
plugin:chat / preferences
plugin:chat / prompts
plugin:chat / history
plugin:scripts-runner / commands
```

本地可以把 `plugin:chat/preferences` 物化或导出为某个 JSON 文件，但文件路径不进入 API 或 Hub 数据库。

### 4.2 文档类型与合并策略

| 类型 | 适用数据 | 默认操作 | 合并规则 |
|---|---|---|---|
| `json` | 普通对象设置 | `set`、`remove` | 按 JSON Pointer 路径合并 |
| `collection` | 带稳定 ID 的对象集合 | `upsertEntity`、`removeEntity`、`moveEntity` | 按实体 ID，再按字段合并 |
| `append-log` | 历史、记录 | `appendEntity`、`removeEntity` | 按实体 ID 去重，删除用 tombstone |
| `opaque` | 旧插件或无法解析的数据 | `replaceDocument` | 文档级 revision，冲突不自动合并 |
| `local-only` | 缓存、设备 UI 状态 | 无 | 不进入同步系统 |
| `secret` | Token、API Key | 专用秘密操作 | 默认本机；同步时必须端到端加密 |

### 4.3 JSON 的职责

JSON 仍用于：

- `SyncDocuments.ContentJson` 的灵活文档快照；
- 操作中的新值或实体数据；
- 插件 schema 和 migrator 的输入输出；
- 用户导入、导出与诊断。

JSON 不负责：

- 事务；
- revision 和 checkpoint；
- 幂等；
- 冲突状态；
- outbox；
- 数据库迁移。

这些职责由 SQLite/PostgreSQL 和同步服务承担。

## 5. 操作日志模型

### 5.1 基础操作

第一版只支持有限且可验证的操作：

```text
set(path, value)
remove(path)
upsertEntity(collectionPath, entityId, changes)
removeEntity(collectionPath, entityId)
moveEntity(collectionPath, entityId, orderKey)
appendEntity(collectionPath, entityId, value)
replaceDocument(content)
resolveConflict(conflictId, resolutions)
```

读取和查询不进入同步操作日志，因为它们不改变配置状态；如果需要访问审计，应使用独立审计日志。

`path` 使用 RFC 6901 JSON Pointer，例如 `/model`、`/appearance/theme`。不接受任意脚本、表达式或 SQL。

### 5.2 操作结构

```json
{
  "operationId": "01991f25-6e37-7d6f-a4f2-8e0e32f7a001",
  "deviceId": "01JDEVICEA",
  "deviceSequence": 108,
  "transactionId": "01991f25-6e37-7d6f-a4f2-8e0e32f7a000",
  "document": {
    "scope": "plugin",
    "pluginId": "chat",
    "documentId": "preferences"
  },
  "schemaVersion": 3,
  "baseRevision": 12,
  "type": "set",
  "path": "/temperature",
  "value": 0.5,
  "createdAt": "2026-09-02T08:30:00Z"
}
```

约束：

- `operationId` 使用 UUID v7 或 ULID，跨设备唯一；
- `deviceSequence` 在单设备严格递增，并在 SQLite 事务中分配；
- `transactionId` 将一次业务保存中的多个操作绑定为原子事务；
- `baseRevision` 是创建操作时该文档已知的服务端 revision；
- `createdAt` 仅用于展示和诊断，不决定应用顺序；
- 单操作和单事务均有大小及数量上限。

### 5.3 原子事务

设置界面一次“保存”可能修改多个字段，必须在一个事务中记录：

```json
{
  "transactionId": "tx-123",
  "operations": [
    { "type": "set", "path": "/name", "value": "Google" },
    { "type": "set", "path": "/url", "value": "https://google.com/search?q={query}" },
    { "type": "set", "path": "/keyword", "value": "g" }
  ]
}
```

Desktop 在一个 SQLite 事务内更新快照并插入全部操作。Hub 在一个 PostgreSQL 事务内校验全部操作、追加日志并更新文档；任何一个操作冲突或校验失败时整组拒绝。

### 5.4 数组和集合

没有稳定 ID 的数组不能使用下标做并发身份。第一版规则：

- 标量数组和无 ID 对象数组作为一个原子值，双方都修改时产生冲突；
- 带稳定 ID 的对象数组声明为 `collection`；
- `entityId` 在集合内唯一且不可变；
- 排序使用独立 `orderKey`，不使用数组下标；
- 删除记录 tombstone，防止离线设备把对象重新带回。

示例：

```json
{
  "type": "upsertEntity",
  "collectionPath": "/commands",
  "entityId": "open-terminal",
  "changes": {
    "shortcut": "Ctrl+Alt+T"
  }
}
```

## 6. Desktop 本地数据库设计

### 6.1 数据库位置和连接

新增数据库：

```text
%AppData%\MyTools.Desktop\Database\configuration.db
```

使用 `Microsoft.Data.Sqlite`，启用：

```sql
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
```

数据库 migration 在打开业务服务前执行。migration 失败时禁止写入新格式，保留旧文件并显示可恢复错误；不能捕获异常后用空配置继续启动。

### 6.2 表结构

#### ConfigurationDocuments

```sql
CREATE TABLE ConfigurationDocuments (
    Id                 TEXT PRIMARY KEY,
    Scope              TEXT NOT NULL,
    PluginId           TEXT NULL,
    DocumentId         TEXT NOT NULL,
    DocumentType       TEXT NOT NULL,
    SchemaVersion      INTEGER NOT NULL,
    ContentJson        TEXT NOT NULL,
    ContentHash        TEXT NOT NULL,
    RemoteRevision     INTEGER NOT NULL DEFAULT 0,
    BaseContentJson    TEXT NULL,
    SyncStatus         TEXT NOT NULL,
    UpdatedAtUtc       TEXT NOT NULL,
    UNIQUE (Scope, PluginId, DocumentId)
);
```

`SyncStatus` 可取 `Clean`、`Pending`、`Syncing`、`Conflict`、`Blocked`。`BaseContentJson` 保存最后成功同步的基础内容，便于客户端显示三方冲突和在服务端历史已压缩时重新变基。

#### ConfigurationOperations

```sql
CREATE TABLE ConfigurationOperations (
    OperationId        TEXT PRIMARY KEY,
    TransactionId      TEXT NOT NULL,
    DocumentRowId      TEXT NOT NULL,
    DeviceSequence     INTEGER NOT NULL,
    BaseRevision       INTEGER NOT NULL,
    SchemaVersion      INTEGER NOT NULL,
    OperationType      TEXT NOT NULL,
    TargetPath         TEXT NULL,
    EntityId           TEXT NULL,
    PayloadJson        TEXT NULL,
    State              TEXT NOT NULL,
    CreatedAtUtc       TEXT NOT NULL,
    AcknowledgedAtUtc  TEXT NULL,
    FailureCode        TEXT NULL,
    FOREIGN KEY (DocumentRowId) REFERENCES ConfigurationDocuments(Id),
    UNIQUE (DeviceSequence)
);
```

`State` 可取 `Pending`、`Uploading`、`Acknowledged`、`Conflict`、`Rejected`。应用启动时把遗留的 `Uploading` 恢复为 `Pending`，依靠操作幂等安全重试。

#### ConfigurationConflicts

```sql
CREATE TABLE ConfigurationConflicts (
    ConflictId             TEXT PRIMARY KEY,
    DocumentRowId          TEXT NOT NULL,
    TransactionId          TEXT NOT NULL,
    CurrentRemoteRevision  INTEGER NOT NULL,
    RemoteContentJson      TEXT NOT NULL,
    MergedDraftJson        TEXT NOT NULL,
    ConflictItemsJson      TEXT NOT NULL,
    CreatedAtUtc           TEXT NOT NULL,
    ResolvedAtUtc          TEXT NULL,
    FOREIGN KEY (DocumentRowId) REFERENCES ConfigurationDocuments(Id)
);
```

#### SyncClientState

```sql
CREATE TABLE SyncClientState (
    SingletonId             INTEGER PRIMARY KEY CHECK (SingletonId = 1),
    DeviceId                TEXT NOT NULL,
    NextDeviceSequence      INTEGER NOT NULL,
    RemoteUserId            TEXT NULL,
    LastServerCheckpoint    INTEGER NOT NULL DEFAULT 0,
    LastSuccessfulSyncUtc   TEXT NULL,
    LastFailureCode         TEXT NULL
);
```

### 6.3 本地写入流程

`ConfigurationStore.ExecuteTransactionAsync()` 是唯一的同步配置写入口：

1. 开启 SQLite transaction；
2. 读取当前文档和 remote revision；
3. 校验 schema、权限、大小和业务前置条件；
4. 应用操作得到新的 `ContentJson`；
5. 更新 `ConfigurationDocuments`；
6. 分配连续 `DeviceSequence`；
7. 插入 `ConfigurationOperations` outbox；
8. 更新 `SyncClientState.NextDeviceSequence`；
9. 提交 transaction；
10. transaction 提交后发布内存事件并唤醒 `SyncScheduler`。

如果 SQLite 提交失败，UI 保存必须失败，不能只更新内存对象。

### 6.4 读取和通知

- 读取优先访问内存 cache，cache 由 SQLite 初始化；
- 所有 cache 变化都来源于已提交的本地 transaction 或已提交的远端 apply；
- `ConfigurationChanged` 在 transaction 提交后按实际变化发布；
- 远端批次应用后合并发布通知，避免一个批次导致大量重复插件重载；
- 不再依靠 `FileSystemWatcher` 感知新架构中的正常配置修改。

## 7. Hub PostgreSQL 数据模型

### 7.1 SyncDocuments

```text
Id                  uuid PK
UserId              uuid FK
Scope               varchar(16)
PluginId            varchar(64) nullable
DocumentId          varchar(64)
DocumentType        varchar(32)
SchemaVersion       int
Revision            bigint
Content             jsonb
ContentHash         varchar(64)
UpdatedAt           timestamptz
RowVersion          concurrency token
```

唯一索引：`UserId + Scope + PluginId + DocumentId`。对于 nullable `PluginId`，PostgreSQL 索引需使用 `NULLS NOT DISTINCT`，或将宿主文档的 PluginId 规范化为空字符串。

### 7.2 SyncDocumentRevisions

```text
DocumentId          uuid FK
Revision            bigint
SchemaVersion       int
Content             jsonb
ContentHash         varchar(64)
ServerSequence      bigint
CreatedAt           timestamptz
Primary Key (DocumentId, Revision)
```

它为三方合并提供 base。初期每个文档至少保留最近 100 个 revision 和 90 天；达到任一条件后进入压缩候选，但仍需满足设备 checkpoint 安全线。

### 7.3 SyncOperations

```text
ServerSequence      bigint identity PK
OperationId         uuid UNIQUE
UserId              uuid FK
DeviceId            uuid FK
DeviceSequence      bigint
TransactionId       uuid
DocumentId          uuid FK
BaseRevision        bigint
AppliedRevision     bigint
SchemaVersion       int
OperationType       varchar(32)
TargetPath          text nullable
EntityId            text nullable
Payload             jsonb nullable
CreatedAt           timestamptz
AppliedAt           timestamptz
Unique (DeviceId, DeviceSequence)
```

### 7.4 SyncDevices

```text
Id                       uuid PK
UserId                   uuid FK
Name                     varchar(128)
Platform                 varchar(32)
AppVersion               varchar(32)
LastAcknowledgedSequence bigint
LastSeenAt               timestamptz
RetiredAt                timestamptz nullable
```

设备退休后不再阻止日志压缩。连续 180 天未上线的设备可标记为过期；再次上线时必须走快照重建流程。

### 7.5 数据库事务顺序

Hub 应在一个 PostgreSQL transaction 中：

1. 按稳定顺序锁定本事务涉及的文档；
2. 通过唯一键检查操作是否已经应用；
3. 读取 base revision 和当前 revision；
4. 执行三方合并与 schema 校验；
5. 任何真冲突则回滚本同步事务；
6. 插入 `SyncOperations`；
7. 更新 `SyncDocuments` revision 和 content；
8. 插入 `SyncDocumentRevisions`；
9. 更新设备游标；
10. 提交。

多个文档事务必须按 Document Id 排序加锁，避免死锁。捕获 PostgreSQL serialization/concurrency failure 后最多做有限次数服务端重试。

## 8. Sync API v2

### 8.1 Bootstrap

```http
GET /api/sync/v2/bootstrap?afterCheckpoint=0
Authorization: Bearer ...
```

返回：

```json
{
  "serverCheckpoint": 428,
  "device": {
    "id": "...",
    "registered": true
  },
  "documents": [
    {
      "scope": "plugin",
      "pluginId": "chat",
      "documentId": "preferences",
      "documentType": "json",
      "schemaVersion": 3,
      "revision": 12,
      "content": { "model": "gpt-5" },
      "contentHash": "..."
    }
  ]
}
```

Bootstrap 用于首次登录、日志已压缩、设备恢复或本地数据库重建。正常同步不重复下载全量文档。

### 8.2 Exchange

```http
POST /api/sync/v2/exchange
```

请求：

```json
{
  "deviceId": "...",
  "lastServerCheckpoint": 421,
  "transactions": [
    {
      "transactionId": "...",
      "operations": [ /* immutable operations */ ]
    }
  ],
  "maxRemoteOperations": 256
}
```

响应：

```json
{
  "serverCheckpoint": 428,
  "hasMore": false,
  "transactionResults": [
    {
      "transactionId": "...",
      "status": "applied",
      "operationIds": ["..."],
      "documentRevisions": {
        "plugin:chat/preferences": 13
      }
    }
  ],
  "remoteOperations": [ /* sequence > 421, excluding or including own ops均可，但协议必须固定 */ ]
}
```

Exchange 允许一次上传 outbox 并拉取远端增量，减少竞态和网络往返。一次最多 256 个操作、1 MiB 解压后 payload；超限分页。

### 8.3 部分事务冲突

批次中的不同 transaction 相互独立。某个 transaction 真冲突时，该 transaction 返回 `conflict`，其他无关 transaction 仍可成功：

```json
{
  "transactionId": "tx-b",
  "status": "conflict",
  "document": "plugin:chat/preferences",
  "currentRevision": 13,
  "remoteContent": { "timeout": 20 },
  "mergedDraft": { "timeout": 20, "theme": "dark" },
  "conflicts": [
    {
      "path": "/timeout",
      "base": 10,
      "local": 30,
      "remote": 20
    }
  ]
}
```

HTTP 409 适合单事务手动接口；批量 Exchange 使用 200 加 transaction 级状态，避免一个插件冲突阻塞全部同步。

### 8.4 错误码

| code | 行为 |
|---|---|
| `sync_conflict` | 持久化冲突，暂停对应文档自动 Push |
| `snapshot_required` | 下载最新快照并重新变基本地 pending 操作 |
| `schema_too_old` | 运行 migrator 或要求升级插件/客户端 |
| `schema_too_new` | 暂停该文档，要求升级客户端 |
| `operation_invalid` | 标记 rejected，不自动重试 |
| `document_too_large` | 标记 rejected，提示插件作者或用户 |
| `rate_limited` | 按 Retry-After 重试 |
| `unauthorized` | 停止调度，刷新登录状态 |
| `service_unavailable` | 指数退避，保持 outbox |

## 9. 三方合并与冲突处理

### 9.1 三方输入

```text
base   = 操作 baseRevision 对应的历史内容
local  = base 应用客户端事务后的内容
remote = 服务端当前内容
```

对每个目标路径：

- `local == base`：客户端没改，采用 remote；
- `remote == base`：远端没改，采用 local；
- `local == remote`：双方结果相同，采用该值；
- 三者不同：真冲突。

对象路径递归比较；无 ID 数组视为原子值；collection 按 entityId 比较。

### 9.2 服务端行为

- 无冲突：自动合并，提交操作，revision 加一；
- 真冲突：该 transaction 不写数据库、不追加操作、不创建服务端锁；
- 返回 remote、merged draft 和冲突路径；
- 其他客户端可以继续写入，服务端不等待冲突客户端。

### 9.3 客户端行为

- 在 SQLite 中保存完整冲突现场；
- 文档进入 `Conflict`，暂停该文档自动上传；
- Pull 到的新远端操作保存在 remote 分支，不能覆盖用户本地草稿；
- 其他文档继续同步；
- UI 对每个路径提供“本地、远端、手动编辑”；
- 用户确认后基于当前 remote revision 创建新的 resolution transaction；
- 如果解决期间远端再次变化，重新三方合并；
- 冲突解决成功后标记旧冲突 resolved，不能删除历史操作或篡改旧操作。

### 9.4 删除冲突

- 一端删除、另一端未修改：采用删除；
- 一端删除、另一端修改同一对象：真冲突；
- 双方删除：幂等成功；
- tombstone 至少保留到所有活跃设备越过其 checkpoint。

## 10. SyncScheduler 设计

### 10.1 单 worker

Desktop 只允许一个同步 worker 操作网络和同步状态。所有入口都发送信号，不直接 fire-and-forget 调用 Pull/Push：

```text
Startup / SignIn       → RequestImmediateExchange
Local transaction     → RequestDebouncedExchange
Manual sync           → RequestImmediateExchange + await result
Network restored      → RequestImmediateExchange
Shutdown              → StopAsync
```

单 worker 串行执行，合并重复信号。启动和登录立即运行；普通本地修改默认 debounce 1.5 秒。

### 10.2 超时和重试

- 单次 Exchange 默认超时 15 秒；
- 手动同步等待调度结果，但不阻塞 UI 线程；
- 连接失败、超时、429、502、503、504 使用带 jitter 的指数退避；
- 建议间隔：2s、5s、15s、30s、60s，后台上限 5 分钟；
- 普通 4xx、schema 错误和冲突不自动重试；
- 退出时取消 worker 并等待已启动请求结束或取消；
- 所有后台异常在 worker 内观察并记录，不进入全局异常弹窗。

### 10.3 登录生命周期

- 登录成功：绑定远端用户，注册 device，执行 bootstrap/exchange；
- 注销：取消进行中同步，清除远端 token 和绑定，但不删除本地配置；
- 切换账号：旧账号 pending 操作必须明确处理，不能自动发送到新账号；
- 请求携带 user/session generation，响应回来时若账号已切换则丢弃响应并记录；
- 401 停止后台重试，要求重新登录。

## 11. 插件同步契约

### 11.1 manifest 扩展

`plugin.json` 新增可选 `sync`：

```json
{
  "sync": {
    "documents": [
      {
        "id": "preferences",
        "type": "json",
        "schemaVersion": 3,
        "merge": "field",
        "maxBytes": 131072,
        "secretPaths": ["/apiKey"],
        "localOnlyPaths": ["/windowBounds"]
      },
      {
        "id": "prompts",
        "type": "collection",
        "schemaVersion": 2,
        "identityField": "id",
        "merge": "entity"
      },
      {
        "id": "history",
        "type": "append-log",
        "schemaVersion": 1,
        "identityField": "id",
        "retentionDays": 90
      }
    ]
  }
}
```

没有 `sync` 声明的现有 `configuration` 自动映射为：

```text
documentId = preferences
type = json
schemaVersion = 1
merge = field
```

### 11.2 插件 API

新增宿主能力：

```text
sync.readOwn
sync.mutateOwn
sync.transactionOwn
sync.getStatusOwn
sync.resolveConflictOwn（通常只开放给设置 UI，不直接开放普通插件）
```

TypeScript SDK 示例：

```ts
await myTools.sync.transaction("preferences", tx => {
  tx.set("/model", "gpt-5");
  tx.set("/temperature", 0.5);
});

await myTools.sync.upsertEntity("prompts", "translate", {
  name: "翻译",
  prompt: "Translate into Chinese"
});
```

宿主从 `request.PluginId` 确定作用域，插件不能指定其他插件 ID。所有操作在进入 SQLite 前验证 capability、manifest 文档声明、schema 和大小。

### 11.3 现有 configuration API

现有 `configuration.writeOwn` 保留，但内部不再直接写 JSON 文件：

1. 解析当前设置值；
2. 计算每个 setting key 的 set/remove 操作；
3. 在一个 `ConfigurationStore` 事务提交；
4. 更新 registry cache；
5. 唤醒 scheduler。

现有 `IConfigurationStorage` 是同步接口。第一阶段可以实现同步的 SQLite adapter 以减少调用面变化；随后新增异步事务接口，批量写入口必须使用异步接口，避免 UI 线程上长事务。

### 11.4 插件自定义数据

- 插件不能要求同步整个目录；
- 每类业务数据声明为独立 document；
- 缓存、日志、下载文件和索引必须 `local-only`；
- 历史使用 append-log 和 retention；
- 大对象默认不允许进入配置同步；
- 需要同步的二进制数据采用独立 blob 子系统，不编码为巨大 base64 JSON 操作。

## 12. 配置 schema 与迁移

### 12.1 两类 migration

必须区分：

1. **数据库 migration**：SQLite/PostgreSQL 表结构变化；
2. **文档 schema migration**：某个宿主或插件配置内容从 vN 转换到 vN+1。

数据库 migration 分别由 Desktop migration runner 和 Hub EF Core migrations 管理。生产启动使用 `Database.MigrateAsync()`，不再使用 `EnsureCreated()`。

### 12.2 插件 migrator

插件为每个 document 提供连续、确定性的 migrator：

```text
preferences v1 → v2
preferences v2 → v3
```

规则：

- 不允许跳过中间版本，runner 逐级执行；
- 输入输出必须通过对应 JSON Schema；
- 迁移不能依赖网络、当前时间或随机数；
- 迁移失败不覆盖旧文档；
- 迁移作为一个 `replaceDocument` 原子事务写入操作日志；
- 历史操作保持原 schema version，不回写旧日志；
- Hub 只接受其支持范围内的 schema version；过新或过旧返回明确错误。

### 12.3 多版本客户端

第一版采用保守策略：

- 新 schema 声明向后不兼容时，旧客户端暂停该文档同步；
- 旧客户端不得用旧结构覆盖新结构；
- 插件降级前检查本地文档 schema；不能降级则阻止或使用只读模式；
- 后续如需要服务端操作翻译，再为特定 schema 引入显式 translator，不做通用猜测。

## 13. 安全、隐私和配额

### 13.1 秘密

- 默认保存在 Windows Credential Manager；
- 普通配置只保存 credential reference；
- 如果实现跨端秘密同步，使用用户设备持有密钥的端到端加密；
- Hub 数据库只保存密文、算法版本和 key envelope；
- 日志、错误响应和冲突详情必须对 secret path 脱敏；
- secret operation 不允许服务端字段级查看或合并，只能密文级 replace + conflict。

### 13.2 授权

- 所有 Hub 查询必须按 JWT userId 过滤；
- DeviceId 必须属于当前 user；
- 插件 API 只能操作自身命名空间；
- 设置插件的全局读写保留显式 capability；
- 服务端不信任客户端提供的 userId、plugin owner 或 checkpoint。

### 13.3 建议配额

初始默认值：

- 普通文档：128 KiB；
- 单用户所有配置快照：20 MiB；
- 单操作 payload：64 KiB；
- 单同步事务：256 个操作、1 MiB；
- 单 Exchange：256 个远端操作；
- append-log 文档按插件声明 retention，服务端设置硬上限；
- 超限返回稳定错误码，不截断内容。

## 14. 日志压缩与 checkpoint

### 14.1 checkpoint

`serverSequence` 是只增不减的服务端 checkpoint。客户端只在一个远端批次完整应用并提交 SQLite 后更新 `LastServerCheckpoint`。

若应用一半失败，整个 SQLite transaction 回滚，下次从旧 checkpoint 重放。远端操作必须是幂等 reducer。

### 14.2 压缩

服务端定期：

1. 为文档确认当前快照和 hash；
2. 计算所有活跃设备的最小确认 checkpoint；
3. 保留该安全线之后的操作；
4. 对安全线之前的操作按保留期删除；
5. 保留必要 revision 供冲突和审计；
6. 清理已过期 tombstone。

如果客户端请求的 checkpoint 已被压缩，返回 `snapshot_required` 和最新 snapshot token。客户端不能把全量快照直接覆盖 pending 本地修改，而要：

1. 保存 pending 操作；
2. 安装远端快照为新的 base；
3. 重新应用 pending 操作；
4. 产生冲突则进入 Conflict；
5. 无冲突则重新提交。

## 15. JSON 文件兼容与数据迁移

### 15.1 Desktop 首次迁移

新增 `LegacyConfigurationImporter`，在 SQLite migration 完成后执行一次：

1. 获取跨进程迁移锁；
2. 读取 `Settings.json`、`Gestures.json`、`PluginOverrides.json` 和插件 settings；
3. 严格解析并记录无法识别的项，不允许静默清空；
4. 映射为 host/plugin 文档；
5. 在一个 SQLite transaction 中写入文档；
6. 写入 `LegacyImportVersion` marker 和源文件 hash；
7. 将原文件复制到带时间戳的 `migration-backup`；
8. 提交后从 SQLite 初始化 registry；
9. 暂时保留文件，不立即删除。

重复启动时 importer 根据 marker 和 hash 保证幂等。迁移成功并稳定至少一个发布周期后，停止写旧文件。

### 15.2 兼容阶段

推荐只做单向兼容：

- SQLite 是唯一写源；
- 可选将 SQLite 快照导出到旧 JSON，供旧插件只读；
- 不允许 SQLite 和 JSON 双向同时写，否则无法确定权威顺序；
- 检测到外部修改旧文件时，明确提示重新导入或由 legacy adapter 生成 `replaceDocument`，不能悄悄覆盖。

### 15.3 Hub 旧数据迁移

当前 `SyncSnapshot.PayloadJson` 的文件键需迁移为逻辑文档：

```text
Settings.json                         → host/preferences
Gestures.json                         → host/gestures
PluginOverrides.json                  → host/plugin-overrides
pluginsData/{pluginId}/settings.json  → plugin:{pluginId}/preferences
```

Hub migration 分两步：

1. 建立 v2 表并保留旧表；
2. 应用级 migration 逐用户解析旧 PayloadJson，写入 `SyncDocuments` 初始 revision 和 baseline revision 记录。

迁移记录用户、源 hash、目标 hash 和状态，支持断点续跑。全部用户验证后再通过后续 EF migration 删除旧列/旧表。不要在一个不可恢复的 SQL migration 中直接丢弃旧 payload。

### 15.4 API 过渡

- Desktop 新版本优先探测 `/api/sync/v2/capabilities`；
- Hub 支持 v1 只读或有限期双协议；
- 同一用户一旦完成 v2 migration，v1 PUT 必须拒绝，避免旧客户端覆盖 v2 状态；
- 返回明确的 `client_upgrade_required`；
- v1 退役日期和最低 Desktop 版本写入 capability 响应。

## 16. 插件市场二进制存储

配置同步不应承载插件 ZIP。根据“Hub 信息均进入数据库、不保存服务器物理文件路径”的约束，插件市场另行迁移：

- `PluginVersion.PackagePath` 替换为 `PackageContent bytea`；
- 同时保存 `ContentHash`、`FileSize`、`ContentType`；
- 发布时在内存/临时受控流中校验 ZIP，数据库 transaction 成功后才返回；
- 下载直接从数据库返回只读 stream；
- README、本地化名称等可在发布时解析为结构化列，避免列表查询加载整个 bytea；
- 对现有 PackagePath 先增加 nullable `PackageContent`，由应用 migration 读取旧文件、验证 SHA-256 后回填；
- 所有记录回填并验证后，再通过下一次 migration 将 `PackageContent` 设为 NOT NULL 并删除 `PackagePath`；
- 回填失败的记录保持可重试状态，不能先删除原 ZIP。

如果未来包体和流量显著增长，可再评估对象存储；这不改变配置同步协议。

## 17. 具体代码修改

### 17.1 `D:\repos\MyTools`：Common 契约

新增建议目录：

```text
MyTools.Common/Sync/
  SyncDocumentKey.cs
  SyncDocumentDescriptor.cs
  SyncOperation.cs
  SyncTransaction.cs
  SyncConflict.cs
  SyncErrorCodes.cs
  IConfigurationDocumentStore.cs
```

修改：

- `MyTools.Common/Config/Interfaces/IConfigurationStorage.cs`
  - 短期由 SQLite adapter 实现现有接口；
  - 长期增加批量事务接口，避免逐项 `Store()` 产生多事务。
- `MyTools.Common/Config/Enums/SettingOptions.cs`
  - 增加 `LocalOnly`、`Secret`；
  - 不要用 `Hidden` 代替秘密语义。
- `MyTools.Common/Config/Models/SettingSchema.cs`
  - 集合 schema 增加稳定 ID、排序键和同步策略描述。
- `MyTools.Common/Config/ConfigPath.cs`
  - 增加 `ConfigurationDatabasePath`；
  - 旧 `PluginSettingsPath` 标记 legacy/export-only，不再作为同步身份。

### 17.2 `D:\repos\MyTools`：Desktop 存储

新增建议目录：

```text
MyTools.Desktop/Storage/Configuration/
  ConfigurationDbContext.cs（或轻量 SQL repository）
  ConfigurationDatabaseMigrator.cs
  SqliteConfigurationStore.cs
  ConfigurationOperationReducer.cs
  LegacyConfigurationImporter.cs
  ConfigurationExportService.cs
```

修改：

- `MyTools.Desktop/Storage/CompositeConfigurationStorage.cs`
  - 改为 SQLite-backed adapter；
  - 删除正常流程中的按插件创建 `JsonConfigurationStorage`；
  - 保留 legacy importer/exporter 的有限调用。
- `MyTools.Desktop/Storage/JsonConfigurationStorage.cs`
  - 标记 legacy；
  - 不再吞掉解析和保存异常后假装成功；
  - 仅用于迁移、导入导出测试。
- `MyTools.Desktop/Services/ConfigurationRegistry.cs`
  - `SaveChanges()` 聚合 dirty settings 为一个事务；
  - SQLite 提交后才清理 dirty 和发布事件；
  - 增加远端批次应用入口，避免逐项反复保存。
- `MyTools.Desktop/DesktopServiceCollectionExtensions.cs`
  - 注册 SQLite connection factory、store、migration runner、scheduler 和 conflict service。

### 17.3 `D:\repos\MyTools`：Desktop 同步

建议替换/新增：

```text
MyTools.Desktop/Services/Sync/
  SyncScheduler.cs
  SyncExchangeClient.cs
  SyncOutboxService.cs
  RemoteOperationApplier.cs
  SyncConflictService.cs
  SyncStatusService.cs
  SyncBootstrapService.cs
```

修改：

- `MyTools.Desktop/Services/HubSyncService.cs`
  - 当前整文件 Collect/Apply 逻辑退役；
  - 临时可作为 facade，最终由上述服务替代；
  - 删除正常路径的 FileSystemWatcher 和直接文件写入；
  - 所有网络操作进入单 worker scheduler。
- `MyTools.Desktop/Services/HubApiClient.cs`
  - 增加 v2 DTO 和稳定的 `HubApiException(StatusCode, ErrorCode, Body)`；
  - 请求接受 cancellation token 和统一超时；
  - 409、429、schema 错误保留结构化响应。
- `MyTools.Desktop/AppBootstrapper.cs`
  - 启动只调用 `SyncScheduler.Start/SignalImmediate`；
  - 不直接 `_ = PullAsync(...)`；
  - migration/import 完成后再初始化 registry 和插件。
- `MyTools.Desktop/Services/HubAccountService.cs`
  - 登录、注销、切换账号通知 scheduler；
  - 注销取消当前 generation 的请求。
- `MyTools.Desktop/Services/GlobalExceptionHandler.cs`
  - 不用全局 handler 承担预期同步错误；
  - scheduler 必须观察自身所有 Task。

### 17.4 `D:\repos\MyTools`：插件协议和 SDK

修改：

- `MyTools.Plugins/NodePlugins/NodePluginManifest.cs`
  - 增加 `SyncDocuments` 描述；
- manifest DTO/validator
  - 校验 document ID、类型、schema version、identity field、secret/local-only path 和配额；
- `MyTools.Plugins/NodePlugins/HostCallProtocol.cs`
  - 增加同步 document/operation/transaction DTO；
- `MyTools.Desktop/Services/SettingsPluginHostCallHandler.cs`
  - 将 `configuration.writeOwn` 转成 SQLite 原子操作；
  - 增加 `sync.readOwn`、`sync.mutateOwn` 等路由；
- `MyTools.Plugins/Examples/sdk-v3`
  - 增加 TypeScript sync API、类型和错误码；
- 示例插件
  - 至少选择 chat、quick-text、scripts-runner 各实现一种 json/collection/复杂事务示例。

### 17.5 `D:\repos\MyTools.Hub`：数据层

修改：

- `src/MyTools.Hub.Api/Data/Entities.cs`
  - 用 `SyncDocument`、`SyncDocumentRevision`、`SyncOperation`、`SyncDevice` 替代单一 `SyncSnapshot`；
  - 插件包后续以 bytea 替代 `PackagePath`。
- `src/MyTools.Hub.Api/Data/HubDbContext.cs`
  - 配置唯一索引、JSONB、并发 token、删除行为和 sequence；
- `src/MyTools.Hub.Api/Data/Migrations/*`
  - 使用正式 EF migrations；
  - 增加可恢复的数据回填状态表；
  - 不再使用 `EnsureCreated()` 或启动时临时 ALTER。
- `src/MyTools.Hub.Api/Program.cs`
  - 启动执行 `MigrateAsync()`；
  - 注册后台压缩/迁移 worker；
  - 数据回填未完成时 capability 返回迁移状态。

### 17.6 `D:\repos\MyTools.Hub`：服务和 API

新增建议目录：

```text
src/MyTools.Hub.Api/Sync/
  SyncExchangeService.cs
  SyncMergeEngine.cs
  SyncOperationReducer.cs
  SyncDocumentRepository.cs
  SyncCompactionService.cs
  SyncMigrationService.cs
  SyncContracts.cs
```

修改：

- `Controllers/SyncController.cs`
  - 保留 v1 兼容入口；
  - 新增 v2 capability/bootstrap/exchange；
- `Services/SyncService.cs`
  - 旧整份 PayloadJson 服务退役；
  - 不继续扩展为复杂 v2 服务，避免单类承担合并、存储和迁移；
- `Services/PluginMarketService.cs`
  - 插件包迁移到数据库后改为 bytea 发布/下载；
  - 列表查询不得加载包内容。

## 18. 实施阶段

### Phase 0：契约与测试基线

- 冻结 v1 行为；
- 定义 document key、operation、transaction、error code；
- 编写 reducer 和三方合并纯函数测试；
- 建立 Desktop/Hub migration 测试环境；
- 暂不切换生产写入口。

### Phase 1：Desktop SQLite 事实来源

- 建表、migration runner、repository；
- JSON 一次性 importer 和备份；
- `ConfigurationRegistry` 改为事务保存；
- 保持远端同步关闭或仍走只读 v1；
- 验证重启、崩溃恢复和旧配置完整性。

### Phase 2：插件基础配置接入

- 现有 `configuration.writeOwn` 生成结构化操作；
- manifest 默认映射 preferences 文档；
- 增加 local-only/secret 标记；
- 示例插件迁移；
- JSON 文件降级为导出/兼容层。

### Phase 3：Hub v2 数据模型和 API

- 新建 v2 表和 EF migrations；
- 实现幂等、事务、revision、checkpoint；
- 实现 merge engine 和 transaction 级冲突；
- v1 数据回填到 v2；
- capability 控制客户端切换。

### Phase 4：Desktop Scheduler 和 Exchange

- 单 worker、15 秒超时、退避、状态；
- outbox 上传和增量拉取；
- bootstrap/snapshot-required；
- 登录生命周期；
- 删除原整文件 watcher 同步路径。

### Phase 5：冲突 UI

- 设置插件展示文档和字段级冲突；
- 本地/远端/手动选择；
- resolution transaction；
- 冲突期间继续同步其他文档；
- 重启后恢复冲突现场。

### Phase 6：插件多文档和 SDK

- collection、append-log、排序和 retention；
- 插件自定义 schema migrator；
- SDK 文档、示例和兼容性检查；
- 配额和权限完善。

### Phase 7：压缩、退役 v1 和包数据迁移

- 设备管理、过期和日志压缩；
- 退役 v1 PUT，再退役 v1 GET；
- 清理旧 JSON 双写；
- 插件 ZIP 从 PackagePath 回填至 PostgreSQL bytea；
- 验证备份恢复和跨版本升级。

## 19. 测试计划

### 19.1 纯函数测试

- JSON Pointer set/remove；
- base/local/remote 三方合并全部分支；
- 不同路径自动合并；
- 同一路径冲突；
- 删除与修改冲突；
- collection 按稳定 ID 合并；
- 无 ID 数组原子冲突；
- reducer 幂等；
- schema migrator 确定性。

### 19.2 Desktop 集成测试

- SQLite transaction 同时提交快照和 outbox；
- 提交失败时 cache、dirty 和事件保持一致；
- 应用崩溃后 pending 操作恢复；
- scheduler 合并信号且不并发请求；
- Hub 离线、超时不弹全局错误；
- 401 停止同步；
- 登录切换不串账号；
- 冲突持久化和重启恢复；
- legacy JSON 导入幂等、失败回滚和备份。

### 19.3 Hub 集成测试

- 相同 operationId 重试仅应用一次；
- 相同 device sequence 不得对应不同 payload；
- 并发 PUT 只有合法事务成功；
- 不同路径并发自动合并；
- 同一路径并发返回冲突且数据库不变；
- 多操作 transaction 全成或全败；
- checkpoint 分页无丢失、无重复；
- snapshot-required 后可恢复；
- migration 从旧 PayloadJson 正确映射文档；
- PostgreSQL JSONB、唯一索引和并发 token 使用真实 PostgreSQL 测试，不只使用 EF InMemory。

### 19.4 端到端场景

1. A、B 从同一 checkpoint 修改不同插件，最终均保留；
2. A、B 修改同一插件不同字段，自动合并；
3. A、B 修改同一字段，B 收到冲突并手动解决；
4. A 删除集合元素，B 离线修改该元素，产生明确冲突；
5. B 离线超过日志保留期，使用快照重建后重新应用 pending 操作；
6. 网络在服务端提交后、客户端收响应前断开，重试不重复执行；
7. 应用在 SQLite 提交后、唤醒 scheduler 前退出，重启仍能上传；
8. 插件 schema 升级后旧客户端不能覆盖新结构；
9. Hub 离线启动，Desktop 正常可用且只记录限频 warning。

## 20. 可观测性和运维

客户端结构化日志字段：

```text
deviceId
remoteUserId（脱敏）
transactionId
operationCount
lastCheckpoint
newCheckpoint
documentKey
syncResult
durationMs
retryAttempt
errorCode
```

服务端指标：

- Exchange 请求量、延迟和 payload；
- applied/conflict/rejected transaction 数；
- 每用户 pending 日志规模；
- snapshot-required 次数；
- migration 成功/失败/待处理数量；
- 文档和历史存储量；
- 幂等重试命中数；
- 压缩删除量和最慢设备 checkpoint。

日志不得记录完整配置、秘密值或冲突中的 secret path 内容。

## 21. 验收标准

### 21.1 数据正确性

- SQLite 是 Desktop 配置唯一事实来源；
- PostgreSQL 是 Hub 同步数据唯一事实来源；
- 远端数据库不保存 Desktop 物理配置路径；
- 本地写入和 outbox 原子提交；
- 服务端日志和快照原子提交；
- 重试不重复执行；
- 真冲突不覆盖远端或本地内容。

### 21.2 多端行为

- 不同文档和不同字段的并发修改自动合并；
- 同字段冲突可在客户端恢复、展示和解决；
- 冲突文档不阻塞其他文档；
- 长期离线客户端能通过 snapshot-required 恢复；
- 删除不会被旧设备无意复活。

### 21.3 可靠性

- Hub 不运行时 Desktop 启动不弹异常；
- 单请求超时生效；
- worker 关闭时无未观察 Task；
- 客户端和服务端 migration 可重复执行或安全恢复；
- 旧 JSON 导入失败不会丢失源文件；
- v1 客户端不能覆盖已进入 v2 的用户数据。

### 21.4 插件生态

- 未声明 sync 的旧插件基础配置仍可使用；
- 新插件能声明多个文档；
- collection 必须声明稳定 ID；
- local-only 和 secret 数据不会进入普通同步日志；
- SDK 能以一次原子事务修改多个设置。

## 22. 已决策项

- 使用 SQLite/PostgreSQL，而不是 JSON 文件作为事实来源；
- JSON/JSONB 继续作为灵活配置文档表示；
- 使用操作日志 + 物化快照，不采用纯事件回放作为每次读取方式；
- 服务端排序和 checkpoint 权威；
- 不同路径自动合并，同路径真冲突交给客户端；
- 服务端遇到真冲突不写入、不等待、不持锁；
- 客户端持久化冲突并以新操作解决；
- 数组只有稳定 ID 时才支持实体级自动合并；
- 文件路径不作为远端文档身份；
- FileSystemWatcher 仅用于短期 legacy adapter；
- 后台同步统一由单 worker scheduler 管理；
- 数据库 migration 和文档 schema migration 分离；
- 插件 ZIP 不进入配置日志，按独立 bytea 迁移处理。

## 23. 实施前仍需确认的参数

这些参数不影响总体架构，但应在 Phase 0 固化：

- 历史 revision 保留数量和天数；
- 设备自动过期天数；
- 普通文档、单事务和单用户配额；
- v1 API 兼容期限；
- 是否首期实现端到端秘密同步，默认建议不实现；
- 冲突 UI 是否允许“全部使用本地/远端”；
- 插件包 bytea 回填后的旧 ZIP 保留周期；
- serverSequence 使用全局序列还是按用户序列，默认建议全局序列并按 user 过滤。
