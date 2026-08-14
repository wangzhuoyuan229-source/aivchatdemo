using System.Globalization;
using System.Text;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ChatApp.AI.SemanticKernel;

/// <summary>
/// Director for group chats. Persists the user message, picks speakers (round-robin
/// or hybrid director-LLM selection), assembles each speaker's context (its own
/// persona + private long-term memory + global knowledge + a group transcript),
/// streams its reply, persists it, and appends it to the transcript so later
/// speakers can react to it.
/// </summary>
public class GroupChatOrchestrator : IGroupChatService
{
    private readonly IConfigurationService _config;
    private readonly IChatHistoryService _history;
    private readonly IRoleService _roles;
    private readonly IMemoryService _memory;
    private readonly IKnowledgeService _knowledge;
    private readonly ILogger<GroupChatOrchestrator> _logger;

    public GroupChatOrchestrator(
        IConfigurationService config,
        IChatHistoryService history,
        IRoleService roles,
        IMemoryService memory,
        IKnowledgeService knowledge,
        ILogger<GroupChatOrchestrator> logger)
    {
        _config = config;
        _history = history;
        _roles = roles;
        _memory = memory;
        _knowledge = knowledge;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Message>> SendAsync(
        int conversationId,
        string userText,
        IProgress<GroupChatEvent>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userText))
            throw new ArgumentException("消息不能为空。", nameof(userText));

        var settings = await _config.LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.ChatModel))
            throw new InvalidOperationException("尚未配置 API Key 或聊天模型，请先打开设置。");

        var conv = await _history.GetConversationAsync(conversationId, ct)
            ?? throw new InvalidOperationException("会话不存在。");
        if (conv.Type != ConversationType.Group)
            throw new InvalidOperationException("该会话不是群聊。");

        var members = await _history.GetMembersAsync(conversationId, ct);
        if (members.Count == 0)
            throw new InvalidOperationException("群聊没有成员。");

        // Resolve member roles.
        var rolesById = new Dictionary<int, Role>();
        foreach (var m in members)
        {
            if (!rolesById.ContainsKey(m.RoleId))
            {
                var r = await _roles.GetAsync(m.RoleId, ct);
                if (r is not null) rolesById[m.RoleId] = r;
            }
        }
        if (rolesById.Count == 0)
            throw new InvalidOperationException("群聊成员角色均不存在。");

        // 1. Persist user message (RoleId = 0 = "no specific role" for user).
        await _history.AddMessageAsync(new Message
        {
            ConversationId = conversationId,
            RoleId = 0,
            Author = MessageAuthor.User,
            Content = userText,
            TokenEstimate = EstimateTokens(userText, settings)
        }, ct);

        // 2. Build the group transcript (recent window, including the user message just added).
        var window = await _history.GetMessagesAsync(conversationId, settings.ContextWindowSize, ct);
        var transcript = FormatTranscript(window, rolesById);

        // 3. Pick speakers.
        var speakerIds = settings.GroupChat.Mode == GroupChatMode.Hybrid
            ? await PickSpeakersHybridAsync(settings, rolesById, members, userText, transcript, ct)
            : members.Select(m => m.RoleId).ToList();

        // Cap to MaxSpeakersPerTurn (Hybrid enforces it; RoundRobin ignores it by design).
        if (settings.GroupChat.Mode == GroupChatMode.Hybrid && speakerIds.Count > settings.GroupChat.MaxSpeakersPerTurn)
            speakerIds = speakerIds.Take(settings.GroupChat.MaxSpeakersPerTurn).ToList();

        // 4. Each speaker takes a turn, seeing the latest transcript (which grows as they speak).
        var results = new List<Message>();
        var kernel = KernelFactory.Build(settings);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var execSettings = new OpenAIPromptExecutionSettings { Temperature = 0.8, TopP = 1.0 };

        foreach (var roleId in speakerIds)
        {
            if (!rolesById.TryGetValue(roleId, out var role)) continue;
            ct.ThrowIfCancellationRequested();

            // Best-effort per-role memory + global knowledge.
            IReadOnlyList<VectorSearchHit> memoryHits = Array.Empty<VectorSearchHit>();
            if (settings.EnableLongTermMemory)
            {
                try { memoryHits = await _memory.RecallAsync(role.Id, userText, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Memory recall failed for role {Id}.", role.Id); }
            }
            IReadOnlyList<VectorSearchHit> kbHits = Array.Empty<VectorSearchHit>();
            if (settings.EnableKnowledgeBase)
            {
                try { kbHits = await _knowledge.RetrieveAsync(userText, settings.KnowledgeTopK, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Knowledge retrieval failed."); }
            }

            var systemPrompt = ChatOrchestrator.BuildSystemPrompt(role, memoryHits, kbHits)
                + BuildGroupRules(role, rolesById, settings.GroupChat);
            var skHistory = new ChatHistory(systemPrompt);
            skHistory.AddUserMessage(transcript + $"\n\n请以 {role.Name} 的身份发言。");

            progress?.Report(new SpeakerStarted(role.Id));

            var sb = new StringBuilder();
            await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(skHistory, execSettings, kernel, ct))
            {
                var delta = chunk.Content;
                if (string.IsNullOrEmpty(delta)) continue;
                sb.Append(delta);
                progress?.Report(new SpeakerDelta(role.Id, delta));
            }

            var reply = sb.ToString();
            if (string.IsNullOrWhiteSpace(reply))
                reply = $"（{role.Name} 沉默了一会儿，没有说话。）";

            var msg = await _history.AddMessageAsync(new Message
            {
                ConversationId = conversationId,
                RoleId = role.Id,
                Author = MessageAuthor.Assistant,
                Content = reply,
                TokenEstimate = EstimateTokens(reply, settings)
            }, ct);

            progress?.Report(new SpeakerFinished(role.Id, msg));
            results.Add(msg);

            // Append so the next speaker can react.
            transcript += $"\n[{role.Name}] {reply}";
        }

        progress?.Report(new TurnFinished());
        return results;
    }

    /// <summary>
    /// Hybrid speaker selection: one non-streaming director LLM call picks 1..N speakers
    /// by name. Falls back to the first N members in display order on any failure.
    /// </summary>
    private async Task<List<int>> PickSpeakersHybridAsync(
        AiSettings settings,
        IReadOnlyDictionary<int, Role> rolesById,
        IReadOnlyList<ConversationMember> members,
        string userText,
        string transcript,
        CancellationToken ct)
    {
        var max = Math.Max(1, settings.GroupChat.MaxSpeakersPerTurn);
        var roleList = members
            .Select(m => rolesById.TryGetValue(m.RoleId, out var r) ? r : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        try
        {
            var directorSystem = BuildDirectorSystem(roleList);
            var directorHistory = new ChatHistory(directorSystem);
            directorHistory.AddUserMessage(
                $"用户说：「{userText}」\n\n" +
                $"最近群聊记录：\n{transcript}\n\n" +
                $"请选出最适合回复的 1-{max} 个角色，按发言顺序输出角色名，用逗号分隔。只输出名字，不要解释。");

            var kernel = KernelFactory.Build(settings);
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var result = await chat.GetChatMessageContentAsync(
                directorHistory,
                new OpenAIPromptExecutionSettings { Temperature = 0.2 },
                kernel, ct);

            var picked = ParseDirectorNames(result.Content ?? string.Empty, rolesById);
            if (picked.Count > 0)
                return picked;
            _logger.LogWarning("Director returned no matching names; falling back to first {N} members.", max);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Director selection failed; falling back to first {N} members.", max);
        }

        return roleList.Take(max).Select(r => r.Id).ToList();
    }

    private static string BuildDirectorSystem(IReadOnlyList<Role> roles)
    {
        var sb = new StringBuilder();
        sb.Append("你是群聊调度员。根据用户消息和群聊记录，选出最适合回复的角色。被直接 @ 的角色必选。\n\n");
        sb.Append("群内成员：\n");
        for (int i = 0; i < roles.Count; i++)
        {
            var r = roles[i];
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1}. {r.Name}");
            if (!string.IsNullOrWhiteSpace(r.Description))
                sb.Append("（").Append(r.Description).Append('）');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Parses the director's comma-separated name list into role ids.</summary>
    private static List<int> ParseDirectorNames(string raw, IReadOnlyDictionary<int, Role> rolesById)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var parts = raw.Split(new[] { '，', ',', '、', ';', '；', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            // Strip leading numbering like "1." "2、" "(3)"
            var name = part.Trim();
            name = System.Text.RegularExpressions.Regex.Replace(name, @"^[\(\[]?\d+[\.\)、\]]?\s*", "");
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Exact match first, then contains (for "林溪" inside "心理咨询师林溪").
            var exact = rolesById.Values.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                if (!result.Contains(exact.Id)) result.Add(exact.Id);
                continue;
            }
            var contains = rolesById.Values.FirstOrDefault(r => r.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                || name.Contains(r.Name, StringComparison.OrdinalIgnoreCase));
            if (contains is not null && !result.Contains(contains.Id))
                result.Add(contains.Id);
        }
        return result;
    }

    /// <summary>Renders the group message window as a labeled transcript.</summary>
    private static string FormatTranscript(IReadOnlyList<Message> messages, IReadOnlyDictionary<int, Role> rolesById)
    {
        var sb = new StringBuilder();
        foreach (var m in messages)
        {
            if (m.Author == MessageAuthor.System) continue;
            if (m.Author == MessageAuthor.User)
                sb.Append("[用户] ").Append(m.Content).Append('\n');
            else
                sb.Append('[')
                  .Append(rolesById.TryGetValue(m.RoleId, out var r) ? r.Name : "AI")
                  .Append("] ")
                  .Append(m.Content)
                  .Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Group-chat rules appended to each speaker's system prompt.</summary>
    private static string BuildGroupRules(Role role, IReadOnlyDictionary<int, Role> rolesById, GroupChatSettings g)
    {
        var sb = new StringBuilder();
        sb.Append("\n\n[群聊规则]\n");
        sb.Append("你正在一个群聊中。群内成员：");
        sb.Append(string.Join("、", rolesById.Values.Select(r => r.Name))).Append("。\n");
        sb.Append("下面是群聊记录，每行以 `[说话者名]` 开头；其中标注为 `")
          .Append(role.Name).Append("` 的是你本人之前说过的话。\n");
        sb.Append("请始终以 ").Append(role.Name).Append(" 的身份回复，不要跳出角色，不要透露你是AI模型。\n");
        if (g.RespondToOtherAgents)
            sb.Append("你可以回应用户，也可以回应或反驳其他角色。发言要简短自然，不必附和别人，可提出不同观点。\n");
        else
            sb.Append("请主要回应用户。发言要简短自然。\n");
        return sb.ToString();
    }

    private static int EstimateTokens(string text, AiSettings settings)
        => Math.Max(1, (int)(text.Length / Math.Max(0.1, settings.CharsPerToken)));
}
