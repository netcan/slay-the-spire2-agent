#if STS2_REAL_RUNTIME
using Godot;

namespace Sts2Mod.StateBridge.InGame;

internal sealed partial class BridgePumpNode : Node
{
    public const string NodeNameValue = "Sts2AgentBridgePump";

    public override void _EnterTree()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
        SetPhysicsProcess(true);
    }

    public override void _Ready()
    {
        Name = NodeNameValue;
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
        SetPhysicsProcess(true);
    }

    public override void _Process(double delta)
    {
        Sts2InGameModEntryPoint.OnGameTick("pump_process");
    }

    public override void _PhysicsProcess(double delta)
    {
        Sts2InGameModEntryPoint.OnGameTick("pump_physics_process");
    }
}
#endif
