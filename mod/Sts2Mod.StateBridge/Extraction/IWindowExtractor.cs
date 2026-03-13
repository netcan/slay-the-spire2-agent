using Sts2Mod.StateBridge.Contracts;
using Sts2Mod.StateBridge.Core;

namespace Sts2Mod.StateBridge.Extraction;

public interface IWindowExtractor
{
    string Phase { get; }

    ExportedWindow Export(RuntimeWindowContext context, BridgeSessionState sessionState);
}
