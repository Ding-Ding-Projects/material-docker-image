using System;

namespace MaterialDockerImage.UI;

/// <summary>The one thing the game ladder may change on a lock: it ends the running wait early.</summary>
public sealed partial class LockStore
{
    /// <summary>The Unix time the running wait ends, or 0 when no wait is running. Identifies one lockout for the ladder.</summary>
    public long LockoutStamp(string id) => WaitUntil(id) is { } until ? until.ToUnixTimeSeconds() : 0;

    /// <summary>
    /// Ends the running wait after a won game. The failure count is left exactly as it was, so the next wrong answer still earns the longer wait,
    /// and nothing is unlocked: the credential is still required. Returns false when there is no running wait to end.
    /// </summary>
    public bool ClearWaitByLadder(string id)
    {
        var record = Get(id);
        if (record == null || record.Policy == LockPolicy.None || WaitUntil(id) == null) return false;
        record.WaitUntilUnix = 0;
        Save();
        return true;
    }
}
