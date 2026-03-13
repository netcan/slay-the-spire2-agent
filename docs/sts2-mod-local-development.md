# STS2 mod 本地开发说明

## 目录结构

- `mod/Sts2Mod.StateBridge/`: bridge 核心库，包含协议模型、状态提取、运行时反射适配、in-game bootstrap。
- `mod/Sts2Mod.StateBridge.Host/`: 本地宿主进程，便于在 `fixture` 或 `runtime-host` 模式下联调。
- `mod/Sts2Mod.StateBridge.sln`: C# 解决方案入口。
- `tools/validate_mod_bridge.py`: 最小闭环校验脚本，会验证 `health`、`snapshot`、`actions` 和 `POST /apply`。

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
