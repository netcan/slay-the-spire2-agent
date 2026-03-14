## Context

2026-03-14 的真实联调已经证明 `card-description-rendering-glossary` 把 schema 和上层消费链路打通了，但 live runtime 中手牌卡牌仍主要暴露模板描述：例如 `造成{Damage:diff()}点伤害。`、`获得{Block:diff()}点格挡。`。当前 `description_rendered` 常常只是去掉富文本标签后的“模板兼容文本”，`description_vars` 里也缺少真实数值，因此 agent 依旧无法基于当前回合的真实牌效做稳定决策。

这个问题主要集中在 `Sts2RuntimeReflectionReader` 的 runtime 反射路径，而不是协议本身。当前设计已经支持 `description_raw`、`description_rendered`、`description_vars`、`glossary`，因此下一步重点是提升 live value resolution 的质量，并把质量状态明确暴露给 Python / runner / validation artifacts。

## Goals / Non-Goals

**Goals:**
- 在真实战斗 runtime 中，尽量为手牌导出当前实例级的真实数值，如 `damage`、`block`、`draw`、`strength` 修正结果。
- 重新定义 `description_rendered` 的质量门槛，避免仍含模板占位符的文本被误当成“已渲染”。
- 为 `description_vars` 增加更可靠的 live 来源探测路径，并输出来源/质量标记，便于排障。
- 在 Python 摘要与 live artifacts 中显式区分“已解析真实值”和“仍处于模板回退”。

**Non-Goals:**
- 本次不扩展完整百科、怪物机制数据库或长期规划知识层。
- 本次不重构动作协议，也不处理敌方 intent 识别不足等相邻问题。
- 本次不保证一次性覆盖所有卡牌脚本效果，先优先打通基础牌与高频动态字段。

## Decisions

### 1. 把“是否真正渲染完成”作为独立质量语义处理
- 决策：`description_rendered` 只有在文本不再包含模板占位符，或已能由变量表可靠替换后，才视为高质量 rendered；否则保持兼容输出，但必须附带质量标记。
- 原因：当前最大问题不是字段缺失，而是“假 rendered”误导上层。
- 备选：继续把去标签后的模板文本当作 rendered。未采用，因为这会让 LLM 错把 `{Damage:diff()}` 当真实事实。

### 2. 优先从 live card instance 上取值，再退回通用字段别名
- 决策：runtime reader 先沿卡牌实例、effect cache、战斗态 preview 字段寻找当前值，再退回现有的 `Damage`/`Block`/`MagicNumber` 这类通用别名。
- 原因：现有通用别名在 STS2 live runtime 中经常拿不到实际值，说明真实值可能挂在更深层的实例字段上。
- 备选：继续只靠浅层反射字段。未采用，因为 live artifacts 已证明这条路径不够。

### 3. 用“字段质量 + 来源”支撑 runner 降级
- 决策：为卡牌描述增加质量标记与来源语义，让 Python 摘要知道哪些卡牌可以信任、哪些只能回退到卡名/traits/glossary。
- 原因：即使部分卡牌短期仍拿不到真实值，runner 也不应该把低质量描述直接当高置信事实。
- 备选：仅在 mod 日志中记录问题。未采用，因为 runner 与 live validation 也需要直接消费这些信号。

### 4. live validation 以“基础牌真实值可复盘”为首要验收门槛
- 决策：联调优先覆盖 `Strike` / `Defend` / 常见 powers，要求 artifacts 能明确看出真实值是否已解析。
- 原因：这些对象最稳定、最容易复现，也最能代表 bridge 是否真正拿到了 live 数值。
- 备选：直接追求所有卡牌全覆盖。未采用，因为会扩大范围并拖慢收敛。

## Risks / Trade-offs

- [Live runtime 字段路径仍不稳定] → 增加多路径探测与 source diagnostics，保留模板回退但不得伪装为高质量 rendered。
- [字段过多导致 payload 继续膨胀] → 质量标记与来源字段尽量保持轻量，trace 保留完整信息，LLM 摘要只挑高价值字段。
- [不同卡牌脚本结构差异过大] → 先覆盖基础动态字段与高频牌，超出覆盖范围的对象保持安全降级。
- [为追求真实值而引入脆弱反射逻辑] → 优先选择只读探测路径，并在读取失败时 fail-safe，不影响快照整体导出。

## Migration Plan

1. 先补 runtime reader 的 live 取值路径与质量标记。
2. 再更新 bridge / Python 摘要的降级逻辑与 artifacts 输出。
3. 增加 fixture / 单测，覆盖“真实值已解析”和“模板回退”两种结果。
4. 在真实 STS2 runtime 中复跑基础牌 live validation，记录 artifacts 并确认 runner 输入已经区分质量。

## Open Questions

- STS2 live card instance 上是否存在统一的“当前描述已格式化文本”字段，还是必须自己做 placeholder -> value 替换？
- 某些动态值是否只在 hover/preview 或选中目标后才会生成，若是，bridge 是否需要补额外的 preview 读取路径？
- `description_quality` 应该作为显式字段导出，还是先放在 metadata / diagnostics 中，待稳定后再提升为正式 schema？
