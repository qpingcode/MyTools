# 插件消息总线 v3 二期设计：取消、订阅与完整背压

> 本文档是 v3 设计拆分后的第二期，在[第一期核心](2026-08-14-plugin-bus-v3-core.md)之上追加可靠性机制。所有机制对第一期协议表面向后兼容：envelope 不变，只启用保留的 route 名并细化调度规则。触发条件见各节开头——**没有出现对应症状之前不实施**。

## 范围

1. `bus.cancel`：请求取消传播。
2. `bus.subscribe` / `bus.unsubscribe`：事件订阅过滤与状态型事件的 revision 协议。
3. 三通道背压模型与 entry 级执行预算。
4. 端到端超时预算传播。

心跳不在本期范围：Host→Node 单向 ping 和两侧的被动看门狗在第一期已是完整形态，本期没有增量。

## 取消（bus.cancel）

**触发条件**：出现真正长时间运行、用户需要中途放弃的插件操作（如大文件处理、外部 API 流式调用）。

- `bus.cancel`：尽力取消 `correlationId` 指向的请求；以 `event` 形式发送，自身不产生响应，结果通过原请求的最终响应（`Cancelled` 或正常完成）体现。
- 取消不依赖 `correlationId` 难以猜测：宿主校验取消方必须是原请求的发起 endpoint 且属于同一 session，不匹配的取消直接丢弃、不影响原请求，并记录限频的安全诊断。
- 请求超时、WebView 关闭或用户取消时，总线向仍在运行的下游发送 `bus.cancel`；Node SDK 将其映射为 handler 的 `AbortSignal`；宿主 capability 侧对称地映射为 `CancellationToken`。
- 取消可能与正常完成竞态，已完成或无法中断的操作不回滚。

## 订阅与状态型事件

**触发条件**：事件量或页面数增长到广播造成可测量的浪费，或出现页面只需要部分事件流的真实场景。

- `bus.subscribe`、`bus.unsubscribe` 管理当前 endpoint 的事件订阅。启用订阅后，事件只发给同一插件会话中已订阅的 endpoint；未显式订阅的 endpoint 不再收到广播（SDK 升级时提供"订阅全部"的兼容模式）。
- 订阅只存在于当前 endpoint 连接中，宿主不跨连接持久化。WebView 页面重载后由页面初始化代码重新订阅；Node 重启后由新进程重新注册 handler 和订阅。
- 状态型 `host.event.*` 路由的快照和事件都携带该状态的单调递增 revision。宿主在该状态的串行发布上下文中原子完成订阅注册与快照生成，保证同一连接上快照先于其后的所有事件；订阅方丢弃 revision 不大于已应用值的消息，兜底注册竞态。
- 事件通道的丢弃或合并可能造成 revision 空洞，订阅方检测到空洞时重新读取快照；因此状态型路由的溢出策略必须选择丢弃最旧或按 key 合并，不得丢弃最新。

## 三通道背压与执行预算

**触发条件**：简化模型（pending 上限 + 有界事件队列）在实际负载下出现响应被事件拥塞、或"请求→取消"模式绕过并发上限的问题。取消机制启用后，执行预算规则**必须**随之启用（见下）。

消息方向与消息类型正交。Host → Node 和 Node → Host 两个方向都可能承载 request、response、event 和 control，不能按方向推断处理优先级。消息归入哪类通道、使用什么优先级和背压策略由 route 语义决定，不能仅根据 `kind` 判断：例如 `bus.cancel` 的 kind 是 event，但按 route 归入 control/response 通道，不受事件溢出策略影响。

每个 endpoint——Node 和 WebView 同样适用——在每个方向都使用三类独立、有界的逻辑通道。出站通道最终由该连接的单写者按优先级写入 transport；入站消息由读侧分类后按相同优先级调度：

1. **control/response 通道**：承载 ping/pong、取消和与现有 pending request 匹配的响应。其保留容量至少覆盖允许的最大 pending request 数。未知 `correlationId` 的响应直接丢弃并记录。匹配响应不能因事件拥塞被丢弃；若对端长期不读取导致保留通道也耗尽，则关闭连接并让 pending request 以 `TransportDisconnected` 失败，不能扩展为无界队列。
2. **request 通道**：发往 Node 的 `plugin.call.*`（无论由 WebView 还是宿主发起）与 Node 发出的 `host.call.*` 分别限制 in-flight 和待处理数量，由两个作用域不同的独立计数共同实施。**pending 计数按 endpoint 记**：约束调用方的响应关联表，在响应、取消、超时或断线时释放，随 endpoint 生灭，endpoint 断开即全部以 `TransportDisconnected` 释放。**执行计数按 `pluginId + entryId` 记，跨 generation 和 session 保留**：在 handler 或 capability 实际开始时增加，只有其真正结束（包括被取消或超时后 abort 收尾完成）才释放——取消和超时不释放执行槽位，否则"请求→取消"循环可绕过并发上限制造无限后台任务。新请求按执行计数准入，默认每 entry 每方向最多 64 个在执行 request，可由 Host Core 下调；超限请求不进入 transport 或 handler 队列，直接返回 `TooManyRequests`。执行计数跨代保留意味着：主 Node 断线重启后，旧 session 发起、仍在宿主 capability 中执行且无法立即中断的残留工作继续占用该 entry 的执行槽位直到结束，新会话请求在此期间可能收到 `TooManyRequests`，这是有意的背压而非缺陷。
3. **event 通道**：承载 `plugin.event.*` 和 `host.event.*`。事件路由必须声明丢弃最新、丢弃最旧或按 key 合并的溢出策略。所有丢弃和合并都增加 `droppedEvents` 或 `coalescedEvents` 诊断计数，不向发布方伪装为可靠投递。

除各通道消息数上限外，每个 endpoint 还限制队列总字节数，任何队列都不能无界增长。

WebView2 transport 的对称规则：宿主在反序列化前先检查消息字节长度，超过全局 `MaxFrameBytes` 的消息不解析、直接丢弃并记录诊断；在全局上限内但超过路由 payload 上限的返回 `MessageTooLarge`。WebView 发起的请求同样受 pending 计数和 entry 级执行计数准入。对持续发送非法或超限消息的页面，宿主对应"关闭连接"的动作是重载或停用该 WebView endpoint 并记录安全诊断。宿主 → WebView 的事件队列有界并执行路由声明的溢出策略，慢页面不能阻塞总线或无界占用内存。

## 超时预算传播

**触发条件**：出现多跳链路上"下游还在做无用功"造成的实际资源浪费，或需要对端到端时延做出承诺。

`timeoutMs` 语义从每跳独立超时升级为**端到端剩余预算**：每条转发链路使用单调时钟扣减 `timeoutMs`，下游预算耗尽时返回 `RequestTimeout` 并尽力取消仍在执行的工作。预算传播只能缩小超时与副作用之间的竞态，不能撤销已经提交的外部副作用。副作用 capability 必须在提交前再次检查预算；超时后的结果可能未知，调用方不得自动重试。

该升级只改变 `timeoutMs` 的解释方式，不改变 envelope；新旧 SDK 在次版本协商下可共存（旧 SDK 按每跳超时理解，行为退化但正确）。

## 测试增补

在第一期测试之上增加：

- 预算传播和尽力取消
- 非发起 endpoint 或跨 session 的 `bus.cancel` 被丢弃且不影响原请求
- topic 订阅、快照和插件隔离；订阅注册与状态变更并发时快照与事件的 revision 顺序
- 每 endpoint 三类通道、双向 in-flight、响应保留容量和事件溢出策略
- WebView 入站消息大小检查、请求准入和出站事件队列溢出
- "请求→立即取消"循环下执行计数不泄漏、并发不超过上限
- 重启后旧 session 的残留执行工作继续占用 entry 级执行槽位、结束后正确释放给新会话
- 取消竞态（取消与正常完成并发）
- 长稳与压力：事件洪水下的内存上限和丢弃行为、慢 WebView、反复崩溃重启后的句柄泄漏
- 混沌：响应乱序、帧分片、连接中断和重启期间的旧 session 响应
