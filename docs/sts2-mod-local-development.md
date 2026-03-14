# STS2 mod 本地开发说明

## 目录结构

- `mod/Sts2Mod.StateBridge/`: bridge 核心库，包含协议模型、状态提取、运行时反射适配、in-game bootstrap。
- `mod/Sts2Mod.StateBridge.Host/`: 本地宿主进程，便于在 `fixture` 或 `runtime-host` 模式下联调。
- `mod/Sts2Mod.StateBridge.sln`: C# 解决方案入口。
- `tools/validate_mod_bridge.py`: 最小闭环校验脚本，会验证 `health`、`snapshot`、`actions` 和 `POST /apply`。
- `tools/validate_live_apply.py`: 真实游戏内的 live discovery / apply 验证脚本，会输出可复盘 artifacts。

## 运行模式

- `fixture`: 完全基于仓库内 fixture，适合协议调试和 CI。
- `runtime-host`: 宿主进程内加载 STS2 程序集，只做反射读取，不在游戏进程内执行。
- `in-game-runtime`: 真实 mod 已注入 STS2 进程，可读取 live state，并在关闭只读后执行动作。

`/health` 返回的 `provider_mode` 可直接区分三种模式。

## SDK 与构建要求

- 当前 STS2 游戏程序集依赖 `System.Runtime 9.0.0.0`，因此 bridge 现已统一切到 `.NET 9`。
- 仓库根目录的 `global.json` 固定到 .NET 9 SDK 线，避免误用 .NET 8 构建真实 mod。
- 本机 STS2 安装路径示例：`F:\SteamLibrary\steamapps\common\Slay the Spire 2`。
- 实际托管程序集目录：`F:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64`。

### 原型构建

```bash
dotnet build mod/Sts2Mod.StateBridge.sln
```

### 真实运行时构建

```bash
dotnet build mod/Sts2Mod.StateBridge.sln \
  -p:Sts2ManagedDir="F:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64" \
  -p:Sts2ModLoaderDir="F:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
```

构建成功后，`mod/Sts2Mod.StateBridge/bin/Debug/net9.0/mod/` 会输出：

- `Sts2Mod.StateBridge.pck`
- `Sts2Mod.StateBridge.dll`
- `mod_manifest.json`

## 真实 mod 安装

STS2 当前内置 mod loader 会递归扫描游戏目录下的 `mods/`，以 `.pck` 作为资源包入口，并在同目录发现同名 `.dll` 时加载程序集。基于这一约束，安装时至少需要这三个文件位于同一 mod 目录：

- `Sts2Mod.StateBridge.pck`
- `Sts2Mod.StateBridge.dll`
- `mod_manifest.json`（需要被打进 `.pck` 的 `res://mod_manifest.json`）

推荐目录结构：

```text
F:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\Sts2Mod.StateBridge\
  Sts2Mod.StateBridge.pck
  Sts2Mod.StateBridge.dll
```

当前仓库会自动调用 Godot headless 工具生成 `.pck`。若本机无法找到 Godot，可通过以下任一方式提供：

- 安装官方编辑器：`winget install --id GodotEngine.GodotEngine`
- 设置环境变量：`GODOT_EXE` 或 `GODOT_CONSOLE_EXE`
- 构建时显式传参：`-p:GodotExe="C:\path\to\Godot_v4.x_console.exe"`

## 启动与写入控制

### Host 模式

```bash
dotnet run --project mod/Sts2Mod.StateBridge.Host -- --port 17654 --game-version prototype --read-only false
```

### 真实 in-game 模式

- 默认只读，避免误操作人工对局。
- 需要写动作时，启动游戏前设置环境变量：

```powershell
$env:STS2_BRIDGE_ENABLE_WRITES = 'true'
```

可选环境变量：

- `STS2_BRIDGE_HOST`，默认 `127.0.0.1`
- `STS2_BRIDGE_PORT`，默认 `17654`
- `STS2_BRIDGE_ENABLE_WRITES`，默认关闭

## Loopback 接口

- `GET /health`
- `GET /snapshot`
- `GET /actions`
- `POST /apply`

`POST /apply` 请求体最少需要：

```json
{
  "decision_id": "dec-...",
  "action_id": "act-...",
  "params": {}
}
```

返回结果会区分：

- `accepted`: 已入队并由受控执行点完成动作。
- `rejected`: 被只读保护、stale decision、非法动作或不支持窗口拒绝。
- `failed`: 执行阶段异常、超时或 bridge 关闭。

## 最小验证流程

### 原型闭环

```bash
python tools/validate_mod_bridge.py
python tools/validate_mod_pck.py
```

脚本会自动验证：

- `health` 正常响应
- 四类窗口的 `snapshot` / `actions`
- `POST /apply` 成功推进到下一窗口
- 旧 `decision_id` 会被拒绝为 `stale_decision`
- `.pck`、DLL 与 manifest 已一起生成
- `.pck` 内可检测到 `res://mod_manifest.json`

### 一键安装与调试

```bash
python tools/debug_sts2_mod.py build
python tools/debug_sts2_mod.py install
python tools/debug_sts2_mod.py debug --enable-writes
```

说明：

- `build`：使用真实 STS2 引用重新构建 mod，并生成 `.pck`
- `install`：把 `.pck`、DLL、manifest 复制到游戏 `mods/Sts2Mod.StateBridge/`
- `debug`：执行构建 + 安装 + 启动游戏，并轮询 `http://127.0.0.1:17654/health`
- `debug` 默认只把游戏运行日志写到 `tmp/sts2-debug/` 下的 `sts2-runtime-*.log`，不再刷当前终端；需要镜像到当前终端时可追加 `--show-game-log`
- 可通过 `--game-dir` 覆盖默认游戏路径，也可设置环境变量 `STS2_GAME_DIR`

### 真实 live apply 验证

默认 discovery 只读探测：

```bash
python tools/validate_live_apply.py
```

如果要让脚本自动构建、安装、启动游戏，并在 live bridge 上执行一次真实 `POST /apply`：

```bash
python tools/validate_live_apply.py \
  --launch \
  --enable-writes \
  --apply \
  --allow-write \
  --game-dir "F:\SteamLibrary\steamapps\common\Slay the Spire 2"
```

关键约束：

- 不传 `--apply` 时，脚本只做 discovery，不会修改游戏状态。
- 传了 `--apply` 也必须再传 `--allow-write`，否则脚本会拒绝发送真实写请求。
- bridge `/health` 仍然必须返回 `read_only=false`，否则验证会被安全拒绝。
- 默认优先选择无需目标的 `play_card`；若当前没有安全出牌动作，会回退到更低风险动作，或直接返回 `no_candidate`。

每次执行都会落盘到 `tmp/live-apply-validation/<timestamp>/`，至少包含：

- `health.json`
- `before_snapshot.json`
- `before_actions.json`
- `candidate.json`
- `apply_request.json` / `apply_response.json`（仅 apply 模式）
- `after_snapshot.json` / `after_actions.json`（仅 apply 模式）
- `result.json`

`result.json` 的常见结论：

- `discovery_only`：完成只读探测，未发起写入
- `no_candidate`：当前窗口没有满足默认安全策略的动作
- `success`：请求被接受，且观测到 live 状态推进
- `inconclusive`：请求被接受，但超时窗口内未观测到明确推进
- `rejected` / `failed`：安全校验、协议或运行时失败

当 `POST /apply` 返回 `failed` 或 `rejected` 时，可优先查看 `apply_response.json` / `result.json` 中的以下诊断字段：

- `queue_stage`：当前动作卡在哪个阶段，例如 `enqueued`、`dequeued`、`executing`、`completed`、`failed`
- `last_tick_count` / `last_tick_at`：最近一次 in-game tick 是否还在推进
- `pending_queue_count`：失败时队列里还有多少请求未消费
- `current_window_ready`：失败时 coordinator 是否持有 live 决策窗口

如果 `queue_stage=enqueued` 且长时间没有进入 `dequeued`，优先排查 in-game tick / pump 是否正常驱动；如果已经进入 `executing`，则更可能是动作反射执行阶段的问题。

### 真实游戏手工联调

1. 以真实引用重新构建 bridge。
2. 准备 `.pck` 并将 mod 复制到 STS2 `mods/` 目录。
3. 需要写动作时，设置 `STS2_BRIDGE_ENABLE_WRITES=true` 后启动游戏。
4. 进入一局 run，再访问 `http://127.0.0.1:17654/health`，确认 `provider_mode` 为 `in-game-runtime`。
5. 访问 `/snapshot` 与 `/actions`，记录当前 `decision_id` 和目标 `action_id`。
6. 向 `/apply` 提交动作，确认返回 `accepted`。
7. 再次请求 `/snapshot`，确认 `decision_id` 或 `state_version` 已推进。

## 已接通的真实动作映射

- `combat`: `play_card`、`end_turn`
- `reward`: `choose_reward`、`skip_reward`
- `map`: `choose_map_node`

其他窗口仍保持只读观测；若窗口未支持，bridge 会明确返回拒绝或运行时诊断，而不是静默猜测执行。
