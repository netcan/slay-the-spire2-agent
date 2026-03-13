using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sts2Mod.StateBridge.Core;

public static class BridgeIds
{
    public static string CreateSessionId(string seed)
    {
        return $"sess-{Hash(seed + Guid.NewGuid()):x8}";
    }

    public static string CreateDecisionId(string sessionId, int stateVersion, string phase)
    {
        return $"dec-{Hash($"{sessionId}:{stateVersion}:{phase}"):x8}";
    }

    public static string CreateActionId(string decisionId, string actionType, IReadOnlyDictionary<string, object?> parameters)
    {
        var canonical = JsonSerializer.Serialize(parameters.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value));
        return $"act-{Hash($"{decisionId}:{actionType}:{canonical}"):x8}";
    }

    private static uint Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt32(bytes, 0);
    }
}
