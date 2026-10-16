namespace Jalium.UI.MacOS;

// kAEQuitReason is present on the quit event sent by the session manager.
// An ordinary Quit or Quit All request does not end the user's session.
internal static class MacOSSessionEndReason
{
    internal static bool TryGet(uint eventClass, uint eventId, uint? quitReason,
        out ReasonSessionEnding reason)
    {
        reason = default;
        if (eventClass != 0x61657674 || eventId != 0x71756974) return false; // aevt / quit
        switch (quitReason)
        {
            case 0x726c676f: // kAEReallyLogOut: rlgo
                reason = ReasonSessionEnding.Logoff;
                return true;
            case 0x73687574: // kAEShutDown: shut
            case 0x72657374: // kAERestart: rest
                reason = ReasonSessionEnding.Shutdown;
                return true;
            default:
                return false;
        }
    }
}
