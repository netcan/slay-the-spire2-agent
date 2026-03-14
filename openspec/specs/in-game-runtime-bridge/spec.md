# in-game-runtime-bridge Specification

## Purpose
定义 STS2 bridge 作为真实游戏内 mod 运行时的状态导出约束，确保 live `health`、`snapshot`、`actions` 在受控线程上下文中稳定可用。
## Requirements
### Requirement: 游戏内 mod 必须暴露 live runtime bridge
系统 MUST 能以真实 STS2 mod 的形式运行在游戏进程内，并通过 loopback bridge 对外暴露 live `health`、`snapshot`、`actions` 能力。该 bridge SHALL 复用统一的决策窗口模型，并在游戏内无活动 run、运行时未就绪或版本不兼容时返回可诊断状态，而不是崩溃或阻塞游戏。对于战斗结束后的奖励窗口，bridge MUST 稳定识别并导出 `phase = reward`，不得在 reward 已显示时错误回落到 `combat` 或继续伪造玩家战斗动作。

#### Scenario: 游戏内 bridge 成功附着到活动 run
- **WHEN** STS2 已启动、mod 已加载且玩家进入一局活动 run
- **THEN** `health` MUST 返回可识别的 in-game runtime 模式
- **THEN** `snapshot` MUST 返回当前决策窗口的 live 状态
- **THEN** `actions` MUST 返回与该窗口对应的 legal actions

#### Scenario: 游戏已启动但当前没有活动 run
- **WHEN** STS2 已启动且 mod 已加载，但玩家仍在主菜单或尚未进入 run
- **THEN** `health` MUST 返回 bridge 已加载但 run 未就绪的状态说明
- **THEN** `snapshot` MUST NOT 伪造战斗或地图数据
- **THEN** bridge MUST 保持可继续服务，直到 run 就绪

#### Scenario: reward 界面显示时导出 reward phase
- **WHEN** 玩家已经结束战斗并进入奖励界面，且 runtime 可观察到 reward screen、reward buttons 或等效 reward 信号
- **THEN** `snapshot.phase` MUST 返回 `reward`
- **THEN** `snapshot.rewards` MUST 返回当前可见奖励文本
- **THEN** `actions` MUST 返回 `choose_reward`、`skip_reward` 或等效 reward 合法动作，而不是 `end_turn`

#### Scenario: 战斗结束过渡态不得伪装为玩家战斗回合
- **WHEN** 当前战斗敌人已经全部清空，但 reward UI 仍在挂载或切换中
- **THEN** bridge MUST 优先进入 reward 识别或保守降级路径
- **THEN** bridge MUST NOT 持续导出 `window_kind = player_turn` 与可重复提交的 `end_turn` 作为主要对外语义

#### Scenario: runtime 读取失败时保持 fail-safe
- **WHEN** 反射读取、游戏节点发现或窗口识别过程中发生异常
- **THEN** bridge MUST 返回结构化错误或降级状态
- **THEN** mod MUST NOT 使游戏进程崩溃
- **THEN** 后续请求 MUST 仍可继续探测健康状态

### Requirement: bridge 必须在受控线程上下文导出 live state
系统 MUST 在游戏主线程或等效的受控调度点读取、刷新和导出游戏状态，不得在任意 HTTP 请求线程直接对 STS2 或 Godot 对象做不安全访问。若当前请求无法立即读取 live state，bridge MUST 返回明确失败语义或最近一次可接受的快照策略说明。

#### Scenario: HTTP 请求与游戏主线程并发发生
- **WHEN** 外部进程在游戏运行过程中并发请求 `snapshot` 或 `actions`
- **THEN** bridge MUST 通过受控调度或快照缓存来读取状态
- **THEN** bridge MUST NOT 直接在 HTTP 线程上执行不安全的游戏对象访问

#### Scenario: 状态版本在 live 更新后推进
- **WHEN** 当前决策窗口内容发生变化，例如手牌、敌人、奖励或可选地图点变化
- **THEN** 导出的 `state_version` MUST 对应推进
- **THEN** 新的 `decision_id` MUST 反映最新的 live 决策上下文

### Requirement: 手牌卡牌必须导出稳定且可区分的运行时 card_id
系统 MUST 为 `snapshot.player.hand` 中的每一张手牌导出稳定的 `card_id`，并确保同名、同费用、同升级状态的重复手牌在同一决策窗口内仍然可以被区分。该 `card_id` MUST 与当前 live 牌实例保持一致，并 SHALL 被对应的 `play_card` legal action 通过 `params.card_id` 直接引用。

#### Scenario: 重复手牌仍然拥有不同 card_id
- **WHEN** 玩家当前手牌中同时存在两张或更多张表面属性相同的卡牌
- **THEN** `/snapshot.player.hand` 中每张手牌的 `card_id` MUST 彼此不同
- **THEN** 卡牌的 `name` MAY 相同，但 bridge MUST 仍能稳定区分这些实例

#### Scenario: legal actions 与 hand 中的 card_id 一一对应
- **WHEN** bridge 生成当前窗口的 `play_card` legal actions
- **THEN** 每个 `play_card` action 的 `params.card_id` MUST 对应到 `snapshot.player.hand` 中的一张具体手牌
- **THEN** `action_id` MUST 反映该 `card_id` 所代表的实例级差异

### Requirement: live snapshot 与 actions 必须导出可读的用户向文本
系统 MUST 对 in-game runtime bridge 中的主要文本字段执行统一解析，至少覆盖 relics、potions、rewards、map nodes、cards、enemies 与 action labels。当字段背后存在 `LocString` 或类似本地化容器时，bridge MUST 优先输出面向玩家的本地化文本，而不得将类名、原始对象名或无意义 `ToString()` 结果直接作为对外值。

#### Scenario: relics 与 potions 使用本地化文本导出
- **WHEN** 玩家状态中包含 relics 或 potions，且对应对象可以解析到本地化显示文本
- **THEN** `/snapshot.player.relics` 与 `/snapshot.player.potions` MUST 返回可读的本地化名称
- **THEN** 返回值 MUST NOT 是 `MegaCrit.Sts2.Core.Localization.LocString` 或类似类名字符串

#### Scenario: reward 与 action label 优先使用用户向文本
- **WHEN** reward 按钮或 legal action 背后同时存在本地化文本与开发者内部标识
- **THEN** `/snapshot.rewards` 与 `/actions[].label` MUST 优先输出用户实际看到的可读文本
- **THEN** 若需要稳定行为参数，bridge MUST 通过 `params` 或 metadata 保留结构化标识，而不是仅依赖 label

### Requirement: 文本解析失败时必须提供可诊断的降级信息
系统 MUST 在文本解析失败或只能使用 fallback 时保持 fail-safe，并在 metadata 或等效 diagnostics 结构中暴露足够的调试信息。bridge MUST NOT 因为个别文本字段解析失败而使整个 `snapshot` 或 `actions` 构建失败。

#### Scenario: 无法解析本地化文本时仍返回稳定快照
- **WHEN** 某个 runtime 对象的文本字段无法通过本地化或预定字段解析
- **THEN** bridge MUST 仍然返回可序列化的 `snapshot` 或 `actions` 响应
- **THEN** bridge MUST 在 metadata 中指出对应字段使用了 fallback 或存在 unresolved 情况

#### Scenario: diagnostics 不得污染面向 Agent 的主字段
- **WHEN** bridge 需要暴露文本解析来源或失败原因
- **THEN** diagnostics MUST 优先放在 metadata 或等效辅助字段中
- **THEN** `name`、`label`、`relics`、`rewards` 等面向 Agent 的主字段 MUST 保持简洁、稳定和可读

### Requirement: 卡牌奖励选择界面必须作为 reward phase 导出并可连续决策
当奖励链路进入“选牌二级界面”（卡牌奖励选择）时，bridge MUST 将当前窗口导出为 `snapshot.phase="reward"`，并导出可选卡牌的用户向文本到 `snapshot.rewards`，同时生成对应的 `choose_reward` legal actions，使外部 agent 可以在同一 reward 链路中继续选择具体卡牌或完成该奖励步骤。

#### Scenario: 进入卡牌奖励选择界面时导出 reward phase
- **WHEN** 玩家在 `NRewardsScreen` 选择了“将一张牌添加到你的牌组。”并进入卡牌奖励选择界面
- **THEN** `snapshot.phase` MUST 等于 `reward`
- **THEN** `metadata.window_kind` MUST 标记为可区分的 reward 子窗口（例如 `reward_card_selection`）
- **THEN** `metadata.reward_subphase` MUST 标记为 `card_reward_selection`
- **THEN** `snapshot.rewards` MUST 为非空数组，按展示顺序包含每张可选卡牌的可读名称
- **THEN** `actions` MUST 为每个可选卡牌生成一个 `type="choose_reward"` 的 legal action，且其 `params.reward_index` MUST 与 `snapshot.rewards` 的索引一致

#### Scenario: reward buttons 不可用时不得回落到 combat_transition
- **WHEN** 当前没有存活敌人且 `NRewardsScreen` 不可见或 reward buttons 为 0，但卡牌奖励选择界面处于可交互状态
- **THEN** bridge MUST NOT 导出 `metadata.window_kind="combat_transition"` 作为当前决策窗口
- **THEN** bridge MUST 按卡牌奖励选择界面导出 reward 决策窗口与 legal actions

#### Scenario: 文本解析失败时仍提供可诊断且可执行的 choices
- **WHEN** 某些可选卡牌的本地化文本无法解析，只能使用 fallback 文本
- **THEN** `snapshot.rewards` MUST 仍包含与可选项数量一致的条目（例如 `card_<index>`）
- **THEN** bridge MUST 在 `metadata.text_diagnostics` 或等效 diagnostics 中指出对应条目使用了 fallback 与解析来源
- **THEN** `choose_reward` legal actions MUST 仍可按 `reward_index` 执行，不得因单个文本失败而缺失动作

#### Scenario: 不可跳过时不生成 skip_reward
- **WHEN** 当前卡牌奖励选择界面不存在跳过/关闭控件或该奖励规则不允许跳过
- **THEN** bridge MUST NOT 生成 `type="skip_reward"` 的 legal action
- **THEN** bridge MUST 在 `metadata` 中标记跳过不可用的原因（例如 `reward_skip_available=false` 与 `reward_skip_reason`）

### Requirement: reward 收尾前进窗口必须导出可执行动作
当奖励链路已经没有可领取奖励，但界面仍停留在需要玩家点击“前进/继续”后才能进入地图的窗口时，bridge MUST 将该窗口导出为 reward 链路中的可区分子窗口，并提供至少一个可执行 legal action，使外部 agent 能显式推进到地图，而不是停留在空 reward 窗口。

#### Scenario: 普通奖励收尾后出现前进按钮
- **WHEN** 玩家已经领完当前奖励，界面显示“前进/继续”按钮，且点击后才会进入地图
- **THEN** `snapshot.phase` MUST 仍可保持为 `reward` 或等效 reward 链路 phase
- **THEN** `metadata.window_kind` MUST 标记为可区分的 reward 收尾子窗口，而不是继续复用空 `reward_choice`
- **THEN** `actions` MUST 至少包含一个可执行的前进/继续 legal action

#### Scenario: 空 reward 窗口不得再被导出为可交互状态
- **WHEN** 当前 `reward_count=0`，且界面实际上处于“前进/继续”窗口
- **THEN** bridge MUST NOT 导出 `rewards=[]` 且 `actions=[]` 的稳定空 reward 窗口作为最终结果
- **THEN** bridge MUST 要么导出 continue/advance 动作，要么显式标记为短暂过渡态并附带 diagnostics

### Requirement: reward 收尾到 map 的推进信号必须可诊断
bridge MUST 在 reward 收尾、提交前进动作、进入地图三个阶段导出稳定的 metadata / diagnostics，便于 runner 与 live 验证脚本判断当前卡在“未点前进”“前进已提交等待切图”还是“已经进入 map”。

#### Scenario: 前进动作提交后进入房间过渡
- **WHEN** 外部通过 `/apply` 成功提交 reward 收尾前进动作
- **THEN** action response metadata MUST 标记这是 reward continue/advance 语义
- **THEN** 后续 `snapshot` 或 metadata MUST 能区分房间过渡中与地图已就绪两种状态

#### Scenario: 地图出现后不再保留 reward 收尾语义
- **WHEN** 地图已经出现并可选择下一个节点
- **THEN** `snapshot.phase` MUST 推进为 `map`
- **THEN** `metadata.window_kind` MUST 不再保留 reward 收尾子窗口标记
- **THEN** `actions` MUST 导出 `choose_map_node` legal actions

### Requirement: bridge 必须稳定导出 reward 到 map 的 run-flow 推进
系统 MUST 在 reward 链路结束后、地图出现前、地图选路后进入下一房间前，以及重新进入 `combat` 前，持续导出与真实窗口一致的 `snapshot` 与 metadata。bridge MUST 为 runner 提供稳定的 phase / window diagnostics，使其能够区分“仍在 reward”“地图已就绪”“房间过渡中”和“下一场战斗已进入”。

#### Scenario: reward 完成后进入 map 窗口
- **WHEN** 玩家完成当前奖励链路，界面从 `reward` 推进到 `map`
- **THEN** bridge MUST 导出 `snapshot.phase = map` 或等效 map phase
- **THEN** metadata MUST 明确标记地图窗口已就绪，而不是继续保留 reward 语义
- **THEN** bridge MUST NOT 继续导出过期的 reward diagnostics 作为当前主窗口

#### Scenario: map 选路后进入房间过渡
- **WHEN** 外部已经提交 `choose_map_node`，但下一房间或下一场战斗尚未完全载入
- **THEN** bridge MUST 导出可轮询的过渡态 `snapshot`
- **THEN** metadata MUST 标记这是地图选路后的过渡窗口，而不是把它误判回 reward 或稳定 map ready

#### Scenario: 下一房间进入 combat 决策窗口
- **WHEN** 地图选路后的房间加载完成并重新出现战斗决策
- **THEN** bridge MUST 导出 `snapshot.phase = combat`
- **THEN** 对应的 `decision_id` 与 `state_version` MUST 推进到新的 live 决策上下文

### Requirement: map 阶段必须导出可用 legal actions 与 diagnostics
系统 MUST 在地图窗口可交互时导出稳定的 `snapshot.map_nodes` 与 `choose_map_node` legal actions；在地图节点暂不可达或文本只能 fallback 时，bridge MUST 保持响应可诊断且动作仍可执行，便于 runner 安全推进。

#### Scenario: reward 后地图节点可正常导出
- **WHEN** 奖励链路结束且地图已经出现可供选择的下一个节点
- **THEN** `snapshot.phase` MUST 为 `map`
- **THEN** `snapshot.map_nodes` MUST 包含当前可达节点的稳定列表
- **THEN** `actions` MUST 导出与这些节点对应的 `choose_map_node` legal actions

#### Scenario: 地图短暂不可选时导出过渡诊断
- **WHEN** 地图界面已经切出，但当前帧还没有稳定的可达节点或可执行动作
- **THEN** bridge MUST 仍返回可序列化的 `snapshot` 与 `actions`
- **THEN** metadata MUST 说明这是暂时过渡、无可达节点或等效可诊断状态

#### Scenario: map 文本 fallback 时仍保持可执行
- **WHEN** 地图节点文本只能使用 fallback 名称或内部标签
- **THEN** `snapshot.map_nodes` 与 `actions[].label` MUST 仍保持一一对应
- **THEN** 每个 `choose_map_node` action MUST 仍能通过稳定参数执行，不得仅因 label fallback 而失效

### Requirement: 无活动 run 时必须导出 menu phase 与可执行开局动作
当 STS2 已启动且 mod 已加载，但当前没有活动 run（例如处于主菜单、开局配置、角色选择等流程）时，bridge MUST 仍然能够导出结构化快照与 legal actions，以支持自动化进入 run。此时 `snapshot` MUST 使用 `phase="menu"`（或等效稳定值）表示当前处于菜单/开局流程；并且 MUST NOT 伪造 `combat`、`map`、`reward` 的 run 内数据。

#### Scenario: 主菜单存在 Continue 时导出 continue_run
- **WHEN** 当前处于主菜单且 Continue/继续 按钮可用
- **THEN** `snapshot.phase` MUST 等于 `menu`
- **THEN** `actions` MUST 包含 `type="continue_run"` 的 legal action
- **THEN** 该 action 的 `label` MUST 为玩家可读的按钮文本或等效可读文本

#### Scenario: 主菜单无存档时导出 start_new_run
- **WHEN** 当前处于主菜单且 Continue/继续 不可用，但 New Run/开始 等入口可用
- **THEN** `snapshot.phase` MUST 等于 `menu`
- **THEN** `actions` MUST 包含 `type="start_new_run"` 的 legal action
- **THEN** bridge MUST 在 `metadata` 中提供 `menu_detection_source` 或等效 diagnostics，便于定位识别路径

#### Scenario: 进入新 run 配置后导出角色选择与确认动作
- **WHEN** 玩家已进入新 run 配置流程，且界面存在角色列表与“开始/确认”按钮
- **THEN** `actions` MUST 包含一个或多个 `type="select_character"` 的 legal actions（每个角色一个）
- **THEN** `actions` MUST 在可用时包含 `type="confirm_start_run"` 的 legal action
- **THEN** `select_character` MUST 通过 `params.character_id` 或等效稳定参数区分不同角色

#### Scenario: 不确定菜单状态时必须 fail-safe
- **WHEN** bridge 无法稳定识别当前菜单流程（例如被遮挡弹窗、版本不兼容、字段探测失败）
- **THEN** bridge MUST 返回可序列化的 `snapshot`，其 `phase` MUST 为 `menu` 或 `unknown` 的稳定值
- **THEN** bridge MUST NOT 导出可能误触危险路径的动作（例如 Exit/Abandon/删除存档 等）
- **THEN** bridge MUST 在 `metadata` 中返回可诊断信息（例如探测失败原因或候选控件摘要）

### Requirement: live runtime bridge 必须尽可能稳定读取 combat piles
当 `snapshot.phase="combat"` 时，bridge MUST 尽可能从 live runtime 读取当前玩家的抽牌堆、弃牌堆、消耗堆内容，并将其导出到统一快照。若某个 pile 暂时不可读、节点缺失或只部分可解析，bridge MUST 对该 pile 独立降级，而不是让整个 combat snapshot 失败。

#### Scenario: live runtime 成功读取三类 pile
- **WHEN** 当前玩家的 `DrawPile`、`DiscardPile`、`ExhaustPile` 在 runtime 中都可访问
- **THEN** `snapshot.player` MUST 导出三类 pile 的结构化卡牌列表
- **THEN** 这些 pile contents MUST 与同一帧里的 pile 计数字段保持一致或等效一致

#### Scenario: 单个 pile 读取失败时保持 fail-safe
- **WHEN** 某一个 pile 的 runtime collection 暂时不可读或其中个别卡牌无法完整解析
- **THEN** bridge MUST 仍然返回可序列化的 combat snapshot
- **THEN** 失败的 pile MUST 独立降级为可接受的空列表或最小 fallback 列表
- **THEN** metadata MUST 提供该 pile 的 source、fallback 或等效 diagnostics

### Requirement: live runtime bridge 必须稳定提取 enemy enrich fields 并按字段降级
当 `snapshot.phase="combat"` 时，bridge MUST 尽可能从 live runtime 读取敌人的当前招式文本、trait/tag、关键词或等效 richer enemy fields。若某个敌人的某个扩展字段暂时不可读，bridge MUST 对该 enemy 独立降级，而不是让整个 enemy 列表或 combat snapshot 失败。

#### Scenario: live runtime 成功读取敌人的当前招式说明
- **WHEN** 某个敌人的当前行动对象、显示文本或等效节点在 runtime 中可访问
- **THEN** `snapshot.enemies[]` MUST 导出该敌人的 `move_name`、`move_description` 或等效 richer fields
- **THEN** 若能解析 glossary，bridge MUST 一并导出对应的结构化 glossary anchors

#### Scenario: 单个敌人的 enrich 字段读取失败时保持 fail-safe
- **WHEN** 某个敌人的 move 文本、trait 容器或关键词来源暂时不可读
- **THEN** bridge MUST 仍然返回可序列化的 `snapshot.enemies[]`
- **THEN** 该敌人的基础字段（如 `name`、`hp`、`intent`、`powers`）MUST 继续可用
- **THEN** metadata MUST 提供该 enemy 或字段的 source、fallback 或等效 diagnostics

