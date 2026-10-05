# 验收-001 · UI 管理器 MVP（面板栈 + MVVM 绑定 + View 池化）

> 依据：`docs/planning/ui-mvp-implementation-plan.md`（S3 **v0.4**）与 `docs/architecture/ui-manager-design.md`（**v0.4**，已批准为实现依据）
> 验证者：AI（独立于作者）　作者：使用者　日期：2026-10-05
> **版本基线**：代码 `9c06286`（文档 `a8fa880`）
> **工作区状态**：干净 ✓（无并发修改，无需隔离手法）
> **测试数据**：无（本阶段未运行任何功能路径）
> **本次范围**：**进门条件核对（S4 §4.3）+ 静态审查（L4）**。
> ⚠️ **本报告不构成"通过"结论** —— 按 `zipper-test` §三，只有 **L1（实测：命令 + 原始输出）** 能支撑"通过"；本次**尚无任何功能层面的 L1 证据** ✗

---

## 1. 跑过的命令与原始输出（强制章节）

### 1.1 编译（L1，但仅证明"能编过"）

```powershell
foreach ($p in @("Zipper.UI.csproj","Zipper.Core.csproj","Zipper.Pool.csproj","Zipper.Resources.csproj","Zipper.Audio.csproj")) {
  dotnet build $p -nologo -v:q 2>&1 | Select-String "错误|error" | Select-Object -First 2
}
```
输出：
```
Zipper.UI.csproj :     0 个错误
Zipper.Core.csproj :     0 个错误
Zipper.Pool.csproj :     0 个错误
Zipper.Resources.csproj :     0 个错误
Zipper.Audio.csproj :     0 个错误
```

### 1.2 改动范围（S4 §4.3 一致性核对）

```powershell
git rev-parse --short HEAD        # 9c06286
git status --porcelain            # 空 → 干净
git diff --stat 44f93ff..HEAD     # S3 获批之后的全部改动
```
输出（摘要）：**41 files changed, 1285 insertions(+), 7 deletions(-)**，提交序列 `58562f6` → `e195650` → `7cd4b8d` → `9c06286`。

### 1.3 options 字段的读取情况（静态审查的取证命令）

```powershell
Get-ChildItem -Recurse -Force -File Assets\Zipper\UI -Include *.cs |
  Select-String -Pattern 'SingleInstance|CloseOnMaskClick|CacheView'
```
输出（**全部命中**）：
```
ZPanelOpenOptions.cs:8: public bool CloseOnMaskClick { get; set; } = false;
ZPanelOpenOptions.cs:9: public bool SingleInstance { get; set; } = false;
ZPanelOpenOptions.cs:10: public bool CacheView { get; set; } = true;
```
→ **三个字段只出现在定义处 ✗ 管理器（及任何其它文件）从未读取它们** ✗

---

## 2. 进门条件核对（S4 §4.3）

| 核对项 | 结果 |
|---|---|
| 「不动」清单（`Core`/`Pool`/`Resources`/`Audio`） | ✅ 实际 diff **一个都没碰** ✓ |
| 改动点清单 vs `git diff --stat` | ✅ **已对齐**（S3 v0.4 逐条回填：41 文件；记录 3 处差异与 3 处实施中新增） |
| 测试 4 项（`Tests.asmdef` / 三个 Tests / `Fakes`） | ⬜ **已声明的延后**（使用者要求"全搞完了再做"）→ 登记在 §3 |
| 未跟踪/未提交文件 | ✅ 无（`git status --porcelain` 为空 ✓） |

**结论**：进门条件**满足** ✓（清单滞后已按规程回填，非越界改动 ✓）

---

## 3. 设计稿 §11 十条验收 · 实现路径核对（**L4 静态，非运行证据**）

| # | 验收项 | 代码里的实现路径 | 静态判定 |
|---|---|---|---|
| 1 | 栈顺序 + `TryHandleBack` 空栈返回 false | `ZPanelManager.TryHandleBack`（`Layer` 降序 + 同层 `Sequence` 降序） | 路径存在 ✓ **待 L1** |
| 2 | `SingleInstance` → 置顶复用、返回同一 handle | **无** ✗ | ❌ **未实现**（P1，见 §4） |
| 3 | 重复 `Close` 幂等 | `ZPanelHandle.IsOpen` 守卫 + `CloseEntry` 的 `_stack.Remove` 守卫 | 路径存在 ✓ **待 L1** |
| 4 | 开关 50 次订阅数不增长 | `_bag.Clear()`（Bind）/`_bag.Dispose()`（Unbind）协议 | 路径存在 ✓ **必须 L1**（`ObservableTracker`） |
| 5 | 不串台（旧 VM 推不到新 View） | 同上 + `Unbind` 在归还池之前 | 路径存在 ✓ **待 L1** |
| 6 | 加载中取消 → 无残留 | **无** ✗（`CancellationToken` 按使用者决定 v1 不加） | ⏸ **已声明推后**（设计稿 §11-6 待 T17 回写） |
| 7 | 错 address → 抛 + 记 Error + 栈无残留 | `EnsurePoolAsync` 失败返回 false → `OpenAsync` 抛；失败发生在 `Push` 之前 | 路径存在 ✓ **待 L1** |
| 8 | `CloseAll(destroy:true)` → 池已清 + 母本已释放 | `CloseAll`：先逐个 `CloseEntry`（归还）→ 再 `ReleaseAllPools`（`DestroyPool` → `Prefab.Dispose`） | 路径存在 ✓ **必须 L1** |
| 9 | VM 可纯 C# 单测 | `ZPanelViewModel` 无任何 `UnityEngine` 类型 | 路径存在 ✓ **待 L1** |
| 10 | UI 音效接线（D8 方案 ②） | `IZUISfx` + `ZUiSfxNoop` + `ZUiSfxAdapter` + 框架注入 `_sfx` | 路径存在 ✓ **待 L1** |

**⚠️ 降级声明**：上表为**静态阅读（L4）**结论 ✓ 按 `zipper-test` §三，**它不能证明运行行为** ✗ 只能说明"代码里有对应的实现路径" ✓

---

## 4. 缺陷清单

| 级别 | 描述 | 复现/证据 | 影响 |
|---|---|---|---|
| **P1 严重** | **`ZPanelOpenOptions.SingleInstance` 声明未实现** | §1.3 的 grep：字段仅在定义处出现；`ZPanelManager.OpenAsync` 无任何"已打开则复用"分支 | 传 `true` 时会**再开一个面板**（各自独立 VM）✗ 与设计稿 §4.4/§11-2 的"置顶并返回**同一个 handle**"**对外契约不符** ✓ |
| **P2 一般** | `CloseOnMaskClick` 声明未实现 | 同上 | 传 `true` 无效果 ✗（且"遮罩"本身是面板 Prefab 的内容，框架该不该管存疑 ✗ 见 §5 选项） |
| **P2 一般** | `CacheView` 声明未实现 | 同上 | 恒为"关闭即归池缓存"✗ 传 `false` 无效果 ✗ |

> 定级只描述影响，不描述工作量 ✓

---

## 5. 放行结论

## **打回** ✗（存在 P1）

按 `zipper-test` §4.2：**存在 P0/P1 → 打回** ✓

### 5.1 三处缺陷的处置选项（**需使用者决定**，AI 不改代码）

| 缺陷 | 选项 A（推荐） | 选项 B |
|---|---|---|
| **P1 `SingleInstance`** | **实现它**：`OpenAsync` 开头查栈——若 `opts.SingleInstance` 且已存在同 `ViewModelType` 的 entry → `view.transform.SetAsLastSibling()` + **返回该 entry 已有的 handle**（不新建、不入栈）✓ 对应 §11-2 ✓ | 明确降级为 v1 不支持 → **删掉该字段**并在设计稿记账 ✓（"声明了不实现"是最糟的中间态 ✗ 会让人误用） |
| **P2 `CloseOnMaskClick`** | **删掉字段**（推荐 ✓）：遮罩是**面板 Prefab 自己的内容** ✓ 使用方在遮罩上挂 `Button` → `onClick` 里调 `RequestClose()` 就能实现 ✓ **框架不必代管** ✓ | 保留 + 文档明确"v1 未实现"✗ |
| **P2 `CacheView`** | 保留字段 + 文档明确"**v1 恒为缓存（等价 `true`）**"✓（语义清晰、将来实现不破坏接口 ✓） | 实现"关闭即销毁该实例"（需池支持"销毁单个实例"✗ 池当前无此路径 → 成本较高） |

---

## 6. 未覆盖项与原因（**不允许沉默跳过**）

| 未覆盖 | 原因 | 何时补 |
|---|---|---|
| §11 全部十条的**运行行为** | 需 Unity 环境跑（真机/编辑器）；本阶段只做了编译与静态审查 | 下一阶段（冒烟 + EditMode） |
| **冒烟**（能开 / 能关 / 再开复用同一实例 / 不卡住） | 需真实面板 Prefab + Addressable + 场景 `ZPanelRoot` | 使用者执行（AI 不能代跑） |
| **EditMode 测试 4 项**（`PanelStackTests` / `PanelBindingTests` / `PanelAsyncTests` / `Fakes` + `Tests.asmdef` 引用） | **使用者要求"全搞完了再做"** → 已声明的延后 | 下一阶段 |
| §11-6 异步取消 | 使用者决定 v1 不加 `CancellationToken`（S3 §2.1 第 3 条） | v2 / 按需 |
| 音频实听（点击音、面板开关音） | 需先挂 Addressable 音效资源 | 冒烟阶段一并 |
| `code-review`（标准轴 + 规格轴并排） | 本 skill 不看 diff；属另一条流程 | 建议与本次验证并行 |

---

## 7. 附带记录（非缺陷，供参考）

- **`using UnityEditor` 清理**：曾出现在 `ZPanelManager.cs`（会导致**打包失败** ✗ 而 `dotnet build` 抓不到 ✓）→ 已清理 ✓ 建议立规矩：**`Zipper.*` 运行时程序集禁止 `using UnityEditor`**（属待建的 C# 编码规范 ✓）
- **设计稿待回写三处**（T17）：① §6.2 模板的 `Close()` → `RequestClose()`；② §8.1 补"`_sfx` 从哪来"（框架注入 ✓ 非容器注入 ✓ 因为 View 是池 `Instantiate` 的 ✗）；③ §11-6 标注"v1 推后"
- **`PanelStackEntry` 归属**：`internal` 类型现放 `Contract/`（对外契约目录）✗ 建议归入机制目录（不强制 ✓）
