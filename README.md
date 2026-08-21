# AI 角色扮演聊天应用 (ChatApp)

> .NET 8 + Avalonia 跨平台桌面应用 | Semantic Kernel AI 引擎 | EF Core + SQLite 持久化

当前版本：**v1.3.6** · [更新日志](CHANGELOG.md)

## 项目概述

ChatApp 是一款支持 macOS 与 Windows 的桌面 AI 角色扮演聊天应用，支持 1:1 私聊与多 AI 群聊。用户可以创建/管理 AI 角色（人设、性格、说话风格），导入知识库文档，并通过远程 OpenAI 兼容 API 驱动角色进行对话。应用采用 BYOK（自带密钥）模式，只允许 HTTPS 托管 API，不提供本地模型入口。

## 模型接入策略

默认服务为 SiliconFlow 统一API，默认聊天模型为 `deepseek-ai/DeepSeek-V3.1`（`BAAI/bge-m3` + `Qwen/Qwen3-VL-32B-Instruct`）。设置页仍允许接入其他
OpenAI 兼容的远程 HTTPS API；localhost、回环地址、私有网络地址和普通 HTTP 地址
会在界面、持久化与运行时三层被拒绝。仓库中的 LoRA 训练材料仅保留用于离线研究和
评测，不再接入桌面应用；详情见 [training/README.md](training/README.md)。

设置页提供“统一 API 模式”开关：开启后聊天、Embedding 与多模态识图共用同一个
端点和 API Key（适合 SiliconFlow 等聚合平台），通过“统一 API 预设”一键选择三模型
（SiliconFlow 推荐 `deepseek-ai/DeepSeek-V3.1 + BAAI/bge-m3 + Qwen3-VL`，阿里百炼 `qwen-plus + text-embedding-v4 + qwen3-vl-flash`，OpenAI `gpt-4o-mini`）；
预设内单独模型选择自动禁用，`自定义` 预设除外；关闭时保留三路独立端点与密钥，两种模式可随时切换。

图片知识默认使用另一套完全独立的多模态 API 配置。内置阿里云百炼
(`qwen3-vl-flash`)、智谱 (`glm-4.6v-flash`)、火山方舟 Responses API
(`doubao-seed-2-0-lite-260215`) 和 SiliconFlow
(`Qwen/Qwen3-VL-32B-Instruct`) 预设，也可填写自定义 Chat Completions 或
Responses 兼容服务。多模态 API 只在导入图片和“重新识图”时调用，聊天回复阶段仍使用
现有聊天模型和已保存的图片描述，不产生额外视觉调用。

## macOS Release 使用说明

当前正式版为 [v1.3.6](https://github.com/wangzhuoyuan229-source/aivchatdemo/releases/tag/v1.3.6)，适用于 Apple Silicon（M1/M2/M3/M4 等 arm64）Mac，要求 macOS 12 或更高版本。安装包已包含 .NET 运行时，普通用户不需要另外安装 .NET SDK。

### 下载与安装

1. 下载 [ChatApp-macOS-arm64.zip](https://github.com/wangzhuoyuan229-source/aivchatdemo/releases/download/v1.3.6/ChatApp-macOS-arm64.zip)。
2. 双击 ZIP 文件解压，得到 `ChatApp.app`。
3. 将 `ChatApp.app` 拖入“应用程序（Applications）”文件夹。
4. 首次启动时，在 Finder 中右键 `ChatApp.app`，选择“打开”，然后在系统提示中再次选择“打开”。后续可正常双击启动。
5. 如果 macOS 仍然阻止启动，请进入“系统设置 → 隐私与安全性”，找到 ChatApp 的拦截提示并选择“仍要打开”。

如果系统显示“ChatApp 已损坏”，请先核对下方 SHA-256；确认文件一致且确实从本仓库下载后，在终端执行：

```bash
xattr -dr com.apple.quarantine /Applications/ChatApp.app
open /Applications/ChatApp.app
```

该命令只应对已核验来源和哈希的 ChatApp 使用，不要用它绕过未知应用的安全检查。

可选：下载后在终端校验文件完整性：

```bash
shasum -a 256 ~/Downloads/ChatApp-macOS-arm64.zip
```

v1.3.6 的 SHA-256：

```text
f39066ad487f4e850caf889c7b73df505d1317112205aa926762846bc2a99d22
```

### 首次配置

1. 启动 ChatApp，打开左侧“设置”。
2. 填写兼容 OpenAI 协议的 API Base URL 和自己的 API Key。
3. 选择或填写聊天模型与 Embedding 模型；如需图片知识，再配置独立多模态服务并点击“测试多模态连接”。可点击“测试聊天连接”/“测试 Embedding 连接”验证端点与密钥是否可用。停止编辑约 700ms 后设置会自动保存。
4. 看到“已自动保存”提示后，进入角色库开始对话。

使用 SiliconFlow 等聚合平台时，可勾选“统一 API 模式”：聊天、Embedding 和多模态
识图共用同一端点和 Key，只需分别为各功能填写模型 ID（模型 ID 需使用完整的
`组织/模型名` 格式，如 `deepseek-ai/DeepSeek-V3`、`BAAI/bge-m3`）。

使用 DeepSeek 聊天并启用知识库时，可在“Embedding 服务预设”选择
“阿里云百炼（推荐 · text-embedding-v4）”。应用会自动填写
`https://dashscope.aliyuncs.com/compatible-mode/v1` 和 `text-embedding-v4`，
用户只需填写独立的百炼 API Key。

### 内置知识库

仓库根目录的 `知识库/` 会在构建时自动复制到应用发布内容（`.DS_Store` 除外），
目前包含文本资料和角色立绘。启用知识库并配置 Embedding 后，应用会在后台把这些资料
自动放入“内置知识库”分组并生成索引：

- 首次安装到一台新设备时，需要使用用户配置的 Embedding API 自动生成一次向量；这是因为不同用户可能选择不同的向量模型，发布包不能安全地共用同一套预生成向量。
- 后续启动和覆盖安装会复用本机数据库中的索引，仅做快速完整性检查，不会重复调用 Embedding 或多模态 API。
- 首次索引被取消、断网或部分失败时，已完成项会保留，下次启动仅续传缺失项。
- 以前手动导入且相对路径相同的未分组资料会迁移到“内置知识库”分组并直接复用。
- 对话仍严格遵循角色的知识分组绑定；需要在角色设置中为相应角色绑定“内置知识库”。

维护内置资料时，必须将根目录 `知识库/` 一并提交到版本控制。新增路径会被自动发现；
如果原路径不变但文件内容发生变化，请同时更新
`ChatApp.UI/Services/BundledKnowledgeService.cs` 中的 `BundleVersion`，使旧索引在升级时重建。

应用采用 BYOK（自带密钥）模式，Release 中没有预置任何 API Key。用户填写的设置、聊天记录和知识库保存在本机：

```text
~/Library/Application Support/ChatApp/
```

其中 `chatapp.db` 包含本地设置和聊天数据，请勿上传、公开或发送给其他人。删除 `ChatApp.app` 不会自动删除这些用户数据。

### 升级与卸载

- 升级：退出旧版本，用新版 `ChatApp.app` 替换“应用程序”中的旧版本；本地数据会保留。
- 卸载程序：删除“应用程序”中的 `ChatApp.app`。
- 同时清除全部本地数据：确认不再需要聊天记录和知识库后，再删除 `~/Library/Application Support/ChatApp/`。

### 开发者构建 macOS 版本

安装 .NET 8 SDK 后，在项目根目录运行：

```bash
./publish-macos.sh
```

脚本会自动识别 Apple Silicon 或 Intel Mac，生成 `publish/osx-arm64/ChatApp.app` 或 `publish/osx-x64/ChatApp.app`。正式公开分发建议使用 Apple Developer ID 签名和公证。

## Windows 发布

在 Windows 且安装 .NET 8 SDK 后运行：

```powershell
.\publish-win-x64.ps1
```

生成内容位于 `publish/win-x64/`。由于内置知识原文件位于同目录的
`BundledKnowledge/`，发布时必须打包整个 `win-x64` 文件夹，不能只分发
`ChatApp.UI.exe`。API Key 不写入程序或发布目录，而是由用户在应用设置中输入并保存到本机 `%LOCALAPPDATA%\\ChatApp`；发布前不要把本地数据库或配置文件复制进发布包。

## 技术栈

| 层级 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET 8 | 8.0 |
| UI 框架 | Avalonia | 11.3.20 |
| MVVM 框架 | CommunityToolkit.Mvvm | 8.2.2 |
| AI 引擎 | Microsoft Semantic Kernel | 1.71.0 |
| 数据库 | EF Core + SQLite (Microsoft.Data.Sqlite) | 8.0.30 |
| DI 容器 | Microsoft.Extensions.Hosting | 10.0.11 |
| PDF 解析 | PdfPig | 0.1.9 |
| 向量存储 | 自研 SQLite Vector Store | — |

---

## 项目架构

```
ChatApp.sln
├── ChatApp.Core            # 领域层：模型、服务接口、设置
├── ChatApp.Infrastructure  # 基础设施层：数据访问、仓储、向量存储
├── ChatApp.AI              # AI 层：语义内核、编排器、记忆/知识服务
└── ChatApp.UI              # 表现层：Avalonia 跨平台界面、MVVM 视图模型
```

### 依赖关系

```
ChatApp.UI ──────────────► ChatApp.AI ─────────► ChatApp.Infrastructure ──► ChatApp.Core
   │                           │                        │
   └───────────────────────────┼────────────────────────┤
                               └────────────────────────┘
```

- **ChatApp.Core**：无项目依赖，仅依赖 DI/日志抽象包
- **ChatApp.Infrastructure**：依赖 ChatApp.Core
- **ChatApp.AI**：依赖 ChatApp.Core + ChatApp.Infrastructure
- **ChatApp.UI**：依赖上述三个项目

---

## 功能特性

### 已实现功能

| 功能 | 描述 |
|------|------|
| 🤖 **AI 角色管理** | 创建/编辑/删除角色，支持头像、人设、背景、用户所扮演身份、性格、说话风格与示范对话 |
| 🧑‍🎨 **知识图片头像** | 新建角色时先查找名称匹配的图片目录/文件（内置图片尚未生成向量时也可使用），再回退语义检索；独立多模态模型定位主要人物面部，本地放大裁剪为 256×256 头像快照，无命中时保留 emoji |
| 📦 **预设角色库** | 6 个内置角色（林溪、诸葛亮、福尔摩斯、Emma、苏念、李白） |
| 💬 **1:1 私聊** | 单角色对话，流式输出，支持上下文窗口管理 |
| 👥 **AI 群聊** | 多角色同台对话，支持混合导演(Hybrid)与轮询(RoundRobin)两种模式；群头像可自定义，未设置时自动使用成员头像拼图 |
| 📚 **严格知识库 RAG** | 导入 txt/md/pdf 文档，按角色绑定的分组检索；无命中时不编造设定 |
| 🖼️ **知识图片检索** | 导入 PNG/JPEG/WebP，独立多模态 API 生成中文描述与标签；私聊/群聊按角色检索并按需附带至多 3 张原图快照 |
| 🧠 **长期记忆** | 按角色隔离的长期记忆，自动批量抽取+向量召回；可查看/新增/编辑/删除/清空单角色记忆（群聊仅显示当前发言者自己的记忆） |
| 💬 **消息操作** | 复制消息内容、重新生成最后一条 AI 回复、编辑已发送消息后重发（发送后替换该消息及其后的回复） |
| 📌 **会话整理** | 私聊/群聊支持重命名与置顶（置顶优先排序）；一键导出为 Markdown（含角色名、时间与附件快照）或结构化 JSON |
| 📎 **知识引用溯源** | AI 回复下方展示所引用的知识文档标签，点击跳转到知识库对应文档 |
| 🗜️ **长对话摘要压缩** | 上下文接近上限时用 LLM 生成“摘要 + 关键记忆点”替换早期消息，保留最近完整消息；失败时回退原有截断逻辑 |
| ⚙️ **BYOK 设置** | 自定义 API 端点/密钥/模型，支持 OpenAI 兼容服务；统一 API 模式下通过预设一键填充对话/向量/视觉三模型（SiliconFlow/阿里百炼/OpenAI），预设内单独模型选择自动禁用（自定义除外），每项旁 `?` 提供悬停/点击通俗帮助 |
| 💬 **帮助与支持** | 侧边栏常驻 `💬` 入口，一键查看开发者社交账户（GitHub/B站/小红书/邮箱/反馈，欢迎私信），支持外链打开与复制，弹窗 `Panel.ZIndex` 置顶，不占滚动空间 |
| ⌨️ **快捷发送** | 聊天输入框 `回车` 发送、`Shift+回车` 换行 |
| 📁 **知识库目录与批处理** | 一次选择多个多层文件夹递归导入，保留完整相对目录树；可按分组/目录范围全选、移动、删除及批量重新识图 |
| 📦 **内置知识库** | 根目录 `知识库/` 随应用发布，首次自动索引并支持断点续传，后续启动和覆盖安装直接复用本机向量 |
| ⚡ **性能与稳定性** | 消息列表虚拟化渲染 + 120 条游标分页（可"加载更早消息"）；启动加载并行化；记忆/知识召回会话级缓存（60 秒 TTL，修改即失效）；设置页一键"测试聊天/Embedding 连接"（失败原因分级、不回显密钥） |
| 📄 **本地日志** | 按日滚动写入用户数据目录 `logs/chatapp-YYYY-MM-DD.log`，保留 7 天；所有日志写盘前自动打码 API Key |

### Phase 2 规划

- FreeForAll 群聊模式（Agent 自评 + 导演评分制）
- 知识库引用调试面板
- 语音输入/输出 (STT/TTS)
- 好感度系统 (Affinity)
- @ 点名机制
- 群聊成员动态增删

---

## 项目结构详解

### ChatApp.Core — 领域层

```
ChatApp.Core/
├── Models/
│   ├── Role.cs                  # AI 角色（人设、性格、示范对话、问候语）
│   ├── Message.cs               # 消息 + 会话 + MessageAuthor 枚举
│   ├── ConversationType.cs      # 会话类型枚举：Private=0, Group=1
│   ├── ConversationMember.cs    # 群聊成员关联（含 DisplayOrder）
│   ├── KnowledgeDocument.cs     # 知识库文档 + KnowledgeChunk
│   ├── KnowledgeGroup.cs        # 知识库分组
│   └── MemoryEntry.cs           # 长期记忆条目 + VectorRecord + VectorSearchHit
├── Services/
│   ├── IChatService.cs          # 1:1 聊天服务接口
│   ├── IGroupChatService.cs     # 群聊服务接口
│   ├── GroupChatEvent.cs        # 群聊流式事件（SpeakerStarted/Delta/Finished/TurnFinished）
│   ├── IChatHistoryService.cs   # 会话与消息持久化接口
│   ├── IRoleService.cs          # 角色 CRUD 接口
│   ├── IConfigurationService.cs # 设置读写接口
│   ├── IMemoryService.cs        # 长期记忆接口
│   ├── IKnowledgeService.cs     # 知识库管理接口
│   ├── IVectorStore.cs          # 向量存储抽象 + IEmbeddingService
│   └── IExtensionServices.cs    # Phase 2 扩展接口（语音/好感度）
└── Settings/
    ├── AiSettings.cs            # AI 配置（API/模型/上下文窗口/记忆/知识库）
    └── GroupChatSettings.cs     # 群聊设置（模式/最大发言人数/互驳开关）
```

**核心模型关系：**

```
Conversation (会话)
├── Type: Private → RoleId 指向单个角色
├── Type: Group   → RoleId = null，成员由 ConversationMember 关联
└── Messages[]    → 每条消息含 RoleId（标识发言人）

ConversationMember (群聊成员)
├── ConversationId → 所属群聊
├── RoleId        → 成员角色
└── DisplayOrder  → 轮询发言顺序
```

### ChatApp.Infrastructure — 基础设施层

```
ChatApp.Infrastructure/
├── Data/
│   ├── AppDbContext.cs          # EF Core 上下文（11 张表 + 关系配置）
│   ├── AppPaths.cs              # 数据路径管理（支持 CHATAPP_DATA_DIR 环境变量）
│   └── PresetRoles.cs           # 6 个预设角色定义
├── Repositories/
│   ├── ChatHistoryService.cs    # IChatHistoryService 实现
│   ├── ConfigurationService.cs  # IConfigurationService 实现（JSON 序列化到 SQLite）
│   └── RoleService.cs           # IRoleService 实现（含级联删除）
├── VectorStore/
│   └── SqliteVectorStore.cs     # 自研 SQLite 向量存储（cosine 相似度搜索）
└── InfrastructureModule.cs      # DI 注册 + 数据库初始化 + 旧库迁移
```

**数据库表结构：**

| 表名 | 用途 |
|------|------|
| `Roles` | AI 角色 |
| `Conversations` | 会话（私聊/群聊） |
| `ConversationMembers` | 群聊成员关系 |
| `Messages` | 聊天消息 |
| `MessageAttachments` | 历史消息的独立图片快照；删除知识图片后仍可显示 |
| `MemoryEntries` | 长期记忆元数据 |
| `KnowledgeDocuments` | 知识文档 |
| `KnowledgeChunks` | 文档分块 |
| `KnowledgeGroups` | 文档分组 |
| `RoleKnowledgeGroups` | 角色与可见知识分组的显式绑定 |
| `Settings` | 键值对配置 |
| `Vectors` | 向量数据（嵌入 BLOB） |

**数据库迁移策略：** `InfrastructureModule.InitializeAsync` 在启动时执行幂等迁移（知识分组、群聊、角色知识绑定、图片知识与消息附件、计划 3 查询索引），支持从旧版本数据库重复升级而不丢失已有文档、消息和向量。

### ChatApp.AI — AI 引擎层

```
ChatApp.AI/
├── AiModule.cs                  # DI 注册（AI 服务单例）
├── Caching/
│   └── ScopedQueryCache.cs      # 会话级召回缓存（TTL + 容量上限 + 按作用域失效）
├── Plugins/
│   ├── DocumentLoader.cs        # 文档加载（txt/md/pdf）
│   └── TextChunker.cs           # 文本分块（按段落 + 重叠窗口）
└── SemanticKernel/
    ├── KernelFactory.cs         # Semantic Kernel 构建器（OpenAI 兼容端点，可传探测超时）
    ├── ApiProbeService.cs       # 设置页 Chat/Embedding 最小请求连接探测
    ├── ChatOrchestrator.cs      # 1:1 聊天编排器
    ├── GroupChatOrchestrator.cs # 群聊编排器（导演模式核心）
    ├── MultimodalClient.cs      # 独立 Chat Completions / Responses 视觉协议适配
    ├── ImageDescriptionService.cs # 图片规范化、识图 JSON 解析与元数据回退
    ├── KnowledgeImageSelection.cs # 内部图片选择指令过滤、校验与限量
    ├── MemoryService.cs         # 长期记忆服务
    ├── KnowledgeService.cs      # 知识库服务
    └── OpenAIEmbeddingService.cs # 嵌入服务封装
```

**1:1 聊天流程（ChatOrchestrator.SendAsync）：**

```
用户输入
  │
  ├─ 1. 持久化用户消息
  ├─ 2. 召回长期记忆（按角色，向量相似度检索）
  ├─ 3. 按角色绑定分组分别检索文本与图片（独立阈值 + TopK）
  ├─ 4. 取短期上下文窗口（最近 N 条消息）
  ├─ 5. 组装 System Prompt（角色人设 + 记忆 + 知识）
  ├─ 6. 构建 ChatHistory → Semantic Kernel 流式调用
  ├─ 7. 校验模型选择的 0–3 张候选图片并过滤内部指令
  └─ 8. 创建图片快照、持久化 AI 回复与附件 → 返回 Message
```

**群聊流程（GroupChatOrchestrator.SendAsync）：**

```
用户输入
  │
  ├─ 1. 持久化用户消息（RoleId=0）
  ├─ 2. 格式化群聊转录（[角色名] 内容格式）
  ├─ 3. 选择发言者：
  │     ├─ Hybrid: 导演 LLM 选择设定人数（不足时按成员顺序补足）
  │     └─ RoundRobin: 所有人按 DisplayOrder
  ├─ 4. 每个发言者依次：
  │     ├─ 召回该角色的 1:1 长期记忆
  │     ├─ 检索当前发言角色绑定的知识分组
  │     ├─ 组装 System Prompt（人设 + 群聊规则段）
  │     ├─ 流式生成 → Report(SpeakerStarted/Delta/Finished)
  │     └─ 持久化回复 → 追加到转录（让后续发言者可见）
  └─ 5. Report(TurnFinished)
```

**Hybrid 导演选角算法：**

1. 导演 System Prompt 列出群内所有成员（名+描述）
2. User Prompt 包含用户消息 + 最近群聊记录
3. 导演返回逗号分隔的角色名 → 解析为 RoleId
4. 导演少选时按 `DisplayOrder` 补足设定人数
5. **容错设计**：解析失败回退 `members.Take(MaxSpeakersPerTurn)`，确保不卡死

### ChatApp.UI — 表现层

```
ChatApp.UI/
├── App.xaml                      # 全局样式 + DataTemplate 映射
├── App.xaml.cs                   # 启动引导（IHost + DI + 初始化）
├── MainWindow.xaml               # 主窗口（导航栏 + 中栏 + 右栏布局）
├── MainWindow.xaml.cs            # 代码后置（空）
├── Program.cs                    # Avalonia 跨平台程序入口
├── Platforms/macOS/Info.plist    # macOS 应用包元数据
├── Converters/
│   └── Converters.cs             # Avalonia 值转换器
├── ViewModels/
│   ├── ViewModelBase.cs          # ObservableObject 基类
│   ├── INavigation.cs            # 导航抽象接口
│   ├── MainViewModel.cs          # 主窗口 VM（导航 + 页面切换）
│   ├── ChatViewModel.cs          # 聊天 VM（私聊/群聊 + 流式气泡）
│   ├── ChatBubbleViewModel.cs    # 单条消息气泡 VM
│   ├── ConversationItemViewModel.cs  # 最近群聊条目 VM
│   ├── RoleListViewModel.cs      # 角色列表 VM
│   ├── KnowledgeViewModel.cs     # 知识库管理 VM
│   ├── SettingsViewModel.cs      # 设置页 VM
│   ├── CreateRoleViewModel.cs    # 创建角色 VM
│   ├── CreateGroupChatViewModel.cs   # 创建群聊 VM
│   ├── GroupNode.cs              # 知识库分组节点
│   └── SelectableDocument.cs     # 可选中文档包装
└── Views/
    ├── ChatView.xaml/.cs         # 聊天界面
    ├── RoleListView.xaml/.cs     # 角色库
    ├── KnowledgeView.xaml/.cs    # 知识库管理
    ├── SettingsView.xaml/.cs     # 设置页
    ├── CreateRoleWindow.xaml/.cs # 创建角色弹窗
    ├── CreateGroupChatWindow.xaml/.cs  # 创建群聊弹窗
    ├── InputDialog.xaml/.cs      # 文本输入对话框
    └── SelectionDialog.xaml/.cs  # 选项对话框
```

**UI 布局（仿微信三栏式）：**

```
┌──────┬────────────────┬──────────────────────────┐
│ 导航  │  中栏 (340px)   │    右栏 (剩余空间)          │
│ 栏   │                │                          │
│(64px)│ 角色列表/       │  聊天界面 / 知识库 / 设置   │
│      │ 最近群聊        │                          │
│ 🤖   │                │                          │
│ 📚   │                │                          │
│ ⚙️   │                │                          │
│      │                │                          │
└──────┴────────────────┴──────────────────────────┘
```

**MVVM 数据流：**

```
View (XAML) ←──DataBinding──→ ViewModel ←──Interface──→ Service (AI/Infrastructure)
    │                              │
    └──Command (RelayCommand)──────┘
```

**群聊气泡渲染机制：**

群聊使用 `GroupChatEvent` 事件流驱动 UI：
1. `SpeakerStarted(roleId)` → 创建新气泡（带角色头像/名，IsStreaming=true）
2. `SpeakerDelta(roleId, delta)` → 追加文本到对应气泡
3. `SpeakerFinished(roleId, msg)` → 完成流式，锁定气泡内容
4. `TurnFinished` → 本轮结束，重置发送状态

---

## 数据库设计

### ER 图（核心实体关系）

```
Roles (1) ──────< Messages (M) ──────> (1) Conversations
  │                                        │
  │  RoleId                                │  ConversationId
  │                                        │
  ├── MemoryEntries (M)                    ├── ConversationMembers (M)
  │     RoleId                                │  RoleId → Roles
  │                                           │
  └── Conversations (M)                       └── DisplayOrder
        RoleId (nullable)

KnowledgeDocuments (M) ──< KnowledgeChunks (M)
  │
  └── GroupId → KnowledgeGroups (1)
```

### 关键索引

- `Messages(ConversationId)` — 按会话查询消息
- `Messages(ConversationId, Id)` — 消息窗口游标分页（`IX_Messages_ConversationId_Id`）
- `Conversations(IsPinned, UpdatedAt)` — 置顶优先 + 时间排序的会话列表（`IX_Conversations_IsPinned_UpdatedAt`）
- `Conversations(RoleId)` — 按角色查会话
- `ConversationMembers(ConversationId)` + `ConversationMembers(RoleId)` — 群聊查询
- `MemoryEntries(RoleId)` — 按角色查记忆
- `KnowledgeDocuments(GroupId)` — 按分组查文档
- `KnowledgeChunks(DocumentId)` — 按文档查分块
- `KnowledgeGroups(Name)` UNIQUE — 分组名唯一
- `Vectors(Scope)` — 向量范围查询

---

## 配置说明

### AI 设置（AiSettings）

设置以 JSON 形式存储在 SQLite `Settings` 表中（Key = "ai"）：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `ApiBaseUrl` | `https://api.deepseek.com/v1` | 远程 HTTPS、OpenAI 兼容 API 端点 |
| `ApiKey` | (空) | API 密钥 |
| `ChatModel` | `deepseek-v4-flash` | 聊天模型名 |
| `UseUnifiedApi` | `false` | 统一 API 模式：聊天/Embedding/视觉共用主端点与主 Key |
| `EmbeddingModel` | (空) | 可选的远程嵌入模型名 |
| `VisionProviderPreset` | `AlibabaModelStudio` | 独立多模态服务预设 |
| `VisionProtocol` | `ChatCompletions` | `ChatCompletions` 或 `Responses` |
| `VisionApiBaseUrl` | 阿里云百炼兼容地址 | 仅用于导入/重新识图的 HTTPS 地址 |
| `VisionApiKey` | (空) | 独立多模态密钥，不写入日志 |
| `VisionModel` | `qwen3-vl-flash` | 可编辑视觉模型或 Endpoint ID |
| `VisionTimeoutSeconds` | 90 | 单张识图超时秒数 |
| `VisionMaxConcurrency` | 3 | 视觉请求最大并发，限制为 1–3 |
| `ContextWindowSize` | 20 | 短期上下文窗口消息数 |
| `MemoryTopK` | 5 | 每轮召回的记忆片段数 |
| `KnowledgeTopK` | 5 | 每轮召回的知识片段数 |
| `KnowledgeMinScore` | 0.35 | 知识命中的最低余弦相似度 |
| `KnowledgeImageTopK` | 5 | 每轮独立召回的图片候选数 |
| `KnowledgeImageMinScore` | 0.35 | 图片命中的最低余弦相似度 |
| `KnowledgeContextCharBudget` | 6000 | 每轮最多注入的知识字符数 |
| `KnowledgeNeighborRadius` | 1 | 命中分块前后补充的相邻块数量 |
| `ChatTemperature` | 0.65 | 私聊与群聊角色回复的生成温度 |
| `MemoryBatchSize` | 50 | 触发记忆抽取的消息阈值 |
| `CharsPerToken` | 4.0 | Token 估算比例 |

### 群聊设置（GroupChatSettings，嵌套在 AiSettings 内）

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `Mode` | `Hybrid` (1) | 发言模式：RoundRobin=0, Hybrid=1, FreeForAll=2 |
| `MaxSpeakersPerTurn` | 2 | 每轮实际发言人数（Hybrid 模式，成员足够时） |
| `RespondToOtherAgents` | true | 是否允许 AI 互相回应/反驳 |

### 数据目录

- 默认路径：`%LOCALAPPDATA%\ChatApp\`
- 数据库文件：`chatapp.db`
- 知识文件存储：`knowledge` 子目录
- 知识原图存储：`knowledge/images` 子目录
- 历史附件快照：`message-attachments` 子目录
- 滚动日志：`logs` 子目录（按日 `chatapp-YYYY-MM-DD.log`，保留 7 天）
- 可通过环境变量 `CHATAPP_DATA_DIR` 重定向

---

## 开发指南

### 环境要求

- .NET 8 SDK
- Windows 可使用 Visual Studio 2022；macOS 推荐 JetBrains Rider，也可使用 VS Code + C# 扩展
- macOS 12+ 或 Windows 10/11

### 构建与运行

```bash
# 还原依赖
dotnet restore

# 编译
dotnet build

# 运行
dotnet run --project ChatApp.UI
```

### 项目命名规范

- **命名空间**：`ChatApp.{Layer}.{Sub}`（如 `ChatApp.Core.Models`）
- **接口**：`I` 前缀（如 `IChatService`）
- **ViewModel 命令**：`RelayCommand` 特性 + `[RelayCommand]`
- **ViewModel 属性**：`ObservableProperty` 特性 + `[ObservableProperty]`
- **异步方法**：`Async` 后缀

### 添加新功能的指南

1. **新模型** → `ChatApp.Core/Models/`
2. **新服务接口** → `ChatApp.Core/Services/`
3. **数据访问实现** → `ChatApp.Infrastructure/Repositories/`
4. **AI 逻辑实现** → `ChatApp.AI/SemanticKernel/`
5. **DI 注册** → 各层的 Module 类
6. **UI 实现** → `ChatApp.UI/ViewModels/` + `ChatApp.UI/Views/`
7. **VM-View 映射** → `App.xaml` 的 `DataTemplate`

### DI 注册清单

**InfrastructureModule**:
- `IDbContextFactory<AppDbContext>` (Singleton)
- `IVectorStore` → `SqliteVectorStore`
- `IRoleService` → `RoleService`
- `IChatHistoryService` → `ChatHistoryService`
- `IConfigurationService` → `ConfigurationService`

**AiModule**:
- `IMultimodalClient` → `MultimodalClient`（独立 `HttpClient`）
- `IImageDescriptionService` → `ImageDescriptionService`
- `IEmbeddingService` → `OpenAIEmbeddingService`
- `IChatService` → `ChatOrchestrator`
- `IGroupChatService` → `GroupChatOrchestrator`
- `IMemoryService` → `MemoryService`
- `IKnowledgeService` → `KnowledgeService`
- `IApiProbeService` → `ApiProbeService`

**UI (App.xaml.cs)**:
- `MainViewModel` (Singleton, also as `INavigation`)
- `ChatViewModel`, `RoleListViewModel`
- `KnowledgeViewModel`, `SettingsViewModel`
- `CreateRoleViewModel`, `CreateGroupChatViewModel`
- `MainWindow`

---

## 关键设计决策

### 为什么手写 Director 而不使用 SK AgentGroupChat？

1. **每 Agent 独立记忆注入**：每个发言角色需要注入自己的长期记忆，SK 的 AgentGroupChat 不支持 per-agent 记忆
2. **按发言者流式气泡**：UI 需要区分每个发言者的流式输出进行独立气泡渲染
3. **可控的发言顺序**：需要精确控制 Hybrid 模式的导演选角 + 顺序发言

### 为什么自研向量存储而不用外部向量数据库？

- 桌面应用场景，向量量级 < 10K
- 免去外部服务依赖，简化部署
- SQLite BLOB + cosine 相似度对于此规模足够快（< 1s）

### Conversation.RoleId 为什么是可空类型？

- 私聊：`RoleId` 指向对应角色
- 群聊：`RoleId = null`，成员由 `ConversationMember` 关联
- 避免把某个群聊成员 id 填入 RoleId 导致的级联删除误伤

---

## 备份与恢复

- SQLite 数据库定时备份为 `chatapp.db.bak-{timestamp}`
- 数据目录可通过环境变量 `CHATAPP_DATA_DIR` 自定义（便携模式）

---

## 许可

内部项目，未设定开源许可。

---

> 文档更新日期：2026-08-21 | 项目版本：v1.3.6
