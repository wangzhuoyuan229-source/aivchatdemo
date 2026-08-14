using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>
/// Streaming events emitted by <see cref="IGroupChatService"/> during a group-chat
/// turn. The UI maps these to per-speaker chat bubbles.
/// </summary>
public abstract record GroupChatEvent;

/// <summary>A new AI speaker has started talking.</summary>
public record SpeakerStarted(int RoleId) : GroupChatEvent;

/// <summary>A streaming text delta from the given speaker.</summary>
public record SpeakerDelta(int RoleId, string Delta) : GroupChatEvent;

/// <summary>The speaker finished; the final persisted message is available.</summary>
public record SpeakerFinished(int RoleId, Message FinalMessage) : GroupChatEvent;

/// <summary>The whole group-chat turn is done (all speakers finished).</summary>
public record TurnFinished() : GroupChatEvent;
