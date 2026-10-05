# UI 管理器 MVP 实施方案（S3）

> 状态：**v0.3 实施中**（2026-10-04：T1–T3 代码已成型、编译 0 error / 0 warning；**实施中的五项调整见 §2.1**；v0.2 的三项确认——R3 命名按说明走 ✓、T14 组装层方案 ✓、T6 定稿"仅 `[SerializeField]` 拖、不做 find 兜底" ✓——仍然有效）
> 定位：`docs/architecture/ui-manager-design.md`（**v0.4，已获使用者批准为实现依据**）的**施工计划**——回答"分几步做、每步改哪些文件、每步怎么验"。**设计契约以设计稿为准，本文不重复**。
> 分工：**代码由使用者实现**（`docs/standards/agent-role.md`）；本文提供分步计划、验收命令与易错点。
> **进度真源**：**§3 的 TODO list 是唯一进度真源**——任何"T5 已完成"的表述只能写在那里，别处一律引用"见 §3 Tn"。
> 前置：`LocalNotes/ui-mvvm-tutorial.md`（入门教程，不入库）；`docs/memory/progress-2026-09-26.md`（上一期上下文）

---

## 0. 目标与范围

**目标**：交付 UI 管理器 MVP，覆盖设计稿 §11 的 **10 条验收**。

| 在范围内 | 不在范围内 |
|---|---|
| 面板注册表（显式、零反射）、面板栈与 5 层层级、`OpenAsync`/`Close`/`TryHandleBack`/`CloseAll` | 转场动画、UI 性能优化、本地化、UI Toolkit（设计稿 §12） |
| View 池化 + **解绑/重绑协议** | 声明式自动绑定、通用 MVVM 框架、一 VM 多 View（设计稿 §12） |
| VM 构造与所有权（Transient 释放 / Singleton 常驻） | 子 Scope / 多场景 UI、Toast 排队与优先级（v2/v3） |
| 异步取消与失败语义、母本生命周期、`GetState` | 音频模块本身（已完成）；UI 只定义 `IZUISfx` 抽象 |
| `IZUISfx` 抽象 + 组装层适配（D8 方案 ②） | 任何业务面板的具体内容（属使用工程） |

**完成判据**：设计稿 §11 的 10 条逐条有证据（EditMode 测试 / `ObservableTracker` 计数 / 手工观察），且 §7 的"文档与代码一致性"核对通过。

---

## 1. 每批 = 一次提交（概览）

| 批 | 主题 | 一句话 | 对应验收 |
|---|---|---|---|
| **B1** | 契约 + 注册表 + 栈 + **池化与母本** | 能开、能关、能复用（`OnBind` 先留空） | §11-1、2、3、7（部分）、8（部分） |
| **B2** | **绑定协议 + VM 所有权** | 复用**不串台、不泄漏**（本模块的心脏） | §11-4、5、9 |
| **B3** | 异步/失败/`CloseAll` + `IZUISfx` + `GetState` | 边界收口 + 与音频接线 + 监控 | §11-6、7、8、10 |
| **S5** | 收尾 | 文档回写 + 记忆 + 全量验收 | — |

> **为什么不把"绑定协议"放 B1**：B1 先把 §4.2 的**生命周期钩子顺序固定下来**（`Bind/Unbind` 作为**空方法**被调用）→ B2 只填内容、不改顺序 ✓ 这样**不返工** ✓

---

## 2. 改动点清单（= 已声明文件）

> 除下列文件外**不碰任何文件**；实施中发现必须多碰 → 停下重新获批（`zipper-dev` §二.1）。

| 批 | 文件 | 状态 |
|---|---|---|
| B1 | `Assets/Zipper/UI/Zipper.UI.asmdef` | ✅（补 VContainer 引用 `GUID:b0214a6008ed146ff8f122a6a9c2f6cc`） |
| B1 | `Assets/Zipper/UI/IZPanelManager.cs` | ✅（5 个成员：`OpenAsync`/`Register`/`TryHandleBack`/`CloseAll`/`GetState`） |
| B1 | `Assets/Zipper/UI/ZPanelViewModel.cs` | ✅ |
| B1 | `Assets/Zipper/UI/ZPanelHandle.cs` | ✅ |
| B1 | `Assets/Zipper/UI/ZPanelOpenOptions.cs` | ✅（4 项已填 + 默认值） |
| B1 | `Assets/Zipper/UI/ZPanelsState.cs` | ✅ |
| B1 | `Assets/Zipper/UI/ZPanelState.cs` | ✅（**实施中新增**：单个面板摘要） |
| B1 | `Assets/Zipper/UI/Contract/ZPanelLayer.cs` | ✅ |
| B1 | `Assets/Zipper/UI/ZPanelRoot.cs` | ✅（**实际落在根目录**，非 `Contract/`；命名空间 `Zipper.UI`） |
| B1 | `Assets/Zipper/UI/Contract/PanelStackEntry.cs` | ✅（`internal`；含构造函数与 `OwnsViewModel`） |
| B1 | `Assets/Zipper/UI/Register/PanelRegistry.cs` | ✅ |
| B1 | `Assets/Zipper/UI/Register/Registration.cs` | ✅（含 `OwnsViewModel`） |
| B1 | `Assets/Zipper/UI/ZPanel.cs` | ✅（`MonoBehaviour + IZObjectPoolItem`；钩子映射 + `_bag` + `CloseRequester` + `_sfx`） |
| B1 | `Assets/Zipper/UI/ZPanelManager.cs` | ✅（295 行：注册表转发 + 栈 + 开关 + 池化 + 母本 + 失败语义 + `CloseAll` + `GetState`） |
| B1 | `Assets/Zipper/UI/ZPanelBootstrapper.cs` | ✅（`Phase = ZBootPhase.UI`） |
| B1 | `Assets/Zipper/DI/GameLifetimeScope.cs` | ✅（`ZPanelRoot` + `IZPanelManager` + `ZPanelBootstrapper` + `IZUISfx`） |
| B1 | `Assets/Zipper/DI/Prefabs/GameLifetimeScope.prefab` | ✅ |
| B1 | `Assets/Zipper/DI/Prefabs/ZPanelRoot.prefab` | ✅（**实施中新增**：层级父节点预制体） |
| B2 | `Assets/Zipper/UI/ZPanelViewModel.cs` | ✅（`CloseRequested` + `Dispose`） |
| B2 | `Assets/Zipper/UI/ZPanel.cs` | ✅（`DisposableBag` + `Bind/Unbind` 实现） |
| B2 | `Assets/Zipper/UI/ZPanelManager.cs` | ✅（VM 构造 + 所有权决策） |
| B3 | `Assets/Zipper/UI/ZPanelManager.cs` | ✅（失败语义 / `CloseAll(destroy)` / `GetState`） |
| B3 | `Assets/Zipper/UI/IZUISfx.cs` | ✅（**同一文件内含** `IZUISfx` + `ZUiSfxNoop`） |
| B3 | `Assets/Zipper/DI/ZUiSfxAdapter.cs` | ✅（**实施中新增**：组装层桥到 `IZAudioManager`） |
| B3 | `Assets/Zipper/UI/ZPanelsState.cs` | ✅（补内容） |
| — | `Assets/Zipper/Tests/Zipper.Tests.asmdef` + `PanelStackTests.cs` + `PanelBindingTests.cs` + `PanelAsyncTests.cs` + `Fakes/FakePanel*.cs` | ⬜ **按使用者要求延后**（"全搞完了再做"）→ 属**已声明的延后**，须在验收报告的"未覆盖项"里逐条登记 |

**不动**（已核对：实际 diff 里一个都没碰 ✓）：`Zipper.Core` / `Zipper.Pool` / `Zipper.Resources` / `Zipper.Audio` ✓

### 2.1 实施中的调整（v0.3 记录）

| # | 调整 | 依据 / 影响 |
|---|---|---|
| 1 | **目录与命名空间按实现落定**：`Contract/`（public 契约：`ZPanelLayer` / `ZPanelRoot`）、`Register/`（机制：`PanelRegistry` / `Registration`）、根目录（对外服务与 DTO） | 替代 v0.1 的 `Internal/` 写法 ✓；`PanelStackEntry`（`internal`）现放在 `Contract/` —— **建议**归入机制目录，**不强制** |
| 2 | **实施顺序**：`T1 → T5 → T2 → T3 → T4 → T6 → T7` | T2 的泛型委托要求 `TView : Component, IZObjectPoolItem` → 必须先有 T5 的 `ZPanel` 基类成立，否则编译不过 ✓ |
| 3 | **`CancellationToken` v1 不加**（使用者决定：暂时不写这么完善，之后再加） | 可选参数**源兼容** → 之后补**不影响任何调用方** ✓；**代价**：设计稿 **§11-6（异步取消回滚）推后到 v2/按需** ✗ 需在 T17 回写设计稿 |
| 4 | **池 `MaxSize = 0`（不限）**（使用者决定） | "就几个页面，能创多少个" ✓ 与音频池（上限 32）的取舍相反，属**有意**（那边是"一次爆炸 200 个音效"的场景） |
| 5 | **框架自带场景资产入库** | `Assets/Zipper/DI/Prefabs/GameLifetimeScope.prefab`（"拖出来就能用，不用自己手动创建"）→ 已同步 `docs/standards/git-workflow.md` **§1.1 豁免**（框架的一部分 → 入库；业务资产仍不入库） |
| 6 | **框架给 View 递东西的两个通道**：`CloseRequester` → `protected RequestClose()`、`SetSfx()` → `protected _sfx` | 实施中发现的设计缺口：`ZPanel.Close()`/`Open()` 是 `internal` ✗ 而**业务侧 View 在另一个程序集**（Assembly-CSharp）✗ → View **没有任何合法路径**请求关闭 ✗；而"View 不碰容器"（设计稿 §3/§7.3）是硬纪律 ✗ → 解法：**框架在 `OpenAsync` 里注入、在 `CloseEntry` 里清空**（`internal` 注入入口 + `protected` 读取字段）✓ 待办：**设计稿 §6.2 的模板需从 `Close()` 改为 `RequestClose()`**，**§8.1 需补"`_sfx` 从哪来"**（T17 回写） |

---

## 3. TODO list（**进度唯一真源**）

> 状态标记：`[ ]` 未开始 / `[~]` 进行中 / `[x]` 已完成（含提交 short-hash）

### B1 · 契约 + 注册表 + 栈 + 池化与母本

> **实施顺序（v0.3 调整）**：`T1 → T5 → T2 → T3 → T4 → T6 → T7`（理由见 §2.1 第 2 条）

- [~] **T1** 补全 `IZPanelManager` 契约 + 新建 `ZPanelOpenOptions` / `ZPanelHandle` / `ZPanelViewModel` 骨架
  - **状态**：代码已成型、编译通过；**唯一尾巴**：`ZPanelOpenOptions` **仍是空类** → 待填 4 项
    - `Layer`（默认 **`Main`**）/ `CloseOnMaskClick`（默认 `false`）/ `SingleInstance`（默认 `false`）/ `CacheView`（默认 **`true`**）
    - 类型必须用 **class**：接口上 `= default` 即 `null` → 实现里须 `options ?? DefaultOptions` 兜底（与 `ZAudioPlayOptions` 同一约定）
  - 前置：asmdef 补 VContainer ✅ 已完成
  - 验：`dotnet build Zipper.UI.csproj` → 0 error ✅ 已通过
- [x] **T2** 内部注册表 `PanelRegistry` + `Registration`
  - **状态**：完成（**待提交**）✓ 含"**泛型在注册期落地、运行期走委托**"（`CreatePool`/`DestroyPool`/`GetItem`）✓ 零反射 ✓；重复注册记 **Warning** ✓
  - 验：编译 ✓（"未注册 → 抛明确异常"由 `ZPanelManager` 承担，T6/T7 覆盖）
- [~] **T3** 栈与层级（`ZPanelLayer` + `ZPanelRoot` + `PanelStackEntry` + `ZPanelManager` 的栈操作）
  - **状态**：`Contract/` 三个文件已建；**待补三项**：
    1. ⚠️ **`ZPanelRoot` 必须继承 `MonoBehaviour`** —— 否则 `[SerializeField]` 被**完全忽略**（5 个 `Transform` 永远是 null ✗）且无法挂到 GameObject 上（设计稿 §4.3 要求"场景预置"）
    2. `PanelStackEntry` 缺**构造函数**（只读属性无法实例化 ✗）
    3. **`ZPanelManager` 尚未创建** ← 下一步就是它
  - 要点：`TryHandleBack` 取"**层级最高、同层最后打开**"（`Layer` 降序、同层 `Sequence` 降序取第一个 —— **不是**简单取栈顶 ✗）；关面板时**按身份移栈**（不用索引 ✗）；`Toast/Loading` **不进栈**
  - 验：**编译通过 + 形状对** ✓（行为验收要等 T4 有真实 View 实例 → 合并到 **T7**）
- [ ] **T4** 池化与母本（`LoadPrefabAsync` → `Registration.CreatePool` → `GetItem` / 归还；卸载时**先清池 → 再释放母本**）
  - **定稿（2026-10-04 使用者拍板）**：**池 `MaxSize = 0`（不限）** —— "就几个页面，能创多少个" ✓（与音频池上限 32 相反是**有意**的 ✓ 见 §2.1 第 4 条）
  - 验：编译通过（行为在 T5/T7 验）
- [ ] **T5** `ZPanel` 基类 + **§4.2 生命周期钩子顺序**（`OnCreate/OnOpen/OnClose/OnRecycle` + 池回调映射；`OnBind/OnUnbind` 留空占位）
  - **状态**：`MonoBehaviour + IZObjectPoolItem` 已到位 ✓ 但成员体是 `throw new NotImplementedException()` ⚠️
    - **本步补实现**；且在补完之前**绝对不要建池** —— `CreatePool` 会立刻 `Instantiate` 并写 `ReturnToPool`（setter 现在会抛）→ 当场炸 ✗
  - 验：手工——开面板后 `OnCreate`/`OnOpen` 各一次；关闭后 `OnClose`；再开时 `OnCreate` **不再调**、只调 `OnOpen` ✓
- [ ] **T6** `ZPanelBootstrapper`（`Phase = ZBootPhase.UI`）+ `GameLifetimeScope` 注册 + `ZPanelRoot` 接线
  - **定稿（2026-10-04 使用者拍板）**：`ZPanelRoot` **只走 `[SerializeField]` 显式引用**（与 Mixer 同一取向）→ **不做 `Find*` 兜底** ✓
    - 理由：显式、可查、零查找开销、与既有做法一致；**未拖 → 启动时明确报错**（属配置错误，应当暴露，不静默兜底）
    - 报错文案要**可操作**，例如：「`GameLifetimeScope` 未配置 `ZPanelRoot`：请在 Inspector 上拖入场景中的 ZPanelRoot」
    - ⚠️ 不要用 `FindObjectOfType`（Unity 2023+ 已废弃；实测本工程 2022.3.62 的 `CoreModule` 里它尚未标 Obsolete，但**跨版本安全**要求用新 API）；即便将来要加兜底，也用 `FindFirstObjectByType` / `FindObjectsByType`（2022.3 已可用 ✓ 实测反射确认）
  - 要点：5 层层级父节点由 `ZPanelRoot` 在**场景里预置**（设计稿 §4.3）；`IZPanelManager` 由容器构造 ✓
  - 验：**未拖 `ZPanelRoot` 时进 Play → 出现明确 Error（不是 NRE、不静默失败）**；拖上后 `GetState()` 可读；未注册面板打开时抛异常 + 记 Error
- [ ] **T7** B1 验收（EditMode + 手工）
  - 验（EditMode，落 `PanelStackTests.cs`）：**§11-1** 栈顺序与 `TryHandleBack` 返回 false；**§11-2** `SingleInstance` 返回同一 handle；**§11-3** 重复 `Close` 幂等
  - 验（手工）：开→关→再开，Hierarchy 里**同一实例被复用**（不新建）✓；池 `CountAll` 不增长 ✓
  - **提交点 ①**：`feat(ui): 面板管理器（注册表/栈/池化 + 生命周期钩子）`

### B2 · 绑定协议 + VM 所有权（**本模块的心脏**）

- [ ] **T8** 补 `ZPanelViewModel` 内容（T1 已建骨架）：`IDisposable` 释放自己的 R3 资源 + `CloseRequested` 供 View 订阅
  - 要点：**VM 里不许出现 `UnityEngine` 类型**（§6.3 纪律，验收 §11-9 的前提）
  - 验：编译通过 + 纯 C# 测试能 `new` 出来（T11）
- [ ] **T9** `ZPanel` 的 `DisposableBag` + `Bind/Unbind` **实现**（`_bag.Clear()` → `OnBind` → `_bag.Dispose()` → `OnUnbind`）
  - 要点：§5.3 三条硬规则；`Unbind` **由框架在归池/销毁前调**，使用方无权跳过 ✓
  - 验：T11 的"50 次开关订阅数不增长"
- [ ] **T10** VM 构造与所有权（`OpenAsync<TViewModel>` 用容器构造 VM；**Transient → 关闭时框架 `Dispose`**；Singleton/Scoped → **不 Dispose**）
  - 依据：设计稿 §2.3（VContainer 只跟踪 Singleton/Scoped 的 `IDisposable`）
  - 验：T11 + 手工（常驻面板状态保留）
- [ ] **T11** B2 验收（**关键**）
  - 验（EditMode）：**§11-4** 同一面板 Open/Close **50 次** → `ObservableTracker` 里该 View 的订阅数**不增长**、`OnBind`/`OnUnbind` 次数相等；**§11-5** 改旧 VM 属性 → View **无变化**（不串台）；**§11-9** `new ShopViewModel()` 纯 C# 断言
  - 验（手工）：开 50 次后点一次按钮，日志**只出现一次**（防 `onClick` 未清的叠加坑）
  - **提交点 ②**：`feat(ui): View 复用解绑/重绑协议 + VM 所有权`

### B3 · 异步/失败/`CloseAll` + `IZUISfx` + `GetState`

- [ ] **T12** 失败语义（**加载失败抛异常 + 栈不污染**；VM 构造失败时已建 View 归池/销毁）
  - ⚠️ **取消语义已按使用者决定推后**（v1 不加 `CancellationToken`，见 §2.1 第 3 条）→ 设计稿 **§11-6 推后到 v2/按需** ✗ 需在 T17 回写设计稿
  - 验（EditMode）：**§11-7** 错 address → 抛 + 记 Error + 栈无残留
- [ ] **T13** `CloseAll(destroy)` 与顺序（全关归池；`destroy: true` → **先清池 → 再释放母本**）+ 回调里再 Open/Close 不破坏栈
  - 验（EditMode）：**§11-8** 池已清 + `AssetHandle` 已释放（资源簿记归零）
- [ ] **T14** `IZUISfx` 抽象 + 空实现（UI 侧）+ 组装层适配（桥到 `IZAudioManager`）
  - 要点：D8 方案 ② —— `Zipper.UI` **不引** `Zipper.Audio` ✓；纪律"点击路径不同步等加载"、面板打开时 `Preload` 预热
  - 验（手工）：设一个面板，点按钮出声（点击音）+ 打开面板出声（PanelOpen）✓；把 `IZUISfx` 换成空实现后**照常运行不报错** ✓
- [ ] **T15** `GetState()`（`ZPanelsState` DTO，照 `ZPoolsState` 形状：`{ get; private set; }` + `internal` 构造）
  - 验：能读到栈深、各层面板数、池计数 ✓
- [ ] **T16** B3 验收 + 全量回归（EditMode 全绿 + 手工清单走完）
  - **提交点 ③**：`feat(ui): 面板异步/失败语义 + CloseAll 顺序 + UI 音效抽象 + 监控 DTO`

### S5 · 收尾

- [ ] **T17** 文档回写（`ui-manager-design.md` §11 补实施进度、`roadmap` M3 状态、`bootstrap-design.md` §10 的 UI 行）+ 记忆条目（`docs/memory/progress-<日期>.md`）+ 三文档一致性核对
  - **提交点 ④**：`docs: UI MVP 收尾回填（设计稿进度 / roadmap / 记忆）`

---

## 4. 每步的验收命令（模板）

```powershell
# 1) 编译（Unity 已生成 csproj 后；注意 Windows 下需显式 UTF-8 读中文文件）
dotnet build "Zipper.UI.csproj" -nologo -v:q

# 2) 全量编译（UI 改动若触及 DI/测试引用）
foreach ($p in @("Zipper.Core.csproj","Zipper.Pool.csproj","Zipper.Resources.csproj","Zipper.Audio.csproj","Zipper.UI.csproj")) {
  dotnet build $p -nologo -v:q 2>&1 | Select-String "错误|error" | Select-Object -First 3
}

# 3) EditMode 测试（Unity Test Runner 里跑；测试程序集是 Editor-only）
#    Window -> General -> Test Runner -> EditMode -> Run All
```

**手工验收的观察点**（每次都看这三样）：

| 看哪 | 期望 |
|---|---|
| Hierarchy | 面板实例挂在 `ZPanelRoot` 对应的层级父节点下；关闭后 **inactive 但仍在**（归池）；`CloseAll(destroy:true)` 后消失 |
| `Window -> Observable Tracker` | 该 View 的订阅数**不随开关次数增长**（B2 之后） |
| Console | 加载失败/未注册 → 有明确 Error；正常开关 **无 Warning 无 Error** |

---

## 5. 测试策略

| 层 | 测什么 | 落在哪 |
|---|---|---|
| **纯逻辑（无 Unity）** | VM 的状态/命令（§11-9） | `Zipper.Tests`（EditMode，`Zipper.Tests.asmdef` 加 `Zipper.UI` 引用） |
| **生命周期与栈（需 Unity 对象）** | 栈顺序、重复开/关、池化复用、`CloseAll` 顺序（§11-1/2/3/8） | 同上（EditMode 可 `new GameObject()` ✓） |
| **绑定泄漏（关键）** | 50 次开关订阅数不增长、不串台（§11-4/5） | 同上 + `ObservableTracker` 计数 |
| **异步/失败** | 取消回滚、加载失败抛（§11-6/7） | 同上（假 `IZResourceManager` 抛异常 → 复用音频那套 `Fakes` ✓） |
| **手工** | 音效接线（§11-10 + T14）、听感/视觉 | Editor 试听清单 |

⚠️ `internal` 成员（`PanelRegistry` 等）若要在测试里直接断言 → 需在 `Zipper.UI` 加 `AssemblyInfo.cs`：`[assembly: InternalsVisibleTo("Zipper.Tests")]`（**照音频模块的做法** ✓ 不把它们改成 `public`）。

---

## 6. 风险与回滚

| # | 风险 | 应对 |
|---|---|---|
| **R1** | **绑定泄漏**（复用串台）—— 本模块最大风险，且"不报错、只是偶尔显示错" | B2 独立成批 + `ObservableTracker` 计数断言（§11-4/5）；`OnClick` 的 `RemoveAllListeners` 进"面板写法约定"清单（设计稿 §6.4） |
| **R2** | **母本与池的销毁顺序**弄反 → 实例残留（与音频那边同一个坑） | T4/T13 明确"先清池 → 再释放母本"；用 `CloseAll(destroy:true)` 后 Hierarchy 是否清空当证据 |
| **R3** | **命名分歧**：设计稿 §9 目录列了 `ZPanelRegistry.cs`，但同节说明"内部机制类型**不加前缀**"（如 `PanelSlot`/`PanelStackEntry`）→ 两者矛盾 | ✅ **已确认（2026-10-04）：按说明走** —— `Internal/PanelRegistry.cs` / `Internal/PanelStackEntry.cs`；设计稿目录清单里那个带前缀的名字视为笔误，**收尾（T17）时一并更正** |
| **R4** | VM 所有权搞反（Transient 忘了 Dispose / 常驻误 Dispose） | T10 专步 + 依据 §2.3 的容器事实；测试断言"关闭后 VM 已 Dispose" |
| **R5** | `ZPanelRoot` 未配置 → 启动即失败（**T6 定稿为"不做 find 兜底"**，配置全靠手工拖） | T6 的 bootstrapper 里**明确报错**，且文案要**可操作**（"请在 GameLifetimeScope 上拖入 ZPanelRoot"）——不是 NRE、不静默；手工验收第一条就查"未配时报错是否清楚" |
| **R6** | 一次性做太大 → 到 B3 才暴露问题 | 严格按批提交（每批自测通过才提交）✓ 批次之间不并行 |

**回滚**：每批一个提交 ✓ 出问题回退该批提交即可（不 `rebase`、不 `force-push` ✓ 见 `git-workflow.md` §4）✓

---

## 7. 完成判据（S4 → S5 的门）

1. 设计稿 **§11 的 10 条**逐条有证据（EditMode 输出 / `ObservableTracker` 计数 / 手工观察记录）；
2. **改动点清单与 `git diff --stat` 逐一对上**（§2 的表格 vs 实际改动文件）；
3. **文档与代码一致性核对**：设计稿 §11 的实施进度表、`roadmap` M3 状态、`bootstrap-design.md` §10 的 UI 行三处同步；
4. 本地提交完成（**不 push** ✓ 推送由你决定）。

---

## 8. 明确不做（本计划外）

沿用设计稿 §12 的 9 项（转场动画 / UI 性能优化 / 本地化 / UI Toolkit / 子 Scope / 声明式绑定 / 通用 MVVM / 一 VM 多 View / Toast 排队）✓ 以及 v2/v3 的内容（Toast-Loading 层细节、`GetState` 扩展、`SingleInstance` 置顶复用之外的策略）✓

---

## 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| **v0.4** | 2026-10-05 | **实施完成回填（T1–T16 全部落地）**：① §2 改动点清单**逐条对齐实际 diff**——基线 `44f93ff..9c06286`，**41 文件 / +1285 −7**；记录三处实际与计划的差异（`ZPanelRoot.cs` 落在根目录、`IZUISfx.cs` 内含 `ZUiSfxNoop`）与三处实施中新增（`ZPanelState.cs` / `ZUiSfxAdapter.cs` / `ZPanelRoot.prefab`）；**测试 4 项标注为"已声明的延后"**（使用者要求"全搞完了再做"）；② §2.1 新增**第 6 条**（框架给 View 递东西的两个通道：`CloseRequester` + `_sfx`；含"设计稿 §6.2 的 `Close()` 应改 `RequestClose()`、§8.1 需补 `_sfx` 来源"的待办）；③ 提交序列：`58562f6` → `e195650` → `7cd4b8d` → `9c06286` |
| v0.3 | 2026-10-04 | **实施中同步（T1–T3 已成型）**：① §2 改动点清单按**实际目录结构**对齐（新增 `Contract/` + `Register/` + `DI/Prefabs/GameLifetimeScope.prefab`；逐个标注 ✅/⚠️/⬜ 状态）；② 新增 **§2.1 实施中的五项调整**（目录落定 / 顺序调整 `T1→T5→T2→T3→T4` / **`CancellationToken` v1 不加**（→ §11-6 推后）/ **池 `MaxSize = 0` 不限** / **框架自带场景资产入库**）；③ §3 的 B1 段按实际状态重写；④ **`docs/standards/git-workflow.md`** 新增 **§1.1 豁免** |
| v0.2 | 2026-10-04 | **批准版（三项确认落定）**：① **R3 命名** —— 按设计稿 §9 的**说明**走（内部机制不加前缀），目录清单里带前缀的名字视为笔误、收尾时更正；② **T14** —— `IZUISfx` 的组装层适配放 `Zipper.DI`（确认）；③ **T6 定稿** —— `ZPanelRoot` **只走 `[SerializeField]` 显式引用、不做 `Find*` 兜底**，未配置即明确报错（附"不要用已废弃的 `FindObjectOfType`"的提醒与实测说明：本工程 2022.3.62 的 `FindFirstObjectByType`/`FindObjectsByType` 已可用）。风险表 R3/R5 同步 |
| v0.1 | 2026-10-04 | 初稿（S3）：依据设计稿 v0.4 拆 **3 批 + 收尾**，每批 = 一次提交；给出改动点清单（= 已声明文件）、**17 条 TODO**（进度唯一真源）、每步验收命令与观察点、测试策略、6 条风险与回滚、完成判据（含"改动点清单与 diff 对得上"的核对） |
