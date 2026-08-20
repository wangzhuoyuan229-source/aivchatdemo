# AGENTS.md

本文件适用于整个仓库。开始修改前先阅读本文件，并按任务需要查阅 `README.md`、相关项目文件和测试。若子目录以后出现更具体的 `AGENTS.md`，以更深层文件为准。

## 项目概览

ChatApp 是基于 .NET 8 和 Avalonia 的跨平台 AI 角色扮演桌面应用，使用 Semantic Kernel 调用远程 OpenAI 兼容 API，并以 EF Core、SQLite 和本地向量存储持久化角色、聊天、记忆及知识库。

解决方案分层如下：

- `ChatApp.Core`：领域模型、设置和服务接口；不得依赖其他本地项目。
- `ChatApp.Infrastructure`：EF Core、SQLite、仓储和向量存储；只依赖 Core。
- `ChatApp.AI`：聊天编排、RAG、记忆、Embedding 和多模态适配；依赖 Core 与 Infrastructure。
- `ChatApp.UI`：Avalonia 视图、ViewModel、应用启动和 DI 组合根。
- `ChatApp.Tests`：xUnit 单元、契约、迁移和持久化测试。
- `training`：Apple Silicon 上的离线 MLX/LoRA 研究流程，不属于桌面应用运行时。
- `知识库`：随发布包分发并在用户本机首次建立索引的内置资料。

保持依赖方向和职责边界。领域契约放在 Core，实现放在相应基础设施或 AI 层，UI 逻辑优先放在 ViewModel，而不是代码后置文件。

## 常用命令

仓库通过 `global.json` 锁定 .NET SDK 8.0.424（允许同一特性带的更新补丁）。

```bash
dotnet restore ChatApp.sln
dotnet build ChatApp.sln --configuration Debug --nologo
dotnet test ChatApp.sln --configuration Debug --nologo
dotnet run --project ChatApp.UI/ChatApp.UI.csproj
```

发布命令：

```bash
./publish-macos.sh
```

Windows PowerShell：

```powershell
.\publish-win-x64.ps1
```

发布输出位于 `publish/`。构建输出、发布包、训练数据、模型、adapter 和本地配置均不应提交。

## 实现约定

- 使用已启用的 nullable reference types 和 implicit usings；新代码保持空值语义明确。
- 遵循现有 C# 风格：4 空格缩进、文件作用域命名空间、类型/公开成员使用 PascalCase、局部变量和私有字段使用 camelCase。
- 接口以 `I` 开头，异步方法以 `Async` 结尾，并尽可能向下传递 `CancellationToken`。
- ViewModel 使用 CommunityToolkit.Mvvm 的 `[ObservableProperty]` 和 `[RelayCommand]`；绑定仍需与现有 Avalonia XAML 约定兼容。
- 新服务必须在所属层的模块中注册；UI 专属服务和 ViewModel 在 `ChatApp.UI/App.xaml.cs` 注册。
- 不要无故升级 NuGet 包、改变目标框架、批量重排代码或重写无关文件。
- 用户可能已有未提交改动；只修改任务涉及的文件，不覆盖或清理无关变更。

## 关键产品约束

### 远程 API 与密钥

- 桌面应用只允许远程 HTTPS 托管 API。不得重新开放 HTTP、localhost、回环地址、私有网络地址、Ollama 或其他本地模型入口。
- 聊天、Embedding 和视觉服务可以使用独立端点和密钥；修改设置解析时必须保留这种隔离。
- API Key 只能保存在用户本机数据目录中，不得硬编码、写入仓库、发布包、异常文本或日志。
- 保持 UI、持久化和运行时三层的端点校验。相关改动至少覆盖 `RemoteApiEndpointPolicy`、设置迁移或 `KernelFactory` 的契约测试。

### RAG、记忆与图片

- 知识检索必须尊重角色到知识分组的显式绑定；不得让未绑定或其他角色的资料隐式可见。
- 严格知识边界是产品行为：没有可靠命中时不能把模型猜测伪装成资料事实。
- 文本知识与图片知识有独立的 TopK、阈值和描述流程。聊天阶段复用已保存的图片描述，不应再次调用视觉 API。
- 模型用于选择图片的内部指令不得显示给用户；最终附件需经过候选校验、数量限制并保存独立快照。
- 私聊记忆按角色隔离。群聊中每个发言者也只能召回自己的记忆和已绑定知识。

### 数据库与本地数据

- 默认用户数据位于 macOS 的 `~/Library/Application Support/ChatApp/` 或 Windows 的 `%LOCALAPPDATA%\ChatApp`，也可由 `CHATAPP_DATA_DIR` 重定向。开发和测试不得删除、提交或覆盖真实用户数据。
- `InfrastructureModule.InitializeAsync` 兼容旧数据库。所有 schema 变更都必须保留已有数据、可重复执行，并同时覆盖新库与旧库升级路径。
- SQLite 结构变更应补充持久化/迁移测试，至少验证幂等性和关键数据关系。
- `Conversation.RoleId` 在私聊中指向角色，在群聊中必须为 `null`；群聊成员使用 `ConversationMember` 表示。
- 删除知识文档后，既有消息的附件快照仍应可显示；不要将历史消息直接依赖于当前知识文件。

### 内置知识库

- 根目录 `知识库/` 是版本化发布内容，不是临时数据。不要把它加入 `.gitignore`。
- 新增路径会被自动发现；若在相同相对路径上修改或替换文件内容，必须同步递增 `ChatApp.UI/Services/BundledKnowledgeService.cs` 中的 `BundleVersion`，让旧索引重建。
- 修改发布逻辑后需确认完整的 `BundledKnowledge/` 被复制。发布时分发整个目录或 `.app`，不能只分发可执行文件。
- 不要提交 `.DS_Store`、本机数据库、生成的向量索引或用户导入资料。

### 离线训练

- `training/` 只用于研究、数据准备、LoRA 训练和固定评测；不得把本地模型或 adapter 接回桌面应用。
- 原始/处理数据、基础模型、训练环境、adapter 和生成报告均受 `.gitignore` 约束。
- 修改训练脚本或配置时遵循 `training/README.md`，避免声称自动规则分数可以替代人工自然度评估。

## 测试与验证

- 默认在交付前运行 `dotnet test ChatApp.sln --configuration Debug --nologo`。纯文档改动可不运行测试，但要检查 diff 和链接/命令准确性。
- 修复缺陷时优先先添加可复现失败的测试，再实施修复。新增行为必须在 `ChatApp.Tests` 中覆盖正常路径和关键失败/回退路径。
- 测试不得依赖真实 API Key、外部服务、用户数据库或执行顺序；数据库测试使用独立临时路径并在 `finally` 中清理。
- AI 协议和提示词改动要维护相应契约测试，尤其是端点策略、严格 grounding、群聊选角、视觉响应解析和知识图片选择。
- UI 改动至少确保解决方案可构建；涉及绑定、命令或转换器时检查运行时绑定名与 ViewModel 成员一致。
- 发布相关改动除测试外，还应在对应操作系统验证发布脚本、内置知识文件存在性以及生成包结构。

## 提交前检查

1. 查看 `git diff --check`，确保没有空白错误或意外生成文件。
2. 查看 `git status --short`，确认改动范围只包含任务所需文件。
3. 运行与改动风险匹配的构建和测试，并如实报告未运行的检查。
4. 若行为、设置、数据结构、发布方式或用户操作发生变化，同步更新 `README.md`、`CHANGELOG.md` 或相关说明。
