using System.Text;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ChatApp.AI.SemanticKernel;

/// <summary>
/// Orchestrates a chat turn: persists the user message, assembles context
/// (system prompt + long-term memory + knowledge + short-term window), streams
/// the assistant reply via Semantic Kernel and persists the reply (F3).
/// </summary>
public class ChatOrchestrator : IChatService
{
    private readonly IConfigurationService _config;
    private readonly IChatHistoryService _history;
    private readonly IRoleService _roles;
    private readonly IMemoryService _memory;
    private readonly IKnowledgeService _knowledge;
    private readonly IServiceProvider _services;
    private readonly ILogger<ChatOrchestrator> _logger;

    public ChatOrchestrator(
        IConfigurationService config,
        IChatHistoryService history,
        IRoleService roles,
        IMemoryService memory,
        IKnowledgeService knowledge,
        IServiceProvider services,
        ILogger<ChatOrchestrator> logger)
    {
        _config = config;
        _history = history;
        _roles = roles;
        _memory = memory;
        _knowledge = knowledge;
        _services = services;
        _logger = logger;
    }

    public async Task<Message> SendAsync(int conversationId, string userText, IProgress<string>? streamingProgress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userText))
            throw new ArgumentException("消息不能为空。", nameof(userText));

        var settings = await _config.LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.ChatModel))
            throw new InvalidOperationException("尚未配置 API Key 或聊天模型，请先打开设置。");

        var conv = await _history.GetConversationAsync(conversationId, ct)
            ?? throw new InvalidOperationException("会话不存在。");
        if (conv.Type != ConversationType.Private || conv.RoleId is null)
            throw new InvalidOperationException("该会话不是私聊会话。");
        var roleId = conv.RoleId.Value;
        var role = await _roles.GetAsync(roleId, ct)
            ?? throw new InvalidOperationException("角色不存在。");

        // 1. Persist user message.
        var userMsg = await _history.AddMessageAsync(new Message
        {
            ConversationId = conversationId,
            RoleId = roleId,
            Author = MessageAuthor.User,
            Content = userText,
            TokenEstimate = EstimateTokens(userText, settings)
        }, ct);

        // 2. Long-term memory (best effort).
        IReadOnlyList<VectorSearchHit> memoryHits = Array.Empty<VectorSearchHit>();
        if (settings.EnableLongTermMemory)
        {
            try
            {
                await _memory.ProcessConversationAsync(conversationId, ct);
                memoryHits = await _memory.RecallAsync(role.Id, userText, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Long-term memory recall failed; continuing without it.");
            }
        }

        // 3. Knowledge-base retrieval (best effort).
        IReadOnlyList<VectorSearchHit> kbHits = Array.Empty<VectorSearchHit>();
        if (settings.EnableKnowledgeBase)
        {
            try
            {
                kbHits = await _knowledge.RetrieveAsync(userText, settings.KnowledgeTopK, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Knowledge-base retrieval failed; continuing without it.");
            }
        }

        // 4. Short-term context window.
        var all = await _history.GetMessagesAsync(conversationId, settings.ContextWindowSize, ct);
        var window = all.Count > settings.ContextWindowSize
            ? all.Skip(all.Count - settings.ContextWindowSize).ToList()
            : all.ToList();

        // 5. Assemble ChatHistory.
        var systemPrompt = BuildSystemPrompt(role, memoryHits, kbHits);
        var skHistory = new ChatHistory(systemPrompt);
        foreach (var m in window)
        {
            if (m.Author == MessageAuthor.System) continue;
            skHistory.AddMessage(
                m.Author == MessageAuthor.User ? AuthorRole.User : AuthorRole.Assistant,
                m.Content);
        }

        // 6. Stream completion.
        var kernel = KernelFactory.Build(settings);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var execSettings = new OpenAIPromptExecutionSettings { Temperature = 0.8, TopP = 1.0 };

        var sb = new StringBuilder();
        await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(skHistory, execSettings, kernel, ct))
        {
            var delta = chunk.Content;
            if (string.IsNullOrEmpty(delta)) continue;
            sb.Append(delta);
            streamingProgress?.Report(delta);
        }

        var reply = sb.ToString();
        if (string.IsNullOrWhiteSpace(reply))
            reply = "（未收到模型回复，请检查网络或模型名称。）";

        // 7. Persist assistant reply.
        var assistantMsg = await _history.AddMessageAsync(new Message
        {
            ConversationId = conversationId,
            RoleId = roleId,
            Author = MessageAuthor.Assistant,
            Content = reply,
            TokenEstimate = EstimateTokens(reply, settings)
        }, ct);

        // P2: affinity update (best effort, optional).
        if (settings.EnableAffinity)
        {
            try
            {
                var affinity = _services.GetService<IAffinityService>();
                if (affinity is not null)
                    await affinity.UpdateAsync(role.Id, userText, reply, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Affinity update skipped.");
            }
        }

        return assistantMsg;
    }

    public async Task<Message> GreetAsync(int conversationId, CancellationToken ct = default)
    {
        var settings = await _config.LoadAsync(ct);
        var conv = await _history.GetConversationAsync(conversationId, ct)
            ?? throw new InvalidOperationException("会话不存在。");
        if (conv.Type != ConversationType.Private || conv.RoleId is null)
            throw new InvalidOperationException("该会话不是私聊会话。");
        var roleId = conv.RoleId.Value;
        var role = await _roles.GetAsync(roleId, ct)
            ?? throw new InvalidOperationException("角色不存在。");

        // Prefer the role's authored greeting (no API call). Otherwise ask the model.
        string greeting;
        if (!string.IsNullOrWhiteSpace(role.Greeting) && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            greeting = role.Greeting;
        }
        else if (!string.IsNullOrWhiteSpace(role.Greeting))
        {
            greeting = role.Greeting;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                throw new InvalidOperationException("尚未配置 API Key，无法生成问候语。");
            var systemPrompt = BuildSystemPrompt(role, Array.Empty<VectorSearchHit>(), Array.Empty<VectorSearchHit>());
            var skHistory = new ChatHistory(systemPrompt);
            skHistory.AddUserMessage("请用你的身份给我一个简短的开场问候。");
            var kernel = KernelFactory.Build(settings);
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var sb = new StringBuilder();
            await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(skHistory,
                new OpenAIPromptExecutionSettings { Temperature = 0.9 }, kernel, ct))
            {
                if (!string.IsNullOrEmpty(chunk.Content)) sb.Append(chunk.Content);
            }
            greeting = sb.ToString();
        }

        return await _history.AddMessageAsync(new Message
        {
            ConversationId = conversationId,
            RoleId = role.Id,
            Author = MessageAuthor.Assistant,
            Content = greeting,
            TokenEstimate = EstimateTokens(greeting, settings)
        }, ct);
    }

    internal static string BuildSystemPrompt(Role role, IReadOnlyList<VectorSearchHit> memory, IReadOnlyList<VectorSearchHit> knowledge)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(role.SystemPrompt))
        {
            sb.Append(role.SystemPrompt);
        }
        else
        {
            sb.Append($"你是一位{role.Name}。");
            if (!string.IsNullOrWhiteSpace(role.Background)) sb.Append(role.Background).Append('\n');
            if (!string.IsNullOrWhiteSpace(role.Personality)) sb.Append($"你的性格特征是：{role.Personality}\n");
            if (!string.IsNullOrWhiteSpace(role.SpeakingStyle)) sb.Append($"你的说话风格是：{role.SpeakingStyle}\n");
            sb.Append($"请始终以{role.Name}的身份进行对话，不要跳出角色，不要透露你是AI模型。");
        }

        if (memory is { Count: > 0 })
        {
            sb.Append("\n\n[长期记忆]\n以下是你记得的与该用户过往交流的片段，可酌情引用以体现连贯性：\n");
            for (int i = 0; i < memory.Count; i++)
                sb.Append($"- {memory[i].Record.Content}\n");
        }

        if (knowledge is { Count: > 0 })
        {
            sb.Append("\n[知识库参考]\n以下是与当前话题相关的设定/资料，请在回答中遵循其设定；若与角色基础设定冲突，以角色设定为准：\n");
            for (int i = 0; i < knowledge.Count; i++)
                sb.Append($"--- 片段 {i + 1} ---\n{knowledge[i].Record.Content}\n");
        }

        return sb.ToString();
    }

    private static int EstimateTokens(string text, AiSettings settings)
        => Math.Max(1, (int)(text.Length / Math.Max(0.1, settings.CharsPerToken)));
}
