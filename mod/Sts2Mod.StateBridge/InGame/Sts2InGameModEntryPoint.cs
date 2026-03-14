#if STS2_REAL_RUNTIME
using Godot;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Sts2Mod.StateBridge.Configuration;
using Sts2Mod.StateBridge.Logging;
using Sts2Mod.StateBridge.Providers;

namespace Sts2Mod.StateBridge.InGame;

[ModInitializer("Initialize")]
public sealed class Sts2InGameModEntryPoint
{
    private static readonly object Gate = new();
    private static readonly Harmony Harmony = new("sts2-agent.bridge");
    private static ConsoleBridgeLogger? _logger;
    private static ModBootstrap? _bootstrap;
    private static BridgePumpNode? _pumpNode;
    private static bool _initialized;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _logger = new ConsoleBridgeLogger();
            var requestedOptions = CreateOptions();
            var (provider, effectiveOptions) = ProviderFactory.Create(requestedOptions, _logger);
            if (provider is not Sts2RuntimeStateProvider runtimeProvider)
            {
                _logger.Warn("In-game bootstrap did not resolve the runtime provider; bridge will not start.");
                return;
            }

            runtimeProvider.EnableInGameRuntime(_logger);
            _bootstrap = new ModBootstrap(effectiveOptions, provider, _logger);
            _bootstrap.StartAsync().GetAwaiter().GetResult();
            Harmony.PatchAll(Assembly.GetExecutingAssembly());
            EnsurePumpNodeAttached();
            _initialized = true;
            _logger.Info("STS2 in-game bridge bootstrap initialized");
        }
    }

    internal static void OnGameTick(string source = "unknown")
    {
        if (!_initialized)
        {
            return;
        }

        EnsurePumpNodeAttached();
        InGameRuntimeCoordinator.Tick(source);
    }

    internal static void Shutdown()
    {
        lock (Gate)
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                InGameRuntimeCoordinator.Shutdown();
                if (_pumpNode is not null && GodotObject.IsInstanceValid(_pumpNode))
                {
                    _pumpNode.QueueFree();
                    _pumpNode = null;
                }
                _bootstrap?.StopAsync().GetAwaiter().GetResult();
            }
            finally
            {
                Harmony.UnpatchAll("sts2-agent.bridge");
                _bootstrap?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _bootstrap = null;
                _initialized = false;
            }
        }
    }

    private static BridgeOptions CreateOptions()
    {
        var managedDir = Path.GetDirectoryName(typeof(NGame).Assembly.Location);
        var writesEnabled = string.Equals(
            System.Environment.GetEnvironmentVariable("STS2_BRIDGE_ENABLE_WRITES"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        return new BridgeOptions
        {
            Host = System.Environment.GetEnvironmentVariable("STS2_BRIDGE_HOST") ?? "127.0.0.1",
            Port = int.TryParse(System.Environment.GetEnvironmentVariable("STS2_BRIDGE_PORT"), out var port) ? port : 17654,
            ProtocolVersion = "0.1.0",
            ModVersion = typeof(Sts2InGameModEntryPoint).Assembly.GetName().Version?.ToString() ?? "0.1.0",
            ProviderMode = "in-game-runtime",
            Sts2ManagedDir = managedDir,
            Sts2ModLoaderDir = managedDir,
            PreferRuntimeProvider = true,
            AllowDebugPhaseOverride = false,
            ReadOnly = !writesEnabled,
        };
    }

    private static void EnsurePumpNodeAttached()
    {
        if (_pumpNode is not null && GodotObject.IsInstanceValid(_pumpNode))
        {
            return;
        }

        var gameInstance = NGame.Instance;
        if (gameInstance is null)
        {
            return;
        }

        var existing = gameInstance.GetNodeOrNull<BridgePumpNode>(BridgePumpNode.NodeNameValue);
        if (existing is not null)
        {
            _pumpNode = existing;
            return;
        }

        var pumpNode = new BridgePumpNode();
        gameInstance.AddChild(pumpNode);
        _pumpNode = pumpNode;
        _logger?.Info("Attached bridge pump node to NGame.");
    }
}

[HarmonyPatch(typeof(NGame), "_Ready")]
internal static class NGameReadyPatch
{
    private static void Postfix()
    {
        Sts2InGameModEntryPoint.OnGameTick("harmony_n_game_ready");
    }
}

[HarmonyPatch(typeof(NGame), "_Notification")]
internal static class NGameNotificationPatch
{
    private static void Postfix(int what)
    {
        if (what == Node.NotificationProcess)
        {
            Sts2InGameModEntryPoint.OnGameTick("harmony_n_game_notification_process");
        }
        else if (what == Node.NotificationPhysicsProcess)
        {
            Sts2InGameModEntryPoint.OnGameTick("harmony_n_game_notification_physics_process");
        }
    }
}

[HarmonyPatch(typeof(NControllerManager), "_Process")]
internal static class NControllerManagerProcessPatch
{
    private static void Postfix()
    {
        Sts2InGameModEntryPoint.OnGameTick("harmony_n_controller_manager_process");
    }
}

[HarmonyPatch(typeof(NGame), "_ExitTree")]
internal static class NGameExitTreePatch
{
    private static void Prefix()
    {
        Sts2InGameModEntryPoint.Shutdown();
    }
}
#endif
