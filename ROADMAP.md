# ROADMAP

> 当前正式版为 `v1.3.7`。`@提及` 已在 `v1.3.3` 交付，FreeForAll 仍属于后续规划。

---

## v1.3.7 — 角色启动指令与稳定性修复

详细范围、实施顺序和验收标准见 [`docs/v1.3.7-plan.md`](docs/v1.3.7-plan.md)。本版本聚焦以下事项：

> 实现已完成并记录于 CHANGELOG 的 `Unreleased` 区域；正式版本号、安装包与发布哈希将在发布时更新。

- 新建角色时，按固定模板原文组装角色扮演启动指令；只填充新建菜单中的角色身份、角色性格、用户身份和其余设定，并在首次对话前作为不可见系统指令交给 AI。
- 最近群聊支持确认删除；删除会话及其消息、附件、成员关系和派生记忆，同时保留成员角色。
- 角色库使用“角色 / 群聊”独立分块切换，两类卡片不再互相占用列表高度。
- 长期记忆改为所有角色共享召回，记忆条目标注来源角色并允许在统一管理窗口中编辑。
- 修复聊天输入框 `Enter` 快速发送的回归问题，同时保留 `Shift+Enter` 换行、输入法确认候选词和群聊 `@` 候选选择行为。
- 将新安装或未配置状态下的默认对话模型调整为 DeepSeek V4 Flash（SiliconFlow ID：`deepseek-ai/DeepSeek-V4-Flash`），不覆盖用户已经保存的模型，不联动修改 Embedding 或视觉模型。
- 围绕远程 API、消息发送状态、配置迁移、数据库、后台任务和敏感信息保护补强鲁棒性，并以自动化测试覆盖关键失败与恢复路径。

---

## v1.3.5 — 安全与基线修复

- Semantic Kernel `1.71.0`，迁移到新的 `IEmbeddingGenerator` 接口。
- EF Core / Microsoft.Data.Sqlite `8.0.30`，SQLitePCLRaw bundle `3.0.5`。
- NuGet Central Package Management、依赖锁文件和全量漏洞审计门禁。
- 隐藏尚未实现的语音、好感度、FreeForAll 设置入口；旧 FreeForAll 配置回退为 Hybrid。
- 增加版本一致性测试，校准 README、CHANGELOG、ROADMAP 与应用版本。

---

## v1.3.x — 工程债与体验修复（P1）

> `v1.3.5` 之后继续小步偿还工程债，不改变产品形态，保持 `AGENTS.md` 单向依赖与 `CHATAPP_DATA_DIR` 隔离。

### A. 依赖健康

| 编号 | 位置 | 现状 | 实现方案 |
|---|---|---|---|
| P1-A4 | `SkiaSharp 2.88.9` `Avalonia 11.3.20` | HEIC/WebP `IncompleteInput` | 升 SkiaSharp 3.x，`KnowledgeService.CreateSquareAvatarJpeg:983` 加重试+回退emoji |
| P1-A6 | `ChatApp.Tests.csproj:25` 直引 `ChatApp.UI` | 破分层 | 抽 `ChatApp.Application` 或测试仅依赖 `Core/AI` 接口，`Avalonia.Headless` 隔离 |
| P1-A7 | `AiModule.cs:11` `MultimodalClient(new HttpClient)` / `KernelFactory.cs:47` per-Build new `HttpClient` | 无 `IHttpClientFactory` 泄漏 | 注册 `AddHttpClient("chat"/"embedding"/"vision").SetHandlerLifetime(15min)`，注入 `IHttpClientFactory` |

### B. 设置与配置

| 编号 | 位置 | 现状 | 实现方案 |
|---|---|---|---|
| P1-S1 | `ConfigurationService.cs:53` 明文JSON存 `chatapp.db` | 仅日志打码 | `DataProtection` + OS Keychain，`AppPaths` 文件 `chmod 600` |
| P1-S2 | `SettingsViewModel.cs:362,568` 700ms防抖与 `Test*Connection` 共用 `_saveGate` | 竞态 | 探针前 `Cancel _autoSaveCts`，独立文案 |
| P1-S4 | `SettingsViewModel.cs:271` 换 `EmbeddingModel` 无提醒 | 同维不同空间静默劣化 | 存 `lastEmbeddingModel`，`BundledKnowledgeService` 顶部横幅一键重建 |

### C. AI / RAG

| 编号 | 位置 | 现状 | 实现方案 |
|---|---|---|---|
| P1-AI1 | `ChatOrchestrator.cs:236` `Dictionary<int,ContextSummaryState>` 常驻 | 不落库，删消息后过期 | 落 `Settings` 或新表 `ConversationSummaries`，LRU 1条/会话 |
| P1-AI2 | `MemoryService.cs:66` 1000字符硬切，`memconv:{Id}` 孤儿 | 切句中断，群聊不抽取 | 复用 `TextChunker`，`DeleteConversation` GC，群聊逐角色抽取 |
| P1-AI3 | `KnowledgeService.cs:484` 先拉全量再过滤，`SqliteVectorStore` 全量进内存 | 5k+ `O(total)` | 按 `GroupId` SQL层过滤或 `Scope=knowledge:{groupId}` 分组 |
| P1-AI4 | `OpenAIEmbeddingService.cs:14` `MaxInputsPerRequest=10` | 硅基支持32吞吐减半 | 按端点自适应 `siliconflow→32 else 10`，`_requestGate 4→6` |
| P1-AI5 | `ImageDescriptionService.cs:312` 文件名插提示词 | 注入风险 | `SingleLine`截断+ `<filename>` 包裹 |
| P1-AI6 | `FreeForAll` 仍为保留枚举值 | 尚无自评与导演评分实现 | 独立实现 Agent 自评、导演评分、调用上限和失败回退 |

### D. 数据层

| 编号 | 位置 | 现状 | 实现方案 |
|---|---|---|---|
| P1-D1 | `AppPaths.cs:31` 静态构造即 `CreateDirectory` | 污染真实目录 | 改惰性 `Lazy` + 显式初始化 |
| P1-D2 | `AppDbContext.cs:84` 缺索引 | 全表扫 | 加 `IX_KnowledgeDocuments_GroupId_SourceRelativePath` |
| P1-D3 | `InfrastructureModule.cs:28` 6次开关连接无事务 | 断电留垃圾 | 单连接+事务包裹重建 |
| P1-D4 | `ChatHistoryService.cs:143` 删文件吞异常 | 孤儿文件 | 启动反向GC `StorageKey` |
| P1-D5 | `SqliteVectorStore.cs:92` 全局 `_cache` 常驻 | 跨测试泄漏 | 精确 `Remove(Id)`，加容量上限 |
| P1-D6 | `Conversation.RoleId` 可空无约束 | 可建非法 | App层守卫或DB `CHECK` |

### E. UI/UX

| 编号 | 位置 | 现状 | 实现方案 |
|---|---|---|---|
| P1-U1 | `MainWindow.xaml:32` 中栏折叠后未清会话 | 残留字幕 | `Navigate` 非 `roles` 时清 `Chat.Conversation` |
| P1-U2 | `ChatView.xaml:68` `ScrollViewer>ItemsControl>VirtualizingStackPanel` | 虚拟化失效 | 改 `ListBox`/`ItemsRepeater` 内置虚拟化 |
| P1-U3 | `KnowledgeViewModel.cs:59` `IsAllSelected` 竞态 | 全选错乱 | `LoadAsync` 前 `false` 再 `Clear` |
| P1-U4 | `SettingsViewModel.cs:509` `Math.Clamp` 静默 | 9999存200 | 换 `NumericUpDown` 或先校验 |
| P1-U6 | `Converters.cs:94` 位图解码常驻 | 200MB+ | `MemoryCache` 100条LRU + `WeakReference` |

### F. 安全

| 编号 | 位置 | 现状 | 实现方案 |
|---|---|---|---|
| P1-SC1 | `SecretRedaction.cs:20` 精确替换 | 漏 `sk-` 空白 | 正则 `sk-[A-Za-z0-9-_]{20,}` + `Bearer` 兜底 |
| P1-SC2 | `AppPaths.cs:40` 未校验 `..` | 穿越风险 | 禁 `..`，`GetFullPath` 校验 |
| P1-SC3 | `RemoteApiEndpointPolicy.cs:50` 任意HTTPS放行 | 钓鱼 | allowlist 弱提示 |

### G. 测试

| 编号 | 现状 | 实现方案 |
|---|---|---|
| P1-T1 | `GroupChatSpeakerSelectionTests` 仅静态 | 补 `PickSpeakersHybridAsync` 联调 |
| P1-T2 | `MigrateConversationExtrasAsync` 无幂等 | 仿旧模式覆盖空库二次迁移 |
| P1-T3 | `RollingFileLoggerTests` 无并发 | 加 `Parallel.For` |
| P1-T4 | 无 `BundledKnowledge` 计数断言 | 加 `BundledKnowledgeContentTests` |

### H. 文档发布

| 编号 | 位置 | 现状 | 实现方案 |
|---|---|---|---|
| P1-Doc2 | `publish-win-x64.ps1:4` 无 `--self-contained` | 对齐macOS `-r win-x64 --self-contained true` |

---

## v1.4.0+ / v2.0 — 长期与 Phase2（P2）

### 架构
* **P2-A1** `RoleService.cs:95` 手写级联删 → 补 `ON DELETE CASCADE`
* **P2-A2** `SqliteVectorStore.cs:126` 线性余弦 → `sqlite-vec` + HNSW
* **P2-A3** 取消链路不一致 → `CreateLinkedTokenSource`

### AI/RAG
* **P2-AI1** `ChatOrchestrator.cs:436` 单体SystemPrompt → 拆 `SystemPromptBuilder` + `Evals/grounding-cases.json`
* **P2-AI2** `CitedDocumentIds` 全量持久化 → 要求 `[[cite:docId]]` 仅存实际引用
* **P2-AI3** `AiSettings.cs:74` 图片阈值全局 → 分组覆写

### 数据/UI
* **P2-D1** 无 `VACUUM/WAL` → 空闲 `VACUUM`
* **P2-U1** `RoleListView` 空库无引导 → 新装“创建首个角色”卡片
* **P2-U2** `ChatViewModel.cs:180` 跳引用丢滚动位 → 保留 `ScrollOffset`

### 安全/测试
* **P2-SC1** 无CPM/lock → 启 `central package management` + `packages.lock.json`
* **P2-T1** 无E2E → `Avalonia.Headless.XUnit`

### Phase2 兑现

| 规划 | 现状 | 实现 |
|---|---|---|
| **FreeForAll** | `GroupChatSettings.cs:13` 枚举有但按 `RoundRobin` | 自评+导演评分两段式 |
| **引用调试面板** | 仅记数量 | `KnowledgeRetrievalDebugView` |
| **语音** | `IExtensionServices` 桩 | `Windows.Media.Speech` / `AVSpeechSynthesizer` |
| **好感度** | `Role.Affinity` 有字段未注册 | `AffinityService` + 心形进度 |
| **@点名** | `v1.3.3` 已交付 | 保持提及优先与群聊成员边界契约测试 |
| **群聊动态增删** | 仅创建时选成员 | `ManageGroupMembersWindow` + 重排 |
| **内置知识库增量** | 无CI | `scripts/verify-bundle.ps1` 哈希比对 |

---

> 已移除 `v1.3.5` 已完成的依赖安全、CPM、锁文件、漏洞门禁和版本一致性事项。`file:line` 需在后续实施前重新核对。
