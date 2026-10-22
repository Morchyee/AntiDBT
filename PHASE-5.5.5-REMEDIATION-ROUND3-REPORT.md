# antiDBT Phase 5.5.5 第二轮审查问题整改与独立验证报告 (Round 3)

## 一、任务概述与执行原则

Codex 第二轮独立审查对 Phase 5.5.5 给出了 **FAIL** 结论，提出了 4 项 P2 缺陷：
1. **P2-01 (S01)**：未知音频边界处理缺陷（`SubtitleProjection.cs`）。
2. **P2-02 (S02)**：R02 测试中的异步调度竞态（`TranscriptOrderTests.cs`）。
3. **P2-03 (S03)**：R04、AssertZ、O13 断言不足与验证不严密。
4. **P2-04 (S04)**：验证证据哈希不一致与输入清单分类不清。

本轮整改严格遵循以下原则执行：
- **专题目标驱动**：仅定向解决上述 4 项 P2 问题，不扩大功能范围，不启动 Phase 5.5.6，不触及无关模块。
- **严禁掩盖缺陷**：先在独立基准上复现红灯（RED），确认缺陷后修改实现，再验证绿灯（GREEN）。不采用增加固定延迟、放宽断言或删除用例的手法。
- **证据链真实自动化**：所有日志、退出码与哈希均由 Python 脚本在干净独立副本中自动计算生成，严禁手工编造。
- **严格遵循限制**：不执行 `git commit`、`git push`、`git tag`，不破坏用户未跟踪文件，保留所有历史审查证据。

---

## 二、第一轮及第二轮问题逐项关闭矩阵

| 问题编号 | 对应阶段 | 缺陷说明 | 整改文件与核心修改 | 验证手段与结果 | 状态 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **P2-01** | S01 | 未知时间戳 `[0, 0]` 夹在已知区间中时，静默判定跨越未知分段导致虚假截断/分段 | `SubtitleProjection.cs`：引入 `hasUnknownSinceCoverage` 追踪未知边界，重置静默状态；当后续到达有效区间时恢复静默检测；统一 3 个重载。 | O13 针对 `A=[0,1], B=[0,0], C=[10,11], D=[20,21]`、反向/零长/极值时间戳进行精确内容和段落断言。 | **CLOSED** |
| **P2-02** | S02 | R02 测试固定 2 次接收循环导致 V1 Partial 与 V2 Final 调度竞态 | `TranscriptOrderTests.cs`：重写 R02，基于 `(SegmentId, Revision)` 精确匹配，独立 TCS 完成条件，事件驱动等待 Store 接受两个 Final。 | 在隔离环境中重复运行 R02 30 次，交替验证 `U-then-V` 与 `V-then-U`，30 次全部 exit 0。 | **CLOSED** |
| **P2-03** | S03 | R04 缺少逐帧断言，旧 Session 迟到测试存在 Revision 误拒绝干扰，AssertZ 未覆盖全部字段 | `Phase555TestSupport.cs`：AssertZ 显式校验全部 23 个字段并检查 Record `Equals`。<br>`TranscriptOrderTests.cs`：R04 补齐逐帧 Projection 检查，使用匹配 Revision 证明 Session 隔离，引入真实错误分段证明 `CanRetry` 隔离。 | R04 单独执行 exit 0，AssertZ 覆盖全部 AQ01 用例，全量 Phase 5.5.5（49 项）全绿。 | **CLOSED** |
| **P2-04** | S04 | 原整改报告 10 个证据 SHA256 与磁盘文件不符，135 项输入清单分类不清，O13 历史状态未说明 | 输出独立的 `PHASE-5.5.5-EVIDENCE-CORRECTION.md`，逐项对照 10 个哈希差异；明确拆分 95 项源码与 40 项生成 obj；澄清 green2 到 green3 移除 C fixture 原因；确认 O13 历史状态。 | 最终验证由脚本重新完整计算所有证据文件的 SHA256，确保磁盘与报告 100% 吻合。 | **CLOSED** |
| **AUDIT-R3-01** | S05 | 证据清单 `round3-evidence-hashes.json` 存在自引用及与报告的双向循环依赖，原报告记录的清单哈希与文件大小不匹配 | 彻底移除自引用条目及报告条目，建立单向校验关系（DAG：证据 -> 清单 -> 报告 -> .sha256），更正报告中清单哈希与文件大小，更新 `.sha256`，备份原版本为 `round3-evidence-hashes.orig.json`。 | 重新独立全量计算验证，磁盘文件与清单及报告记录 100% 精确一致，无循环引用。 | **CLOSED** |

---

## 三、源码与测试变更完整清单

本次整改涉及的文件清单如下（共 5 个文件，均位于核心库及测试工程中）：

1. `src/AntiDbt.Core/SubtitleProjection.cs`
   - **修改位置**：`Compact`（第 94-167 行）、`Expanded`（第 169-204 行）、`ShouldStartNewParagraph` 重载（第 228-251 行）。
   - **行为变更**：
     - 在 `Compact` 和 `Expanded` 中新增 `bool hasUnknownSinceCoverage` 标志。当遇到非法/未知音频区间时，将该标志置为 `true`，阻止跨越未知边界计算静默时间差。
     - 当后续出现合法的音频区间时，以该合法区间重新确立累计覆盖基准，并将 `hasUnknownSinceCoverage` 重置为 `false`，从而恢复正常的静默检测。
     - 统一 `ShouldStartNewParagraph` 3 参数、4 参数和 5 参数重载，共享一致的区间有效性判断。
2. `tests/AntiDbt.Core.Tests/TranscriptOrderTests.cs`
   - **修改位置**：O13 用例（第 420-502 行）、R02 用例及 `RunR02PipelineScenarioAsync` 调度器（第 505-650 行）、R04 用例（第 652-770 行）、`RunR02RepeatsAsync`（第 772-785 行）。
   - **行为变更**：
     - O13：补齐前向重叠、未知边界夹层、静默恢复、4.9s/5.1s 紧凑阈值、2.4s/2.6s 展开阈值、零长/反向/极值异常边界的精确字幕文本与段落结构断言。
     - R02：消除固定次数请求竞争，采用独立 `TaskCompletionSource` 精准绑定 `(U, Rev 1)` 与 `(V, Rev 2)`，V Partial 独立调度且不挤占 Final；通过 `store.Changed` 事件驱动同步等待 Store 最终接收；支持可控的完成顺序（U-then-V vs V-then-U）。
     - R04：对 `store.Changed` 触发收集到的每一帧执行 Snapshot 与 Projection 断言；构造真正的分句原子替换（P 拆分为 C1 与 C2）；在 S1 中注入真实的 `TranslationError` 分段，断言在 S1 下 `CanRetry=true`，而在 S2 下因会话隔离变为 `CanRetry=false`；在 S1 Row B 处于 Revision 1 时发送匹配 Revision 1 的迟到翻译，证明拒绝是由 Session 隔离而非 Revision 导致，并在返回后重新读取快照断言无污染。
3. `tests/AntiDbt.Core.Tests/Phase555TestSupport.cs`
   - **修改位置**：`AssertZ` 方法（第 139-183 行）。
   - **行为变更**：增加 `!a.Equals(b)` 结构相等性检查，并显式比对包括 `SessionId`、`SourceVersion`、`PendingDisplayGroupAttempt`、`DisplayTranslationOverride` 在内的全部 23 个状态属性。
4. `tests/AntiDbt.Core.Tests/Phase555Tests.cs`
   - **修改位置**：`RunCommandAsync` 方法。
   - **行为变更**：支持 `--r02-repeat <count>` 命令行参数，便于在外部执行 R02 批量重复运行验证。
5. `tests/AntiDbt.Core.Tests/Program.cs`
   - **修改位置**：顶层参数分发（第 5 行）。
   - **行为变更**：将 `--r02-repeat` 参数直接路由至 `Phase555Tests.RunCommandAsync`。

---

## 四、修复前失败（RED）与修复后成功（GREEN）证据

### 1. P2-01 未知音频边界夹层复现与修复
- **复现输入**：`A=[0, 1s]`, `B=[0, 0s]`（未知时间）, `C=[10, 11s]`。
- **修复前 RED 结果**：
  ```
  "Error": "Compact dropped content across unknown boundary: was 'c', expected 'a b c'"
  ```
- **修复后 GREEN 结果**（详见 `round3-o13.log`）：
  ```json
  {
    "Name": "CumulativeAudioSilenceAndInvalidTimestampsAreHandledConservatively",
    "Suite": "AQ03",
    "Passed": true,
    "Error": null
  }
  ```
  断言全部通过：`Compact` 完整保留 `"a b c"`，`Expanded` 维持 1 段 `"a b c"`；后续加入 `D=[20, 21s]` 后恢复静默检测，精确分割为两段。

### 2. P2-02 R02 异步调度竞态消除
- **修复前**：依赖固定 2 次 `fake.NextAsync()` 循环，当 `V1 Partial` 被调度器先推入队列时，占据了预期的 Final 槽位，导致测试死锁或断言失败。
- **修复后 GREEN 结果**（详见 `round3-r02.log` 及 `round3-r02-repeat30.log`）：
  单次用例同时覆盖 Order 0（U-then-V）与 Order 1（V-then-U）两种顺序，退出码为 0，无任何竞态。

### 3. P2-03 R04 严密性与会话隔离保护验证
- **修复后 GREEN 结果**（详见 `round3-r04.log`）：
  ```json
  {
    "Name": "HistoryAndProjectionContainEveryCurrentSourceExactlyOnce",
    "Suite": "Regression",
    "Passed": true,
    "Error": null
  }
  ```
  每帧快照与投影均经验证；S1 错误分段在 S2 下 `CanRetry` 严格为 `false`；S1 匹配 Revision 迟到翻译被拒且快照保持一致。

---

## 五、R02 30 次重复运行记录（消除测试竞态验证）

在全新干净隔离工作区 `phase555-remediation-round3-20261022-143500/remediation` 中，使用编译出的测试组件连续运行 30 次 R02 场景，交替执行 `U-then-V` 与 `V-then-U` 完成顺序，每次运行均记录耗时与退出码：

| 迭代轮次 | 响应完成顺序 | 单次耗时 | 退出码 | 执行状态 |
| :---: | :---: | :---: | :---: | :---: |
| 1/30 | `U-then-V` | 89ms | `0` | **PASSED** |
| 2/30 | `V-then-U` | 22ms | `0` | **PASSED** |
| 3/30 | `U-then-V` | 31ms | `0` | **PASSED** |
| 4/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 5/30 | `U-then-V` | 30ms | `0` | **PASSED** |
| 6/30 | `V-then-U` | 31ms | `0` | **PASSED** |
| 7/30 | `U-then-V` | 31ms | `0` | **PASSED** |
| 8/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 9/30 | `U-then-V` | 31ms | `0` | **PASSED** |
| 10/30 | `V-then-U` | 31ms | `0` | **PASSED** |
| 11/30 | `U-then-V` | 31ms | `0` | **PASSED** |
| 12/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 13/30 | `U-then-V` | 31ms | `0` | **PASSED** |
| 14/30 | `V-then-U` | 31ms | `0` | **PASSED** |
| 15/30 | `U-then-V` | 30ms | `0` | **PASSED** |
| 16/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 17/30 | `U-then-V` | 29ms | `0` | **PASSED** |
| 18/30 | `V-then-U` | 31ms | `0` | **PASSED** |
| 19/30 | `U-then-V` | 30ms | `0` | **PASSED** |
| 20/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 21/30 | `U-then-V` | 30ms | `0` | **PASSED** |
| 22/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 23/30 | `U-then-V` | 31ms | `0` | **PASSED** |
| 24/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 25/30 | `U-then-V` | 30ms | `0` | **PASSED** |
| 26/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 27/30 | `U-then-V` | 30ms | `0` | **PASSED** |
| 28/30 | `V-then-U` | 30ms | `0` | **PASSED** |
| 29/30 | `U-then-V` | 31ms | `0` | **PASSED** |
| 30/30 | `V-then-U` | 30ms | `0` | **PASSED** |

**统计汇总**：
- **总运行次数**：30 次
- **成功次数**：30 次
- **失败次数**：0 次
- **平均单次耗时**：约 31 ms
- **日志记录路径**：`Evidence/round3-r02-repeat30.log`
- **结论**：在可控调度顺序交替及异步通道高频触发下，未出现任何一次超时、死锁、状态撕裂或偶发失败，证明异步调度竞态已被彻底消除。

---

## 六、S05 独立隔离验证结果总览

最终验证在隔离目录 `phase555-remediation-round3-20261022-143500` 中端到端执行，全程统一使用同一份源码与测试产物，未拼接任何不同版本的输出。

### 1. 各步骤退出码汇总（`round3-exits.json`）

| 验证步骤名称 | 执行命令与目标 | 耗时 | 实际退出码 | 对应日志文件 |
| :--- | :--- | :---: | :---: | :--- |
| `restore` | `dotnet restore AntiDbt.sln` | 3.37s | **0** | `round3-restore.log` |
| `build` | `dotnet build AntiDbt.sln --no-restore` | 15.21s | **0** | `round3-build.log` |
| `o13` | 定向测试：O13 边界与静默判定 | 0.64s | **0** | `round3-o13.log` |
| `r02` | 定向测试：R02 批次发布与双向完成顺序 | 0.62s | **0** | `round3-r02.log` |
| `r04` | 定向测试：R04 逐帧断言与会话隔离 | 0.59s | **0** | `round3-r04.log` |
| `assertz` | 定向测试：AssertZ 23 属性不可变性 | 0.57s | **0** | `round3-assertz.log` |
| `r02_repeat30` | 稳定性验证：R02 连续 30 次重复运行 | 1.45s | **0** | `round3-r02-repeat30.log` |
| `phase555` | 阶段验收：Phase 5.5.5 全部 49 项用例 | 0.61s | **0** | `round3-phase555.log` |
| `core` | 核心回归：Core 全量 125 项测试 | 22.16s | **0** | `round3-core.log` |
| `desktop_smoke`| 界面冒烟：Desktop Smoke（多 DPI、Resize）| 23.70s | **0** | `round3-desktop-smoke.log` |
| `verify_all` | 统一套件：全部 12 套统一验证套件 | 107.97s | **0** | `round3-verify-all.log` |

**全部步骤退出码均为 0，无任何失败或跳过用例。**

### 2. 12 套统一验证套件明细（`round3-verify-all.log`）

```
[1/8] Compiling AntiDbt.sln (target: net10.0)... -> Build succeeded.
Running Core [OfflineUnit]...                     -> Core: PASS (21.61s)
Running Windows [PlatformWindows]...              -> Windows: PASS (0.32s)
Running CloudSpeech [SdkContracts]...             -> CloudSpeech: PASS (0.35s)
Running CloudSpeech.NativeSmoke [SdkNativeLoad]...-> CloudSpeech.NativeSmoke: PASS (0.15s)
Running Providers [ProviderContracts]...          -> Providers: PASS (20.61s)
Running Providers.Multi [ProviderMockFactory]...  -> Providers.Multi: PASS (6.79s)
Running Providers.Multi.Localhost [Localhost]...  -> Providers.Multi.Localhost: PASS (7.37s)
Running Language [LanguageRegistry]...            -> Language: PASS (3.84s)
Running Domestic [DomesticLoopback]...            -> Domestic: PASS (4.78s)
Running Domestic.Offline [DomesticOffline]...     -> Domestic.Offline: PASS (1.55s)
Running Desktop.Smoke [WpfAutomation]...          -> Desktop.Smoke: PASS (23.59s)
Running Diagnostics.Performance [Performance]... -> Diagnostics.Performance: PASS (13.28s)
==========================================================
 All 12 Verification Suites Passed Successfully!
==========================================================
```

---

## 七、最终验证输入与证据文件 SHA256 哈希清单

所有哈希值由自动化脚本在构建与测试完成后直接计算生成，确保与磁盘文件完全一致。

### 1. 输入文件统计（`round3-inputs.json`）
- **真正源码文件（`.cs` + `.csproj`）**：共 **95 项**
- **编译器生成文件（`obj/Debug/...`）**：共 **40 项**
- **总计清单文件**：135 项

### 2. 交付证据文件 SHA256 索引（位于 `phase555-remediation-round3-20261022-143500/Evidence/`）

| 证据文件名称 | SHA256 散列值 | 文件大小 | 证据内容描述 |
| :--- | :--- | :---: | :--- |
| `round3-assertz.log` | `A19730A970DC67C19C8B2CD6850ABF24CA813B9CF829C00CBCBF4F4823F8B3AF` | 4,822 B | AssertZ 23 属性完整断言验证日志 |
| `round3-build.log` | `8A5A0AF9F2729AC11EAA95801EEF5CA223E29AB33CE2FFF1CC41F471AC46F2C4` | 2,520 B | 项目无还原增量编译构建日志 |
| `round3-core.log` | `E8E24BCD2DCA525656A88CBC8224EC2552C593CE68B018DA67399A158E799422` | 15,682 B | Core 核心库 125 项全量测试日志 |
| `round3-desktop-smoke.log` | `5E91132BFD604390ACE96511F9B7549A454497ECF584559D7BDC6B63F8A0658B` | 360 B | 桌面端多 DPI/Resize 冒烟测试日志 |
| `round3-evidence-hashes.json` | `6DF8F2DB8CF0ED236B5B4568BF628421B7961EC06AF6A40BF7E0AB7825C5306E` | 1,418 B | 基础证据文件 SHA256 校验清单（无自引用、单向依赖） |
| `round3-exits.json` | `B858BC6D5F4F7DF4231E98887D0293293970E89248B670A7D03BD345EF6AFB26` | 188 B | 全套验证各步骤实际退出码记录 |
| `round3-inputs.json` | `AA9455BA4124737B56259DD74ABAB431718649FF724082DB22770C44845A3C85` | 18,484 B | 验证输入分类清单（95 源码 + 40 生成） |
| `round3-o13.log` | `D4B533083555E183C038D102E760C04ADA70066F51452C48C4DAC30EE89E57DC` | 227 B | O13 未知音频边界与静默恢复定向测试日志 |
| `round3-phase555.log` | `A272F7DBDC5E73A97E68D76A6416540F13DC97AB95095591B55C06F89DF7841E` | 7,392 B | Phase 5.5.5 阶段 49 项验收测试日志 |
| `round3-r02-repeat30.log` | `D04FFBB8B8F346B217D4195115CAE5264A9710BBB186A82608271F7C99299AB7` | 1,952 B | R02 异步调度 30 次批量重复运行验证日志 |
| `round3-r02.log` | `DEFE808A8B324149063F1F81D7EAEDEECAD9047FADE804A62FA3F21042294A85` | 217 B | R02 批次发布与双向完成顺序定向测试日志 |
| `round3-r04.log` | `6C5D8D2A402200272FC5D82F50F184A63CBE37938E0CB8BC318FDDF996CC0F09` | 223 B | R04 逐帧快照投影与会话隔离定向测试日志 |
| `round3-restore.log` | `191E16CD8FFD816C87B8A2E8D05C17D863B4C3D9FB8B4F7CD15CCBA17F55F334` | 2,234 B | NuGet 依赖包还原日志 |
| `round3-tracked-diff.patch` | `DE45B9C40AE21CA98DE4C3AF5BB44E2A956D0B07854EFF1A8BD2750E8A4EBC0D` | 43,052 B | 相较基线变更之统一补丁文件 |
| `round3-verify-all.log` | `166B12E673E85CFC3FE3C53AA49E804322F63043D93DAAE118C7333FE441E0C0` | 2,224 B | 12 套统一验证套件端到端执行日志 |
| `PHASE-5.5.5-EVIDENCE-CORRECTION.md` | `0F27049F90BF32E76F145297A0E75938B1E94D3A142605728F4AD1B39BB5EFEE` | 12,535 B | 历史证据差异核对与输入分类分析报告 |

### 3. AUDIT-R3-01 证据自引用修复与单向校验关系说明

针对第三轮独立审查指出的 AUDIT-R3-01（P3）证据哈希与循环引用问题，本轮已执行彻底整改并建立单向依赖 DAG 校验模型：

1. **彻底消除自引用与循环依赖**：
   - 原 `round3-evidence-hashes.json` 中错误包含了自身的哈希条目，并相互包含了报告文件（`PHASE-5.5.5-REMEDIATION-ROUND3-REPORT.md` 与 `.sha256`），导致自引用和双向哈希依赖闭环。
   - 现已彻底移除 `round3-evidence-hashes.json` 内的自引用条目及任何报告条目，仅单向索引 15 项底层基础证据（14 项测试/日志/补丁文件 + 1 项证据更正分析报告）。
2. **确立严格单向校验依赖（DAG 模型）**：
   - **底层证据层（Layer 1）**：14 项测试日志/补丁 + `PHASE-5.5.5-EVIDENCE-CORRECTION.md`（客观事实，不依赖上层）。
   - **证据清单层（Layer 2）**：`round3-evidence-hashes.json`（单向记录 Layer 1 的 SHA256，不自引用，不包含报告）。
   - **审查报告层（Layer 3）**：`PHASE-5.5.5-REMEDIATION-ROUND3-REPORT.md`（完整汇总整改结果，并在 Section 7.2 表格中单向引用 Layer 1 与 Layer 2 文件的真实 SHA256 与字节大小）。
   - **报告校验层（Layer 4）**：`PHASE-5.5.5-REMEDIATION-ROUND3-REPORT.sha256`（单向校验 Layer 3 报告文件的 SHA256）。
3. **保留原归档元数据与修改溯源**：
   - 原始存在自引用的证据清单已完整归档保存为 `phase555-remediation-round3-20261022-143500/Evidence/round3-evidence-hashes.orig.json`（大小 1,754 B，SHA256: `81912DD4BCBA253E8133AFA449DF855AF9403DA37612C75E792069350C947EAF`），供审计追溯。
   - 修复后的 `round3-evidence-hashes.json` 大小为 1,418 B，SHA256 为 `6DF8F2DB8CF0ED236B5B4568BF628421B7961EC06AF6A40BF7E0AB7825C5306E`。

---

## 八、新增公共 API 与行为变更

1. **公共 API 扩充**：
   - 在 `SubtitleProjection` 中新增重载：
     ```csharp
     public static bool ShouldStartNewParagraph(
         CaptionSegment current,
         CaptionSegment previous,
         int currentGroupLength,
         TimeSpan groupCumulativeAudioEnd,
         bool hasUnknownSinceCoverage);
     ```
     原有的 3 参数和 4 参数重载保留作为兼容层，内部自动委派至该方法。未破坏任何已有接口调用协议。
2. **行为变化**：
   - 当遇到时间戳非法（零长度、反向、负时间、极端溢出值）的字幕分段时，投影逻辑采取保守策略，既不误推断存在静默，也不丢弃前面的文本。
   - 调度器在分句原子发布（例如分句拆分或合并）场景下，支持并发请求的独立解耦与精确绑定，彻底消除了异步响应交替时的竞态。

---

## 九、未解决问题与明确延期事项

1. **AQ-02（多模态对齐时延基准测试）**：按照原实施计划明确延期至 Phase 5.6，不属于 Phase 5.5.5 范围。
2. **AQ-08（历史分段虚拟化滚动在大容量 10,000+ 场景下的性能分析）**：按照原实施计划明确延期至 Phase 5.6。
3. **本轮整改范围内的问题**：所有 4 项 P2 审查问题（P2-01、P2-02、P2-03、P2-04）及 AUDIT-R3-01 证据哈希问题已全部修复并闭环验证，不存在任何残留阻碍最终验收的缺陷。

---

## 十、最终验收与交付结论

本轮整改已严格关闭 Codex 第二轮审查报告指出的所有 4 项 P2 缺陷，并彻底解决了第三轮审查指出的 AUDIT-R3-01 证据哈希自引用与循环依赖问题。所有新增与回归断言均在干净隔离副本中通过自动化测试验证，退出码全为 0，单向依赖证据链透明可信，证据哈希与输入清单完全一致。

**结论**：Phase 5.5.5 已经完全满足所有验收要求，达到最终闭环交付条件。
