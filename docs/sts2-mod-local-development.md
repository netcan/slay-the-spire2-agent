# STS2 mod 本地开发说明

## 目录结构

- `mod/Sts2Mod.StateBridge/`：游戏侧 bridge 类库
- `mod/Sts2Mod.StateBridge.Host/`：本地验证 host，用于不启动游戏时验证接口输出
- `mod/Sts2Mod.StateBridge.sln`：解决方案文件
- `tools/validate_mod_bridge.py`：自动检查 `health` / `snapshot` / `actions` 输出的脚本

## 工程模式

当前实现分为两层：

1. `Sts2Mod.StateBridge`
   - 定义游戏侧 observation / legal action / compatibility metadata 数据模型
   - 维护 `session_id`、`decision_id`、`state_version`、`action_id`
   - 提供 loopback 本地只读接口
   - 默认使用 `FixtureGameStateProvider` 输出四类核心窗口

2. `Sts2Mod.StateBridge.Host`
   - 用于脱离游戏单独运行本地 bridge
   - 方便手工验证和 agent 联调

## 构建

```bash
dotnet build mod/Sts2Mod.StateBridge.sln
```

## 运行本地 host

```bash
dotnet run --project mod/Sts2Mod.StateBridge.Host -- --port 17654 --game-version prototype
```

启动后可访问：

- `GET http://127.0.0.1:17654/health`
- `GET http://127.0.0.1:17654/snapshot`
- `GET http://127.0.0.1:17654/actions`

为方便原型验证，还支持 `phase` 查询参数：

- `GET /snapshot?phase=combat`
- `GET /snapshot?phase=reward`
- `GET /snapshot?phase=map`
- `GET /snapshot?phase=terminal`
- `GET /actions?phase=reward`

`phase` 参数仅用于本地原型调试；真实接入 STS2 时应由 mod 自动判断当前窗口。

## 配置真实 STS2 程序集

`mod/Sts2Mod.StateBridge/Sts2Mod.StateBridge.csproj` 已预留条件引用：

- `$(Sts2ManagedDir)\sts2.dll`
- `$(Sts2ManagedDir)\GodotSharp.dll`
- `$(Sts2ModLoaderDir)\0Harmony.dll`

示例：

```bash
dotnet build mod/Sts2Mod.StateBridge.sln -p:Sts2ManagedDir="E:\SteamLibrary\steamapps\common\Slay the Spire 2\Game" -p:Sts2ModLoaderDir="E:\mods\sts2"
```

如果没有配置这些路径，工程会以 `prototype mode` 编译，不会阻塞本地验证。

## 后续接真实游戏的落点

- 将 `FixtureGameStateProvider` 替换为真实的 `Sts2RuntimeStateProvider`
- 保留现有 HTTP 输出层与数据模型
- 在真实 provider 中读取游戏对象并映射为 `RuntimeWindowContext`
- 由四个窗口 extractor 统一生成对外 observation / actions
