namespace ChatApp.Core.Settings;

/// <summary>Speaker-selection policy for group chat.</summary>
public enum GroupChatMode
{
    /// <summary>Every member speaks in DisplayOrder each turn (zero extra LLM cost).</summary>
    RoundRobin = 0,

    /// <summary>A director LLM picks 1-N speakers; they respond sequentially.</summary>
    Hybrid = 1,

    /// <summary>Each agent self-judges + director picks best (Phase 2).</summary>
    FreeForAll = 2
}

/// <summary>Group-chat tuning knobs (persisted as part of <see cref="AiSettings"/>).</summary>
public class GroupChatSettings
{
    /// <summary>Default speaking policy. Hybrid = director picks speakers.</summary>
    public GroupChatMode Mode { get; set; } = GroupChatMode.Hybrid;

    /// <summary>Requested AI speaker count per user turn (Hybrid only).</summary>
    public int MaxSpeakersPerTurn { get; set; } = 2;

    /// <summary>Whether agents are told they may respond to / rebut other agents.</summary>
    public bool RespondToOtherAgents { get; set; } = true;
}
