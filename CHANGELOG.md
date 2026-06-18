# Changelog

本文件记录 `ShrinkEventBus` 在当前工作区中的包内变更。

## [1.3.0] - 2026-06-12

### Breaking

- 移除 `EventBase.GetListenerList()`。事件对象不再内置 `ListenerList`，改为持有派发时的快照数组；调试请使用 `GetSubscribers()`（现在返回拷贝）。
- 数字优先级 `0` 现在映射到 `NORMAL`（原为 `LOW`），与枚举重载的默认值保持一致。
- 所有 int 数字优先级重载（`RegisterEvent` / `SubscribeEvent`）不再提供默认值 `0`，必须显式传入数字——消除与枚举重载之间的重载二义性。不带优先级的调用现在唯一解析到枚举重载（`NORMAL`）。

### Fixed

- 织入前会检查类型自身及基类链上是否存在实例 `[EventSubscribe]` 方法，没有的类型不再织入，避免在 `Awake` 中抛出注册异常。
- `AutoRegister`（含 Mod 框架反射路径）遇到没有任何匹配 `[EventSubscribe]` 方法的类型时，改为输出警告并跳过，不再抛异常；显式 `Register()` 仍保持严格抛错。
- `ListenerList` 父子链脏标记传播与快照重建之间的竞态：同一总线内所有监听列表（含父子链）现共用一把锁。
- `RegisterCore` 的 check-then-act 竞态（并发下同一实例可能被注册两次）。
- `EventPool.Release` 的双重归还竞态；对象池增加容量上限（128），超出直接丢弃。
- 父子事件同 phase 内的 handler 顺序：现在按数字优先级跨父子稳定归并（平局时子类型 handler 在前），数字优先级不再只在单层内生效。

### Changed

- 派发热路径重构：事件对象的监听者快照从“逐个带锁二分插入内置 ListenerList”改为一次数组引用赋值；`EventId` 改为懒生成；事件元数据缓存改用 `ConcurrentDictionary`。每个事件实例的构造分配显著减少。
- 删除死代码 `EventCache<T>`（README 此前描述的“泛型静态缓存热路径”自实例化总线改造后已不存在）。
- 恢复模块内 `CodeGen/` 作为可选本地织入管线：命中 `ShrinkShared.CodeGen` 覆盖范围的程序集继续由共享管线优先处理，其余只引用 `ShrinkEventBus.Runtime` 的程序集改由本地 `EventBusILPostProcessor` 兜底，避免 `[EventBusSubscriber]` 自动注册失效。
- `GetEventSubscribers` / `GetAllSubscribersSnapshot` / `EventBase.GetSubscribers` 返回内部快照的拷贝，外部修改不再影响派发顺序。
- `SetPhase` / `IsCanceled` / `Result` 的校验异常现在携带具体错误消息。
- ILPP 的诊断列表与程序集解析器改为调用内局部状态，消除共享实例并发隐患。

## [1.2.0] - 2026-05-18

### Changed

- `MonoBehaviour` 自动注册/反注册的 IL 织入已并入共享 `ShrinkShared.CodeGen` 管线；当前工作区同时保留 `ShrinkEventBus` 本地 `CodeGen/` 作为可选兜底，以兼容纯 `ShrinkEventBus.Runtime` 引用程序集。
- 保留现有 `[EventBusSubscriber]` 用法与自动生命周期行为，不要求业务层改写订阅代码。

## [1.1.6] - 2026-05-04

### Changed

- 运行时总线升级为“默认静态门面 + 可实例化 `IShrinkEventBus`/Builder”双层结构，支持异常策略、事件类型校验、按 phase 分发。
- 手动注册能力从纯 delegate 扩展为 `object / Type / MethodInfo` 三类入口，并在注册阶段严格校验签名、静态/实例模式和事件参数类型。
- `ListenerList` 改为按 phase 分桶 + 快照缓存模型，并支持父事件监听子事件。
- 事件查看器不再反射私有字典，改走公开快照接口。
- `EventBusBenchmark` 扩展为覆盖实例 bus、扫描注册、继承监听、phase 分发、对象池与取消事件跳过等关键场景。

## [1.1.5] - 2026-04-07

### Changed

- 修正 `PrepareForDispatch()`，现在只重置派发期状态，不再误清业务载荷。
- 修正同步 `TriggerEvent(...)` 遇到异步 handler 时的行为，改为向异步处理器分发脱离原对象的事件快照，避免直接复用原事件对象。
- `OnEventTriggered` 与编辑器追踪钩子改为在派发完成后触发，同时覆盖无监听者事件。
- `ShrinkNetworkEventBusBridge` 的异步转发改为先克隆事件，降低桥接层状态污染风险。
- 事件查看器的实时日志改为快照模型，支持关键词过滤、按“有监听者 / 无监听者”筛选、同类事件折叠聚合显示。
- 编辑器菜单迁移到 `ShrinkSDK/事件总线/事件查看器`。
- README 同步更新新的调试入口与同步/异步派发语义说明。
