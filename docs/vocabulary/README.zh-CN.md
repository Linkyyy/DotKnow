# TypeLingo 中英日本地词库（zh / en / ja）

## 1. 这是什么
供 TypeLingo 在**本地精确查询**的三语词库。程序先按
`sourceLanguage + targetLanguage + sourceText + style` 精确命中本词库，
未命中时再走应用自身的在线翻译。**本包不含翻译模型，不联网即可查询。**

- 语言：简体中文 `zh`、英语 `en`、日语 `ja`
- 风格：全部为 `natural`（自然常用表达），未生成敬语/商务等其他风格版本
- 六个方向：`zh→en`、`en→zh`、`zh→ja`、`ja→zh`、`en→ja`、`ja→en`

## 2. 文件清单
| 文件 | 说明 |
|---|---|
| `manifest.json` | 包元信息、方向、格式、实时统计 |
| `vocabulary.jsonl` | 原始三语词条，每行一个 JSON 对象 |
| `entries.jsonl` | 可直接查询、已去重且无冲突的六向记录 |
| `ambiguous.jsonl` | 歧义/冲突/待审核记录，**不进入自动命中** |
| `validation-report.json` | 真实统计与校验结果 |
| `licenses/` | 许可证与来源说明 |
| `checksums.sha256` | 包内文件 SHA256 |

## 3. 数据格式
### vocabulary.jsonl
```json
{"id":"TL000001","zh":"面包","en":"bread","ja":"パン","partOfSpeech":"noun","category":"饮食","sense":"作为食品的面包"}
```
字段：`id`（唯一编号）、`zh`、`en`、`ja`、`partOfSpeech`、`category`、`sense`（中文词义说明，仅供人工核对，程序不据此消歧）。

### entries.jsonl
```json
{"sourceLanguage":"zh","targetLanguage":"en","sourceText":"面包","translatedText":"bread","style":"natural"}
```
查询键：`sourceLanguage + targetLanguage + sourceText + style`。

## 4. 歧义与冲突如何处理
程序只按“原文 + 源语言 + 目标语言 + 风格”精确查询，**不依靠 `sense` 自动判断词义**。
当同一查询键对应多个不同译文时（例如英文 `bank` 的“银行/河岸”、`drink` 的动词/名词），
该查询的所有变体都写入 `ambiguous.jsonl`，**不静默保留最后一条，也不随机选一条**，
并从 `entries.jsonl` 剔除；同一词条不冲突的其他方向仍保留。

## 5. 导入建议
1. 逐行读取 `entries.jsonl`，按四元键建索引（建议哈希表或数据库唯一约束）。
2. 查询时先精确匹配；命中即返回 `translatedText`，未命中再回退在线翻译。
3. `ambiguous.jsonl` 可用于后续人工消歧，不要直接用于自动命中。
4. 全部为 UTF-8、LF 换行；请勿改写语言代码（固定 `zh/en/ja`）。

## 6. 来源与许可
见 `licenses/REFERENCES.md` 与 `licenses/LICENSE.txt`。词条为人工编写汇编，
未原样再分发第三方词典；编写时仅作对照的 CC-CEDICT、OpenJLPT 均为 CC-BY-SA 4.0，
已在 REFERENCES 中署名。

## 7. 本次实际规模与未完成项（如实说明）
- 可用原始词条：**6426** 组（目标 ≥10000，本次未达标）。
- 可查询六向记录：**32159** 条。
- 被隔离的冲突查询：**2441** 组；待审核：**0** 条。
- 各方向数量、分类数量、JSONL 可解析性见 `validation-report.json`。
- 未完成：剩余词条需按第 8 节续做；本包**不冒充 10000 组完整词库**。

## 8. 续做方式（达到 10000 组）
包内 `build/` 目录附带构建脚本、批次模板 `_TEMPLATE.json` 与 `continue.md`。
1. 参照 `build/_TEMPLATE.json` 新建批次文件 `batch_NNN.json`（放入 `build/batches/`），
   每批 200–500 组，逐条人工核对三语同义和词性；不确定的条目标记待审核（第 6 元素 `true`）。
2. 运行 `python3 build_package.py`，脚本自动跨批去重、隔离冲突、重新编号并刷新统计。
3. 运行 `python3 finalize.py` 重新生成 `checksums.sha256` 与 ZIP。
4. 详细步骤见 `build/continue.md`。
