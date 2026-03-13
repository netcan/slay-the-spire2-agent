# STS2 mod 与 agent 协议一致性检查

## 已对齐字段

当前 mod-side 原型已对齐 `src/sts2_agent/models.py` 中的关键字段：

- `session_id`
- `decision_id`
- `state_version`
- `phase`
- `player`
- `enemies`
- `rewards`
- `map_nodes`
- `terminal`
- `action_id`
- `type`
- `params`
- `target_constraints`

同时补充了 `compatibility` 字段，用于暴露：

- `protocol_version`
- `mod_version`
- `game_version`
- `provider_mode`
- `read_only`
- `ready`

## 当前差异

- Python agent 原型使用 `metadata` 字段承载环境细节；mod-side 也保留了该字段，但真实接入后需要根据 STS2 内部对象补充更多内容。
- 当前 mod-side 原型仍使用 `FixtureGameStateProvider`，尚未接真实 STS2 运行时对象。
- Python 侧当前没有真实 `HttpGameBridge`，后续需要新增一个读取这些 endpoint 的 bridge adapter。

## 后续补充项

- 为战斗目标选择补充更细粒度的目标约束
- 为事件 / 商店 / 遗物 / 药水交互扩展更多窗口类型
- 为未来动作提交接口预留 `action_submission` / `action_result` 对应字段
- 在真实游戏模式下增加游戏版本和 mod loader 版本采集
