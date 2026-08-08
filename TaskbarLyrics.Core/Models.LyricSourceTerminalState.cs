namespace TaskbarLyrics.Core.Models;

public enum LyricSourceTerminalState
{
    Succeeded,
    IdentityRejected,
    NoLyrics,
    InvalidContent,
    Failed,
    TimedOut,
    Disabled,
    Canceled
}
