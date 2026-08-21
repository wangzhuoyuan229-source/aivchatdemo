# ROADMAP

> `v1.3.2`/`v1.3.3` 已交付（`v1.3.3`：撤回回填 + @提及 + 极简新建 + 隐藏时间）。`git tag v1.3.3` 待打，本文件仅保留未完成 P1/P2。

---

## v1.3.x — 工程债与体验修复（P1）

> 目标：`v1.3.2` 之后以 `v1.3.3+` 迭代还债+小步快跑，不改产品形态，按 `AGENTS.md` 单向依赖与 `CHATAPP_DATA_DIR` 隔离。

### A. 依赖健康

| 编号 | 位置 | 现状 | 实现方案 |
|---|---|---|---|
| P1-A1 | `global.json:3` `8.0.424` | 2024-11补丁滞后 | 升至 `8.0.414+`/`8.0.12x`，`dotnet restore` 重锁，CI 加 `dotnet list package --vulnerable` 门禁 |
| P1-A2 | `ChatApp.Infrastructure.csproj:11` EF 8.0.11 | 滞后3 patch | 升 `Microsoft.Data.Sqlite/EFCore.Sqlite 8.0.15`，跑迁移幂等测试 |
| P1-A3 | `ChatApp.AI.csproj:12` SK 1.21.1 | 滞后11月，`DeepSeek-V3.1` 推理`reasoning_content`无法透出 | 升 SK 1.60+，解 `SKEXP0001`，`ChatOrchestrator` 增加 `reasoning_content` 透出或丢弃策略，补提示词契约测试 |
| P1-A4 | `SkiaSharp 2.88.9` `Avalonia 11.3.20` | HEIC/WebP `IncompleteInput` | 升 SkiaSharp 3.x，`KnowledgeService.CreateSquareAvatarJpeg:983` 加重试+回退emoji |
| P1-A5 | `ChatApp.UI.csproj:21-25` `Configuration 8.0.0` | 与 `Hosting 8.0.1` 偏斜 | 对齐至 `8.0.1/8.0.2`，启用 CPM `Directory.Packages.props` |
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
| P1-AI6 | `GroupChatOrchestrator.cs:298` 未解析 `@`，`FreeForAll` 桩 | `@` 失效 | `Regex @` 预解析强制入 `picked`，`FreeForAll` 自评+导演评分 |

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
| P1-Doc3 | 无版本一致性门禁 | 加 `scripts/verify-version.ps1` 比对 `Version/CFBundle/MainWindow/README/BundleVersion/tag` |

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
| **@点名** | 提示词有但无解析 | 见 P1-AI6 |
| **群聊动态增删** | 仅创建时选成员 | `ManageGroupMembersWindow` + 重排 |
| **内置知识库增量** | 无CI | `scripts/verify-bundle.ps1` 哈希比对 |

---

> 已移除 `v1.3.2` 已交付的 P0（SiliconFlow统一默认、侧边栏帮助、预设锁定、视觉迁移、滚动修复）。`file:line` 基于 `2026-08-22` 工作区。
