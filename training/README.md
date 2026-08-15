# 离线对话模型研究

本目录提供可复现的 Apple Silicon MLX LoRA 流程。最终候选是
`Qwen2.5-1.5B-Instruct + strict-v1 1000 步`：训练只负责知识边界、拒绝编造和简短
互动，事实本身继续由应用 RAG 提供。模型、数据和权重都被 `.gitignore` 排除。

## 最终模型：准备、训练与评测

```bash
./training/scripts/bootstrap.sh
./training/scripts/download_model.sh
./training/scripts/prepare_strict_data.sh
./training/scripts/train_strict_lora.sh
./training/scripts/select_adapter.sh
./training/scripts/evaluate.sh
```

默认参数针对 16GB Apple Silicon：BF16 LoRA、16 层、rank 8、batch 1、梯度累积
4、学习率 `5e-6`。训练会跑 1200 步并每 200 步存档；`select_adapter.sh` 默认选中
固定评测较好的 1000 步检查点。可通过 `SOURCE_ADAPTER_FILE` 改选其他检查点。

严格数据包含 4000/400/400 条 train/valid/test 样本，覆盖已知事实、资料缺失、
空资料角色私事、资料/用户提示注入、直接冲突、多轮指代和普通闲聊。生成器会检查
固定 50 条评测问题没有进入训练集。

## 经典对话数据实验

经典数据仍保留为研究对照，但首轮实验表明，在 1.5B 模型和有限步数下直接混合会
稀释严格知识边界信号，因此它们不进入最终选中 adapter。

| 数据集 | 研究用途 | 条件 |
|---|---|---|
| CPED | 中文角色、情绪与口吻 | 仓库 Apache-2.0；影视内容再分发需另审 |
| NaturalConv | 长对话与话题转换 | 腾讯非商业研究许可 |
| LCCC-base | 中文短对话多样性 | 原项目声明仅限科研 |
| KdConv | 有依据的知识对话 | Apache-2.0 |

接受 NaturalConv 非商业许可后，可复现混合实验：

```bash
./training/scripts/download_data.sh
./training/scripts/prepare_data.sh
./training/scripts/train_lora.sh
```

多样化校准实验使用 `prepare_diverse_data.sh`、`train_strict_v2_lora.sh` 和
`calibrate_final_lora.sh`；其规则准确率低于 strict-v1，因此未发布为默认模型。

## 与桌面应用的边界

此目录只用于离线训练、生成和固定评测。桌面应用不再接受 MLX、Ollama、localhost、
私有网络或其他本地模型端点，训练得到的权重不会成为应用可选模型。产品运行时默认
通过远程 DeepSeek API 使用 `deepseek-v4-flash`；如需 RAG，需另行配置一个远程
HTTPS Embedding API。

## 数据、产物和报告

```text
training/
  configs/       MLX LoRA 配置
  scripts/       下载、清洗、训练、选模、评测和服务脚本
  data/          原始与处理数据（不提交）
  cache/         基础模型（不提交）
  artifacts/     LoRA adapter（不提交）
  reports/       固定实验报告；generated/ 为逐条输出（不提交）
```

本次实际训练结果见 [pilot-2026-08-15.md](reports/pilot-2026-08-15.md)。自动规则评分
用于回归，不替代人工自然度盲评。
