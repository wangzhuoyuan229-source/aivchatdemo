using ChatApp.Core.Services;
using ChatApp.AI.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.AI;

public static class AiModule
{
    public static IServiceCollection AddChatAppAi(this IServiceCollection services)
    {
services.AddSingleton<IMultimodalClient>(_ => new MultimodalClient(new HttpClient()));
        services.AddSingleton<IImageDescriptionService, ImageDescriptionService>();
        services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();
        services.AddSingleton<IChatService, ChatOrchestrator>();
        services.AddSingleton<IGroupChatService, GroupChatOrchestrator>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<IKnowledgeService, KnowledgeService>();
        services.AddSingleton<IApiProbeService, ApiProbeService>();
        return services;
    }
}
