# 更新日志

项目遵循语义化版本号。版本日期按 Asia/Shanghai 记录。

## [Unreleased]

### 新增

- 新建角色启用版本化的固定角色扮演启动指令：只使用新建菜单内容确定性填空，在首次 AI 问候前作为不可见系统上下文执行；旧角色继续使用原提示行为。
- 角色表新增 `PromptTemplateVersion`，旧数据库幂等增加该列并将既有角色保留为版本 `0`，避免升级后静默改写角色设定。
- 最近群聊卡片新增删除操作；确认后删除群聊消息、附件、成员关系及由该群聊产生的长期记忆，但保留成员角色。
- 长期记忆改为全角色共享召回；每条记忆保留来源角色，管理窗口统一展示来源并支持新增、编辑、删除和清空。
- 外观相关话题自动增强角色绑定知识中的外观文字和图片召回；外观细节被明确约束为客观设定，资料未覆盖时不得自行补造，非必要不主动描写。

### 修复

- 修复聊天输入框 `Enter` 快速发送回归并兼容中文输入法：读取 Avalonia IME 的真实预编辑状态，候选存在时将回车完整交给输入法；仅在非预编辑状态下撤销 `TextBox` 插入的换行并发送，`Shift+Enter` 保持换行，群聊 `@` 候选不会抢占中文候选确认。
- 默认 SiliconFlow 对话模型更新为 `deepseek-ai/DeepSeek-V4-Flash`；DeepSeek 官方端点使用 `deepseek-v4-flash`，旧自定义模型不会被升级逻辑覆盖。
- 角色库将角色与最近群聊拆分为独立切换分块，各自占满可用高度并独立滚动，避免群聊过多时把角色卡片挤出视野。
- SiliconFlow `DeepSeek-V4-Flash` 的角色回复固定使用 Think High，保留推理质量并避免进入更慢的 Think Max；共享记忆与知识检索改为并行执行，缩短私聊和群聊的回复前等待。

### 稳定性与安全

- 切换或清空会话时取消仍在进行的旧请求，避免迟到结果继续更新已离开的对话。
- API Key 与 Bearer Token 即使尚未注册，也会按凭证格式自动脱敏；设置加载时提前注册三路 API 密钥。
- 用户可见错误统一分类并脱敏，覆盖聊天、设置、角色、知识库、记忆管理、启动与多模态响应错误。
- 配置 JSON 损坏时安全回退到当前可用默认设置，不访问或覆盖真实用户数据。

### 质量保障

- 新增固定提示词、旧库迁移、共享记忆召回与来源标注、快捷发送策略、提供商模型映射、自定义模型保留、损坏配置回退及敏感信息脱敏测试。

## [1.3.7] - 2026-08-22

本版本发布当前 Unreleased 中的角色创建、群聊管理、共享记忆、输入法兼容、模型性能、知识库外观召回与稳定性改进。

## [1.3.6] - 2026-08-21

### 新增

- 群聊头像支持创建时选择本地图片；未设置时自动使用前四名成员头像生成 `2×1` 或 `2×2` 拼图，旧群聊自动回退为成员拼图。
- 聊天中的用户头像统一使用内置用户头像资源。

### 改进

- 最近群聊卡片不再显示更新时间；数据库新增可空语义的 `Conversations.Avatar` 字段，并提供旧库幂等迁移。
- 全局 UI 更新为 Apple 桌面应用风格：使用系统蓝和 grouped background 灰阶、SF/PingFang 优先字体、连续圆角、轻材质顶栏、即时按压反馈，并统一浅色/深色语义资源；角色列表选中继续使用中性灰而非蓝底。

## [1.3.5] - 2026-08-21

### 安全

- 升级 Semantic Kernel 至 `1.71.0`、EF Core/SQLite 至 `8.0.30`，并显式使用新版 SQLitePCLRaw 原生 bundle，消除已知 Critical/High 依赖漏洞。
- 启用 NuGet 全量依赖审计，`NU1901`–`NU1904` 在 restore 阶段按错误处理；CI 使用锁定依赖恢复并输出漏洞报告。

### 改进

- 引入 `Directory.Packages.props` 集中管理 NuGet 版本，并为各项目生成 `packages.lock.json`。
- RID 发布使用 `obj/` 下的平台专用锁文件，避免 self-contained 发布改写常规构建锁文件。
- Embedding 适配迁移到 `Microsoft.Extensions.AI.IEmbeddingGenerator`，消除 Semantic Kernel 升级后的弃用警告。
- 设置页隐藏尚未实现的语音、好感度与 FreeForAll 入口；旧配置中的 FreeForAll 自动回退到 Hybrid。
- 增加版本一致性与未完成功能可见性测试，统一 `v1.3.5` 开发版本标识。

### 质量保障

- `dotnet build` 0 警告/0 错误，120 项测试全部通过；NuGet 审计无已知漏洞。
- macOS arm64 完整发布脚本通过，Windows x64 自包含发布输出验证通过，均包含 1792 个内置知识文件。

## [1.3.4] - 2026-08-21

### 新增

- 帮助与支持新增 B站（UID:451598529）、小红书（7439240082）、邮箱（1037561013@qq.com）并附“欢迎私信”提示：`DeveloperSocials.All` 扩展 3 项，`MainWindow` 弹窗底部新增常驻文案，支持 `IUrlLauncher` 打开与 `ClipboardService` 复制。

### 改进

- 应用图标全面更新：新增 `ChatApp.UI/Assets/icon.png`（512px，`Window.Icon="/Assets/icon.png"`）、`icon.ico`（WinExe `ApplicationIcon`，6 尺寸含 PNG 压缩）与 `AppIcon.icns`（macOS `CFBundleIconFile=AppIcon`，`Contents/Resources/AppIcon.icns`），`publish-macos.sh` 新增图标搬运与校验，`ChatApp.UI.csproj` 版本升至 `1.3.4/1.3.4.0`，`Info.plist` 同步为 `1.3.4/6`，侧边栏版本标识 `v1.3.4`，`README` Current/Release 链接与帮助表述同步更新。
- 占位图标为临时渐变 AI 图，后续替换 `Assets/icon.png` 后需按 `icon.ico`（256/128/64/48/32/16）与 `AppIcon.icns`（1024..16 @2x）重新生成（`PIL`/`iconutil` 流程已在仓库保留），或直接提供新源图由维护者重新导出。

### 质量保障

- `dotnet build` 0 警告/0 错误，`dotnet test` 保持 117 项通过；`MainWindow` 弹窗新文案不影响现有契约，图标资源经 `AvaloniaResource`/`Content` 双通道校验，`publish-macos.sh` 仍通过 `BundledKnowledge` 与图标存在性检查。

## [1.3.3] - 2026-08-21

### 新增

- 消息撤回：用户 2 分钟内可撤回最后一条本人消息，撤回后原文回填草稿栏并聚焦（`ChatBubbleViewModel.CanRecall` + `ChatViewModel.RecallMessageAsync` + `IChatHistoryService.DeleteMessageAsync`），超期置灰。
- 群聊 @ 成员：输入框键入 `@` 自动弹出成员列表（`ChatViewModel.FilteredMentionCandidates` + `Popup`），支持过滤、上下选择、回车/点击插入 `@Name `，发送时导演优先让被 @ 成员发言（`GroupChatOrchestrator` 提及优先）。

### 改进

- 新建角色极简化：首屏仅保留头像、名称、用户扮演角色、知识分组、补充设定 5 项，`CreateRoleWindow` 高度 680，旧字段（简介/背景/性格/说话风格/示范对话/开场问候）收进 `Expander 补充设定（高级）` 默认折叠，`CreateRoleViewModel.SupplementaryPrompt` 复用 `Role.SystemPrompt` 兼容老角色。
- 聊天气泡隐藏时间：`ChatView.xaml` 气泡时间 `TextBlock IsVisible=False` 仅保留 `ToolTip`，`Message.CreatedAt` 仍用于排序与导出，侧边栏时间不动。

### 质量保障

- `dotnet build` 0 警告/0 错误，`dotnet test` 117 项通过（含撤回/提及/新建简化契约），`ChatView` 虚拟化与 `@` 弹窗轻量。

## [1.3.2] - 2026-08-21

### 新增

- 帮助与支持常驻侧边栏：左侧导航栏新增 `💬` 入口，`MainWindow` 全窗 `Panel.ZIndex=10` 模态弹窗展示 `DeveloperSocials`（GitHub / 邮箱 / 反馈），`MainViewModel.IsDeveloperHelpOpen` + `IUrlLauncher.TryOpenAsync` 外链与 `ClipboardService` 回退，`SettingsView` 移除底部滚动内分组以修复截断。
- 统一 API 预设：`UnifiedApiPresets`（SiliconFlow 推荐 `deepseek-ai/DeepSeek-V3.1 + BAAI/bge-m3 + Qwen3-VL` / 阿里百炼 `qwen-plus + text-embedding-v4 + qwen3-vl-flash` / OpenAI `gpt-4o-mini`），`AiSettings.UnifiedPreset` 持久化，`SettingsView` 预设下拉在统一模式下自动填充三模型。

### 改进

- 统一API默认切换至 SiliconFlow：`RemoteApiEndpointPolicy.DefaultBaseUrl` → `https://api.siliconflow.cn/v1`，`AiSettings` 默认 `deepseek-ai/DeepSeek-V3.1` + `BAAI/bge-m3` + `SiliconFlow/Qwen3-VL-32B-Instruct`，`UseUnifiedApi=true`（`ConfigurationService` 对旧库缺 `useUnifiedApi` 保持 `false` 兼容），`SettingsViewModel` 预设与 `DetectProvider` 同步（`BAAI/bge-m3` + 提示文案）。
- 统一模式下模型选择锁定：`AreIndividualModelsEnabled = !UseUnifiedApi || UnifiedPreset==Custom`，`ChatModel/EmbeddingModel/VisionModel` 在预设非自定义时 `IsEnabled=false`，`VisionModel` 对 `qwen3-vl-flash` 等旧 ID 自动迁移至 `Qwen/Qwen3-VL-32B-Instruct`（`AiSettings.MigrateToRemoteApiOnly` + `SettingsViewModel` 硅基检测）。
- 版本标识同步：`ChatApp.UI.csproj` `1.3.2/1.3.2.0`，`Info.plist` `1.3.2/4`，`MainWindow` `v1.3.2`，`README` 默认服务更新为 SiliconFlow 三件套，修复设置页滚动容器 `Grid RowDefinitions="*"` + `VerticalAlignment=Stretch` 截断。

### 质量保障

- `dotnet build` 0 警告/0 错误，`dotnet test` 117 项通过（含统一预设、视觉迁移与旧库幂等新契约），`publish-macos.sh` `BundledKnowledge` 拷贝校验仍通过。

## [1.3.1] - 2026-08-21

### 改进

- 修复知识库/设置页左侧 340px 空白占位：`MainWindow` 中间栏改为 `Auto` + `IsVisible="{Binding MiddleView}"` 折叠，`RightView` 占满剩余宽度；左下角版本标识升至 `v1.3.1`。
- 知识库“分组 / 文件夹”支持折叠：`GroupNode` 新增 `IsExpanded/HasChildren/IsVisible`，`KnowledgeFolderTree` 按 `FolderPath` 前缀判定父子，`KnowledgeView` 左栏新增“全部展开/全部折叠”与每行 `▾/▸` 按钮，子文件夹随祖先联动隐藏，折叠状态在刷新/导入后保留。
- 删除“外观与阅读”设置：移除设置页深色主题切换与聊天气泡字号调节，启动时不再读取 `UiSettings` 并固定 Light 主题与 14 号字，相关 `ThemeMode/ChatFontSize` 文案与校验逻辑一并清理。
- 设置页为 34 项设置添加“?”通俗帮助：每项标签旁 `16×16 圆形 ?`，`ToolTip Placement=Right/ShowDelay=0` 悬停即显、移开隐藏，同时 `Flyout` 点击可常驻；修复初版 `Border Top/200ms` 悬停不显示问题，文案面向非专业用户解释作用与可选值（如统一 API、Embedding 预设、协议、Top-K、温度等）。
- 聊天输入框支持回车发送：`ChatView` 的 `InputBox_KeyDown` 兼容 `Key.Enter/Return`，`Enter` 发送、`Shift+Enter` 换行，增加 `Watermark="输入消息，回车发送 · Shift+回车换行"` 提示。

### 质量保障

- `dotnet build` 0 警告/0 错误，`dotnet test` 117 项通过，发布脚本 `BundledKnowledge` 拷贝校验仍通过。

## [1.3.0] - 2026-08-20

### 新增

- 记忆管理窗口：私聊按角色、群聊按当前发言者查看/新增/编辑/删除/清空记忆条目；编辑后立即重嵌入向量，下次召回即反映变化。
- 消息气泡操作：一键复制（含附件引用信息）、重新生成最后一条 AI 回复（替换旧回复并重新计算上下文）、编辑已发送消息后重发（发送后替换该消息及其后的回复）。
- 会话整理：私聊/群聊支持重命名与置顶（置顶优先于更新时间排序，重启后保持）；一键导出为 Markdown（含角色名、时间、附件快照）或结构化 JSON（含引用文档 Id）。
- 知识引用溯源：AI 回复下方展示所引用的知识文档标签（点击跳转知识库对应文档），未命中时不显示标签。
- 长对话摘要压缩：上下文接近窗口上限时用 LLM 生成“摘要 + 关键记忆点”替换早期消息并缓存，保留最近完整消息；摘要生成失败自动回退原有截断逻辑。开关与保留条数可在设置页配置。
- 深色主题与阅读设置：跟随系统或手动切换深浅主题（全局即时生效并持久化），聊天气泡字号可调（12–22）。
- 数据库新增 `Conversations.IsPinned` 与 `Messages.CitedDocumentIds` 两列，旧库幂等迁移，历史数据与附件快照完整保留。

### 改进

- 全部视图（角色列表、知识库、设置、群聊、创建窗口、对话框）改用主题画刷（DynamicResource），深色模式无对比度问题。
- 长对话上下文构建抽离为可测试的摘要/截断管线，与记忆抽取相互独立。
- 聊天消息列表改用虚拟化面板（ScrollViewer + VirtualizingStackPanel），长对话切换与滚动不再一次性创建全部消息元素。
- 启动加载并行化：角色、知识库、设置三路 `LoadAsync` 并行执行，冷启动时间缩短。
- 会话级召回缓存：记忆与知识检索结果按角色/会话作用域缓存（60 秒 TTL + 容量上限），同会话重复相似提问不再重复调用 Embedding 与向量检索；记忆编辑、删除、清空时按角色即时失效。
- 消息列表分页加载：默认每次取 120 条，滚动到顶部可"加载更早消息"（游标分页，配合新增查询索引）。
- 本地日志文件：按日滚动写入 `~/Library/Application Support/ChatApp/logs/chatapp-YYYY-MM-DD.log`（Windows 为 `%LOCALAPPDATA%`，随 `CHATAPP_DATA_DIR` 重定向），保留 7 天自动清理；所有日志行写盘前经密钥打码，API Key 不会落到日志。

### 新增

- 设置页新增"测试聊天连接"与"测试 Embedding 连接"按钮：发送最小请求验证端点、模型与密钥，失败原因分级展示且绝不回显 API Key。

### 质量保障

- 新增 12 项测试：引用解析去重、导出 Markdown/JSON 结构、文件名清洗、置顶排序、重命名、尾部消息截断、引用列持久化与旧库迁移幂等、记忆编辑重嵌入。
- 新增 16 项测试：召回缓存 TTL/作用域隔离/容量淘汰、日志密钥打码/按日滚动/过期清理、计划 3 索引（新库与旧库幂等）、`beforeId` 游标分页、连接探测缺 Key/缺模型守卫路径与错误信息不含密钥。
- 当前测试套件共 117 项测试全部通过。

## [1.2.0] - 2026-08-20

### 新增

- 设置页新增“统一 API 模式”开关：聊天、Embedding 与多模态识图可共用同一远程 HTTPS 端点和 API Key（推荐 SiliconFlow 等聚合平台），只需分别为各功能填写模型 ID。
- 保留三路独立 API 接入能力，两种模式可随时切换；切换到统一模式时，已填写的独立端点与密钥会被保留，切回独立模式后自动恢复。
- 运行时端点解析统一收口：统一与独立模式均沿用 `RemoteApiEndpointPolicy` 的 HTTPS/禁本地校验，设置解析契约测试覆盖两种模式与回退路径。

### 改进

- Embedding 与多模态运行时缓存签名按有效端点/密钥计算，避免统一模式下复用旧客户端。
- 统一模式下若主端点为 DeepSeek 官方（不提供 Embedding/视觉），设置页会给出明确提示。
- README 补充统一 API 模式说明与 SiliconFlow 配置示例。

### 质量保障

- 新增统一 API 设置契约测试 14 项：模式解析、端点/密钥复用与回退、运行时端点校验、旧设置 JSON 兼容（缺省 `useUnifiedApi` 默认独立模式）、迁移归一化与 ViewModel 持久化。
- 当前测试套件共 89 项测试全部通过。

## [1.1.0] - 2026-08-16

### 新增

- 知识库支持 PNG、JPEG、WebP 图片，图片描述、标签和向量独立保存，并可在私聊和群聊中按角色知识分组检索。
- 新增阿里云百炼、智谱、火山方舟和 SiliconFlow 多模态服务预设，同时支持自定义 Chat Completions / Responses 服务。
- 图片导入支持多层文件夹递归批处理，保留完整相对目录结构；支持批量重新识图、编辑语义和重新索引。
- 聊天回复可按需附带最多 3 张知识图片，使用独立快照保存历史附件。
- 内置根目录 `知识库/`，首次安装自动索引，后续启动复用本机向量并支持断点续传。
- 创建角色时可填写“你扮演谁”，该身份会用于私聊和群聊中的称呼与关系理解。
- 自动匹配角色头像时，使用独立多模态模型定位主要人物面部并放大裁剪为 256×256 头像；识别失败时自动回退。
- 旧数据库增加幂等迁移，保留已有角色、知识、消息和向量数据。

### 改进

- 知识库批量导入采用批量向量写入和并发限制，减少大目录导入耗时并允许保留已完成项目。
- 头像候选优先匹配角色名称所在的图片目录和文件名，再使用语义检索兜底。
- README 补充内置知识库、图片知识、多模态服务、发布和升级说明。

### 质量保障

- 自动化测试覆盖多模态协议、重试与响应解析、图片规范化、人脸区域裁剪、目录导入、头像匹配、附件快照和数据库迁移。
- 当前测试套件共 73 项测试全部通过。

## [1.0.2] - 2026-08-15

- 严格 RAG、角色示范对话、远程 API-only、Embedding 预设、群聊编排和 macOS 发布包。
