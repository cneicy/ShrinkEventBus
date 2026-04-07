# Changelog

本文件记录 `ShrinkEventBus` 在当前工作区中的包内变更。

## [1.1.5] - 2026-04-07

### Changed

- 修正 `PrepareForDispatch()`，现在只重置派发期状态，不再误清业务载荷。
- 修正同步 `TriggerEvent(...)` 遇到异步 handler 时的行为，改为向异步处理器分发脱离原对象的事件快照，避免直接复用原事件对象。
- `OnEventTriggered` 与编辑器追踪钩子改为在派发完成后触发，同时覆盖无监听者事件。
- `ShrinkNetworkEventBusBridge` 的异步转发改为先克隆事件，降低桥接层状态污染风险。
- 事件查看器的实时日志改为快照模型，支持关键词过滤、按“有监听者 / 无监听者”筛选、同类事件折叠聚合显示。
- 编辑器菜单迁移到 `ShrinkSDK/事件总线/事件查看器`。
- README 同步更新新的调试入口与同步/异步派发语义说明。
