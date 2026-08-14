# AI 角色扮演聊天应用 (ChatApp)

> .NET 8 + Avalonia 跨平台桌面应用 | Semantic Kernel AI 引擎 | EF Core + SQLite 持久化

## 项目概述

ChatApp 是一款支持 macOS 与 Windows 的桌面 AI 角色扮演聊天应用，支持 1:1 私聊与多 AI 群聊。用户可以创建/管理 AI 角色（人设、性格、说话风格），导入知识库文档，并通过 OpenAI 兼容 API 驱动角色进行对话。应用采用 BYOK（自带密钥）模式，支持任何兼容 OpenAI 接口的服务。

## macOS Release 使用说明

当前正式版为 [v1.0.1](https://github.com/wangzhuoyuan229-source/aivchatdemo/releases/tag/v1.0.1)，适用于 Apple Silicon（M1/M2/M3/M4 等 arm64）Mac，要求 macOS 12 或更高版本。安装包已包含 .NET 运行时，普通用户不需要另外安装 .NET SDK。

### 下载与安装

1. 下载 [ChatApp-macOS-arm64.zip](https://github.com/wangzhuoyuan229-source/aivchatdemo/releases/download/v1.0.1/ChatApp-macOS-arm64.zip)。
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

v1.0.1 的 SHA-256 应为：

```text
66eebe3b064ac95200d9ea7843322eb7916f4bc3752c7256fef9b8aa45cd9608
```

### 首次配置

1. 启动 ChatApp，打开左侧“设置”。
2. 填写兼容 OpenAI 协议的 API Base URL 和自己的 API Key。
3. 选择或填写聊天模型与 Embedding 模型。
4. 点击“保存设置”，再进入角色库开始对话。

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

生成的单文件程序位于 `publish/win-x64/ChatApp.UI.exe`。API Key 不写入程序或发布目录，而是由用户在应用设置中输入并保存到本机 `%LOCALAPPDATA%\\ChatApp`；发布前不要把本地数据库或配置文件复制进发布包。

## 技术栈

| 层级 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET 8 | 8.0 |
| UI 框架 | Avalonia | 11.3.20 |
| MVVM 框架 | CommunityToolkit.Mvvm | 8.2.2 |
| AI 引擎 | Microsoft Semantic Kernel | 1.21.1 |
| 数据库 | EF Core + SQLite (Microsoft.Data.Sqlite) | 8.0.11 |
| DI 容器 | Microsoft.Extensions.Hosting | 8.0.1 |
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
| 🤖 **AI 角色管理** | 创建/编辑/删除角色，支持头像、人设、背景、性格、说话风格 |
| 📦 **预设角色库** | 6 个内置角色（林溪、诸葛亮、福尔摩斯、Emma、苏念、李白） |
| 💬 **1:1 私聊** | 单角色对话，流式输出，支持上下文窗口管理 |
| 👥 **AI 群聊** | 多角色同台对话，支持混合导演(Hybrid)与轮询(RoundRobin)两种模式 |
| 📚 **知识库** | 导入 txt/md/pdf 文档，自动分块+向量化，对话时检索注入 |
| 🧠 **长期记忆** | 按角色隔离的长期记忆，自动批量抽取+向量召回 |
| ⚙️ **BYOK 设置** | 自定义 API 端点/密钥/模型，支持 OpenAI 兼容服务 |
| 🔍 **消息搜索** | 全文关键词搜索历史消息 |
| 📁 **知识库分组** | 文档分组管理、批量移动/删除 |

### Phase 2 规划

- FreeForAll 群聊模式（Agent 自评 + 导演评分制）
- 每角色专属知识库
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
│   ├── Role.cs                  # AI 角色（人设、性格、说话风格、问候语）
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
│   ├── AppDbContext.cs          # EF Core 上下文（9 张表 + 关系配置）
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
| `MemoryEntries` | 长期记忆元数据 |
| `KnowledgeDocuments` | 知识文档 |
| `KnowledgeChunks` | 文档分块 |
| `KnowledgeGroups` | 文档分组 |
| `Settings` | 键值对配置 |
| `Vectors` | 向量数据（嵌入 BLOB） |

**数据库迁移策略：** `InfrastructureModule.InitializeAsync` 在启动时执行幂等迁移（`MigrateKnowledgeGroupsAsync` + `MigrateGroupChatAsync`），支持从旧版本数据库升级而不丢失数据。

### ChatApp.AI — AI 引擎层

```
ChatApp.AI/
├── AiModule.cs                  # DI 注册（5 个 AI 服务单例）
├── Plugins/
│   ├── DocumentLoader.cs        # 文档加载（txt/md/pdf）
│   └── TextChunker.cs           # 文本分块（按段落 + 重叠窗口）
└── SemanticKernel/
    ├── KernelFactory.cs         # Semantic Kernel 构建器（OpenAI 兼容端点）
    ├── ChatOrchestrator.cs      # 1:1 聊天编排器
    ├── GroupChatOrchestrator.cs # 群聊编排器（导演模式核心）
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
  ├─ 3. 检索知识库（向量相似度 TopK）
  ├─ 4. 取短期上下文窗口（最近 N 条消息）
  ├─ 5. 组装 System Prompt（角色人设 + 记忆 + 知识）
  ├─ 6. 构建 ChatHistory → Semantic Kernel 流式调用
  └─ 7. 持久化 AI 回复 → 返回 Message
```

**群聊流程（GroupChatOrchestrator.SendAsync）：**

```
用户输入
  │
  ├─ 1. 持久化用户消息（RoleId=0）
  ├─ 2. 格式化群聊转录（[角色名] 内容格式）
  ├─ 3. 选择发言者：
  │     ├─ Hybrid: 导演 LLM 选 1~N 人（低温度 0.2）
  │     └─ RoundRobin: 所有人按 DisplayOrder
  ├─ 4. 每个发言者依次：
  │     ├─ 召回该角色的 1:1 长期记忆
  │     ├─ 检索全局知识库
  │     ├─ 组装 System Prompt（人设 + 群聊规则段）
  │     ├─ 流式生成 → Report(SpeakerStarted/Delta/Finished)
  │     └─ 持久化回复 → 追加到转录（让后续发言者可见）
  └─ 5. Report(TurnFinished)
```

**Hybrid 导演选角算法：**

1. 导演 System Prompt 列出群内所有成员（名+描述）
2. User Prompt 包含用户消息 + 最近群聊记录
3. 导演返回逗号分隔的角色名 → 解析为 RoleId
4. **容错设计**：解析失败回退 `members.Take(MaxSpeakersPerTurn)`，确保不卡死

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
│   ├── ConversationListViewModel.cs  # 会话列表 VM
│   ├── RoleListViewModel.cs      # 角色列表 VM
│   ├── KnowledgeViewModel.cs     # 知识库管理 VM
│   ├── SettingsViewModel.cs      # 设置页 VM
│   ├── CreateRoleViewModel.cs    # 创建角色 VM
│   ├── CreateGroupChatViewModel.cs   # 创建群聊 VM
│   ├── GroupNode.cs              # 知识库分组节点
│   └── SelectableDocument.cs     # 可选中文档包装
└── Views/
    ├── ChatView.xaml/.cs         # 聊天界面
    ├── ConversationListView.xaml/.cs  # 会话列表
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
│ 导航  │  中栏 (300px)   │    右栏 (剩余空间)          │
│ 栏   │                │                          │
│(64px)│ 角色列表/       │  聊天界面 / 知识库 / 设置   │
│      │ 会话列表/       │                          │
│ 🤖   │ 最近群聊        │                          │
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
| `ApiBaseUrl` | `https://api.openai.com/v1` | OpenAI 兼容 API 端点 |
| `ApiKey` | (空) | API 密钥 |
| `ChatModel` | `gpt-4o-mini` | 聊天模型名 |
| `EmbeddingModel` | `text-embedding-3-small` | 嵌入模型名 |
| `ContextWindowSize` | 20 | 短期上下文窗口消息数 |
| `MemoryTopK` | 5 | 每轮召回的记忆片段数 |
| `KnowledgeTopK` | 5 | 每轮召回的知识片段数 |
| `MemoryBatchSize` | 50 | 触发记忆抽取的消息阈值 |
| `CharsPerToken` | 4.0 | Token 估算比例 |

### 群聊设置（GroupChatSettings，嵌套在 AiSettings 内）

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `Mode` | `Hybrid` (1) | 发言模式：RoundRobin=0, Hybrid=1, FreeForAll=2 |
| `MaxSpeakersPerTurn` | 2 | 每轮最多发言人数（Hybrid 模式） |
| `RespondToOtherAgents` | true | 是否允许 AI 互相回应/反驳 |

### 数据目录

- 默认路径：`%LOCALAPPDATA%\ChatApp\`
- 数据库文件：`chatapp.db`
- 知识文件存储：`knowledge` 子目录
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
- `IEmbeddingService` → `OpenAIEmbeddingService`
- `IChatService` → `ChatOrchestrator`
- `IGroupChatService` → `GroupChatOrchestrator`
- `IMemoryService` → `MemoryService`
- `IKnowledgeService` → `KnowledgeService`

**UI (App.xaml.cs)**:
- `MainViewModel` (Singleton, also as `INavigation`)
- `ChatViewModel`, `RoleListViewModel`, `ConversationListViewModel`
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

> 文档生成日期：2026-08-11 | 项目版本：v1.0
