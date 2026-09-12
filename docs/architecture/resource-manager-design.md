# Zipper 资源管理器设计（Addressables 封装）— 思路与伪代码

> 状态：**v0.3 草稿，待审阅**
> 定位：`docs/planning/technical-roadmap.md` §5.1「资源管理器（Zipper.Resources）」的展开设计稿：先讲清 Addressables 是什么（面向此前不熟悉的读者），再给设计思路与**思路级伪代码**。**只讲做法，不含实现代码**。
> **实施归属**：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**。依据：`docs/standards/agent-role.md`。
> 关联：`docs/planning/technical-roadmap.md`、`docs/research/unity-framework-tech-facts.md` §3、`docs/architecture/core-design.md`
> 变更记录：v0.1 初稿；v0.2 键边界修订 + UniTask 形态同步；**v0.3 按使用者要求删除实现级代码，伪代码改为思路级**。

---

## 0. 这份文档怎么读

| 读者 | 建议路径 | 目的 |
|---|---|---|
| 第一次接触 Addressables 的人 | 先读 §2（速成），再看 §7（流程思路） | 建立心智模型 |
| 要动手实现的人 | §4（地址用法）→ §5（API 面）→ §6（内部结构）→ §7（流程） | 照着写实现 |
| 把关设计的人 | §3、§9、§10 | 职责边界、验收标准、待决项 |

一句话结论：**资源管理器 = 一层"门卫"。Addressables 是仓库本身，门卫负责登记进出、校验收到的地址、记录谁拿了什么、关门时统一清场——但库存盘点（引用计数）始终由仓库自己负责，门卫绝不二次记账。**

---

## 1. 为什么需要「资源管理器」

### 1.1 三个时代的问题

- **直接引用**（Inspector 拖引用）：复用差、换资源要手改、无法热更。
- **`Resources` 文件夹**：能按路径动态加载，但**全量打进包体、常驻内存**。
- **Addressables**：按需加载 + 用毕即还（引用计数），支持远程内容。

### 1.2 为什么还要再包一层

Addressables 是"开放市场"：任何脚本知道地址就能 Load。裸用的痛点与封装的收益：

| 痛点 | 裸用 | 包一层 |
|---|---|---|
| 谁加载了什么没人记账 | 泄漏只能事后查 | 簿记 + 统计 + 泄漏点名告警 |
| 取消/超时各写各的 | 代码重复 | 统一 CancellationToken 与超时策略 |
| 释放纪律靠自觉 | 忘 Release / 双 Release | 句柄凭证统一出口、释放幂等、兜底清理 |
| 底层升级影响面大 | 到处直接调 Addressables | 只改管理器一处 |

> **关键边界**：引用计数**依托 Addressables 原生**，封装层只做簿记与日志，**不自造引用计数**（原因见 §6.2）。
> **本框架 v1 不建业务键表**：公共 API 只收 Addressables 原生 key——**address / label**；地址怎么组织是使用工程自己的事（§4）。

---

## 2. Addressables 新手速成

### 2.1 三个核心词

| 词 | 是什么 | 比喻 |
|---|---|---|
| **Address（地址）** | 给资源起的字符串名，如 `"game/enemy_basic"` | 货架标签 |
| **Group（分组）** | 编辑器里的资源分组 | 货架区（打包单元） |
| **Catalog（清单）** | 构建时生成的"地址 → 打包位置"对照表 | 仓库索引册 |

流程：Inspector 勾 Addressable 并填地址 → 构建时按 Group 打成 AssetBundle 并生成 Catalog → 运行时查 Catalog 定位、按需下载/解压/加载。

### 2.2 运行时五件事

初始化（加载 Catalog）→ 定位（地址查 Catalog）→ 加载（LoadAssetAsync / LoadAssetsAsync 按 label / InstantiateAsync / LoadSceneAsync）→ 使用 → **释放（Release / ReleaseInstance）**。忘掉释放 = 泄漏。

### 2.3 引用计数——唯一必须牢记的规则

> **每成功 Load / Instantiate 一次，计数 +1；每 Release / ReleaseInstance 一次，计数 -1；归零后资源才可能卸载。**

- 同一地址 Load 两次 → 必须 Release 两次。**谁请求、谁释放**。
- 并发请求底层会合并为一次真实加载，但每个请求方各持句柄、各增各的计数。
- `LoadAssetAsync` 与 `InstantiateAsync` 是两类操作，后者要用 `ReleaseInstance` 抵消。

### 2.4 生命周期图

```
业务 Load(address) ──▶ 管理器（簿记：登记句柄）──▶ Addressables（查 Catalog → 下载 → 解压）
业务 Release(handle) ─▶ 管理器（簿记：注销）   ──▶ Addressables（计数-1；归零→卸载）

内存走势：0（未加载）→ 1（首次 Load）→ 2（第二方 Load）→ 1（一方 Release）→ 0（卸载）
```

### 2.5 常用 API 对照（真实 API，实现时用得到）

| 用途 | API |
|---|---|
| 初始化 | `Addressables.InitializeAsync()`（内部幂等） |
| 按地址加载 | `Addressables.LoadAssetAsync<T>(key)` |
| 等待句柄 | `handle.ToUniTask()`（UniTask.Addressables 提供的扩展） |
| 按 label 批量 | `Addressables.LoadAssetsAsync<T>(keys, callback, MergeMode)` |
| 实例化 | `Addressables.InstantiateAsync(key, parent, inWorldSpace, trackHandle)` |
| 释放资产 | `Addressables.Release(handle)` |
| 释放实例 | `Addressables.ReleaseInstance(instance)` |
| 场景 | `Addressables.LoadSceneAsync` / `UnloadSceneAsync` |
| 预下载 | `GetDownloadSizeAsync` / `DownloadDependenciesAsync` |

---

## 3. 设计思路总览

### 3.1 分层

```
使用层（UI / 音频 / 业务 / 对象池）  只认识：address/label、UniTask、CancellationToken
        ▼
管理器层 Zipper.Resources          簿记 → 调 Addressables → 包句柄 → 释放出口
        ▼
资源层 Addressables 1.x            Catalog 定位、下载、Bundle 加载/卸载、引用计数
        ▼
存储层 本地/远程 AssetBundle
```

### 3.2 管什么 / 不管什么

**管**：
1. 统一异步入口（加载资产 / 实例化 / 加载预制体母本），全部 UniTask + 可取消
2. 簿记（谁借了什么，用于统计、泄漏告警、兜底清理）
3. 句柄包装（安全的释放出口、幂等）
4. 预加载/预热
5. 生命周期（随 VContainer Scope 创建/释放）

**不管**：
- ❌ 不自己维护引用计数（§6.2）
- ❌ 不维护业务资源清单（哪个枚举对应哪个地址——使用工程自己的事，§4）
- ❌ 不持有"实例的业务所有权"（实例归对象池或调用方）
- ❌ 不做下载进度 UI、不决定资源在哪个 Group
- ❌ 不做"偷偷缓存"（预加载是显式策略）

### 3.3 与其它模块

- 依赖方向（roadmap §4.2）：`Core → Pool → Resources → {Audio, UI}`
- **对象池联动**：池化的预制体，资源本体要**长期保活**（由池/持有方持有一个"母本句柄"），克隆体进出普通对象池（§7 F3）
- VContainer：管理器是 Scope 内单例；Scope 取消令牌即全局取消源；Scope Dispose 触发兜底清理
- 异步栈：对外一律 UniTask，由 UniTask.Addressables 的 `ToUniTask()` 桥接

---

## 4. 地址与键：框架只认 Addressables 原生 key

> **边界**：`Zipper.Resources` **不内置业务资源清单**（不做枚举键表）。公共 API 只收 **address / label**。资源地址在 Inspector 勾选 Addressable 时就定死了，它是最终契约。
> **为什么**：清单一旦进框架，你每加一个自己的资源就要改框架程序集并重新导包——框架被业务绑架。正确形态：框架给"加载能力"，你的工程决定"有哪些地址"。

### 4.1 用法（思路）

- 加载：按地址加载，泛型 `T` 决定返回类型
- 地址正确性：由 Addressables 保证（地址不存在/未标记会在加载时抛错；编辑器 Addressables 窗口可检索校验，构建期也会报 catalog 错误）
- 批量：label 用于"按标签取一组"（预加载、按标签加载）

### 4.2 治理建议：你工程里的常量表（可选，不进框架）

地址拼错是运行时才炸，想提前拦就把地址收敛成**你工程自己的常量**（放业务程序集，**不是框架程序集**）：

```
你的工程代码（示例：ResKeys）
   EnemyBasic  = "game/enemy_basic"
   UIPanelSet  = "ui/panel_setting"
   SfxClick    = "audio/sfx_click"
   ...
```

新增资源 = Addressables 里打标填地址 + 这里加一行。用常量即可让拼写错误在编译期暴露。

### 4.3 演进方向（v1 之后，现在不做）

若以后地址满天飞、重构资源名要全局搜，再给框架加**键解析器**能力：加载接口收"业务键对象"，先经解析器转成 address 再走 Addressables。

触发条件：业务键复用频繁、需要编辑器遍历校验、或要按枚举做配置表。**单人毕设 v1 用常量表足够，不要提前造。**

### 4.4 与 AssetReference 的分工

| 方式 | 场景 | 说明 |
|---|---|---|
| address / label | 代码里动态加载 | 走管理器（统一簿记/取消/兜底） |
| `AssetReference`（Unity 原生字段） | Inspector 就地拖引用 | **不经管理器**，直接用 Addressables API |

---

## 5. 公共 API 一览（设计面）

> 命名是建议值，实现时可微调；key 一律为 **address 字符串**。

| 成员 | 签名（设计面） | 语义 |
|---|---|---|
| 初始化 | `UniTask InitializeAsync(CancellationToken ct)` | 幂等；首次 await 时初始化 Catalog |
| 加载资产 | `UniTask<AssetHandle<T>> LoadAssetAsync<T>(string address, string owner, CancellationToken ct)` | 校验 → 加载 → 簿记 → 返回**句柄凭证** |
| 释放 | `void Release<T>(AssetHandle<T> handle)` | 还凭证（销号 + 计数-1，幂等） |
| 加载并长期持有 | `UniTask<PrefabAsset> LoadPrefabAsync(string address, string owner, CancellationToken ct)` | 供对象池持有；Dispose 才真正释放 |
| 一次性实例化 | `UniTask<GameObject> InstantiateAsync(string address, Transform parent, CancellationToken ct)` | 经 Addressables.InstantiateAsync；销毁走 ReleaseInstance |
| 预加载 | `UniTask PreloadAsync(IEnumerable<string> addresses, CancellationToken ct)` | 逐个加载后立即释放（预热） |
| 按 label | `UniTask PreloadByLabelAsync(string label, CancellationToken ct)` | 原生批量 |
| 诊断 | 活跃句柄集合、计数、加载失败事件 | 簿记只读面 |
| 兜底清理 | `void Dispose()` | 释放全部未还句柄并逐个告警 |

> 有意**不提供**同步加载入口（`WaitForCompletion` 只在近程资源安全，见 §8 雷区 8）。

---

## 6. 内部结构

### 6.1 组成

```
ZResourceManager（Scope 内单例）
 ├─ 初始化一次性包装（AsyncLazy 之类，保证只初始化一次）
 ├─ 簿记台账：活跃句柄集合（按引用相等；句柄自带 Address/Owner）
 └─ 统一出口：加载 / 释放 / 预制体母本 / 预加载 / 兜底清理

句柄基类（非泛型：承载 Address / Owner / 是否已释放 / 释放行为 / 销号回调）
 └─ 泛型句柄（携带具体资源 T）
     └─ 台账统一收纳基类 → 无需 object 兜底、无需向下转型

母本持有者（PrefabAsset）：持有预制体句柄 + 提供纯克隆 + Dispose 释放母本
```

### 6.2 簿记为什么"只记账、不计数"

- Addressables 对每次 Load 各 +1 计数，各有各的句柄、各对应一次真实 Release。
- 若封装层再"合并记账、自计数"，就出现**两本账**：漏同步就永久泄漏或重复释放（`InvalidHandleException`）。
- 所以：**计数权归 Addressables；台账只回答"谁还没还"**，用途仅三样：统计、泄漏点名、诊断数据。
- 使用纪律：**句柄即凭证**——借出拿到句柄，释放把句柄还回来。**不提供"按地址一键释放"**（会撞上别人还在用的句柄）。

### 6.3 句柄的释放语义（唯一路径）

- 释放语义**只有一条**：句柄的 `Dispose()` ——先销号（通过构造时注入的回调），再释放内层句柄；**幂等**，重复调用安全。
- 管理器的 `Release(handle)` 只是它的转发。
- 句柄由管理器构造（构造对外不可见），防止业务伪造句柄；对外只暴露"读 + 释放"。
- 不要出现第二条释放路径（否则"销号"与"计数释放"会分叉，兜底清理会误报）。

---

## 7. 关键流程（思路级伪代码）

> 只讲做法与顺序；具体实现、命名与拆分由实现者决定。

#### F1 初始化（幂等）

```
· 用一次性包装包住 Addressables 初始化（幂等）：多个调用方并发也只真正初始化一次
· 启动阶段由组装层入口点 await 一次（见 F7）；此后所有加载前不再等待
· 不调用也不会崩：Addressables 首次加载前会自动初始化 Catalog；
  显式初始化只是把时机前移到受控阶段（可先做预下载/预热、统计耗时）
```

#### F2 加载与释放（簿记主战场）

```
加载：
  1) 校验 address 非空（空地址尽早失败）
  2) 发起 Addressables.LoadAssetAsync<T>(address)
  3) await（ToUniTask，可传 CancellationToken）
  4) 成功 → 构造句柄（address / owner / 内层句柄 / 销号回调）→ 登记台账 → 返回句柄
  5) 取消或失败 → 释放内层句柄（把计数退掉）→ 上报失败事件 → 抛错
     ⚠ 取消与失败路径都必须释放，否则计数泄漏

释放：
  转发到句柄 Dispose：销号（台账移除）+ 释放内层句柄（计数-1），幂等
```

#### F3 预制体母本 + 对象池联动（池化主路径）

```
1) 加载预制体 → 得到长期持有的"母本"（内部持句柄，不释放）
2) 母本交给对象池使用（池用母本克隆实例；纯 Instantiate，不占 Addressables 计数）
3) 实例借出/归还由池管理；组件实现池要求的接口并自行处理显隐
4) 销毁顺序（重要）：先清池（销毁全部克隆体）→ 再释放母本句柄
   ⚠ 母本释放后其 AssetBundle 可能被卸载：池内实例引用的子资源会 Missing，再扩容也实例化不出来

不打算池化、用完即毁的对象 → 走 F4，不要"先加载母本再克隆"（母本句柄会一直悬着）

> **推荐做法**：把"加载母本 → 建池 → 销毁时先清池再释放母本"这组编排**收敛到一个上层组合器**（同时依赖资源管理器与对象池管理器，见 `docs/architecture/pool-manager-design.md` §5.4）。池管理器**只接收 `GameObject prefab`**，因此不依赖资源管理器；顺序纪律由组合器保证，避免每个业务点各写一遍、各写错一遍。
```

#### F4 一次性实例化

```
· 用 Addressables.InstantiateAsync（自带句柄跟踪，计数由原生管理）
· 销毁必须用 ReleaseInstance（普通 Destroy 会泄漏计数）
· 两条路径纪律不同：F3 池化克隆体用 Destroy/归还池；F4 实例必须 ReleaseInstance，别混
```

#### F5 预加载 / 预热

```
· 按地址列表：逐个加载 → 立即释放（"资源进内存/Bundle 就位"即达目的，不留占用）
· 按 label：用原生批量加载 → 完成后释放
· 用途：把 UI 常驻面板、高频特效在切场景/加载界面时提前下载解压，消除首帧卡顿
```

#### F6 兜底清理（Scope 关闭）

```
· 遍历台账（先做快照，避免遍历中因 Dispose 触发销号修改集合而抛异常）
· 对每个未还句柄：打一条告警（点名 address 与持有者 owner）→ 释放
· 清空台账；此后禁止再加载
```

#### F7 组装层接线（谁在何时调用初始化）

```
· 管理器自身的方法（如 InitializeAsync）不是 Scope 的回调；
  由组装层写一个 VContainer 异步入口点，在 Scope 启动时 await 一次
· 入口点的 StartAsync 返回类型随工程组合变化
  （装了 UPM UniTask → UniTask；vendor 源码 + Unity 2022.3 → Task），
  但入口点只 await 一行，不受影响
· 管理器注册为 Scope 单例；入口点通过注入拿到同一实例
```

---

## 8. 新手雷区自查

| # | 雷区 | 后果 | 规避 |
|---|---|---|---|
| 1 | Load 了不 Release | 资源泄漏 | 簿记 + 兜底点名告警（F6） |
| 2 | 同一句柄 Release 两次 | `InvalidHandleException` / 误释放他人计数 | 释放幂等（§6.3） |
| 3 | 对 InstantiateAsync 的实例用普通 Destroy | 计数泄漏 | 必须 ReleaseInstance（F4） |
| 4 | 地址拼错 / 忘标记 Addressable | 运行时抛错 | 业务侧常量表集中（§4.2）+ Addressables 窗口校验 |
| 5 | 取消时不 Release | 取消即泄漏 | F2 失败/取消路径补释放 |
| 6 | 封装层自造引用计数 | 两本账，迟早对不上 | 计数只信 Addressables（§6.2） |
| 7 | 把每个池化克隆体当 Addressables 实例化 | 计数爆炸、池化失效 | F3 母本保活 + 普通池 |
| 8 | 用 `WaitForCompletion` 同步等远程资源 | 主线程卡死 | 不开放同步入口 |
| 9 | 共享资源未标 Addressable、被多 Group 引用 | 打进多份 Bundle | 编辑器依赖分析 + 共享资源标 Addressable |
| 10 | 母本释放早于池清理 | 实例子资源 Missing、扩容失败 | 销毁顺序：先清池再释放母本（F3） |

> 调试利器：Addressables **Event Viewer** 可实时看每个资源的加载/释放与引用计数，怀疑泄漏先开它。

---

## 9. 验收标准与测试点（对接 roadmap §5.1）

| 验收点 | 测试场景 | 断言 |
|---|---|---|
| 加载正确性 | 对已标记地址各加载一次 | 返回类型正确、资产非空 |
| 释放正确性 | 加载后释放，再加载 | 台账清空、无异常 |
| **重复加载去重** | 并发多次加载同一地址 | 底层仅一次真实加载；各自释放后计数归零 |
| 双释放安全 | 同句柄释放两次 | 第二次静默幂等 |
| 取消不泄漏 | 加载中途取消 | 无残留台账、计数归零 |
| 失败路径 | 加载未标记/不存在的地址 | 失败事件触发、无句柄残留 |
| 空参数拦截 | 空/null 地址 | 立即抛参数异常 |
| 池化路径 | 借 20 还 20 | 活跃计数正确、销毁后无泄漏 |
| 预加载 | 预热若干地址 | 资源就位、台账为空 |
| 兜底清理 | 故意不释放后 Dispose | 逐条告警 + 全部释放 |

---

## 10. 开放问题与待办

| 项 | 内容 | 建议 |
|---|---|---|
| asmdef 引用 | Resources 需引用：UniTask（核心）、Unity.Addressables、Unity.ResourceManager、UniTask.Addressables。**asmdef 引用不传递**，缺一个即编译期报类型不可见 | 实现第 1 步 |
| UniTask 形态 | 现为 UPM `com.cysharp.unitask`（git 源），与 roadmap「vendor 随包分发」策略冲突（UPM/git 依赖进不了 .unitypackage） | 分发给第三方前必须决策 |
| 场景资源生命周期 | 场景加载与切换时"整场资源卸载策略" | 专项小设计 |
| Addressables 工程设置 | Settings/Group 初始化、构建脚本、开发期 Play Mode Script | 随实现落地 |
| Owner 诊断 | 句柄的 owner 字段由调用方传（建议传类名），是否需要更友好的诊断上下文 | 按需 |
| 解析器演进 | 是否引入"业务键 → address"解析器 | v1 不实现（§4.3） |
| 同步入口 | 是否提供受限同步加载 | 默认不开放 |
| 预下载 | 远程内容下载接入点 | 热更需求出现再加 |

---

## 11. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.3 | 2026-09-06 | **按使用者要求删除实现级代码**：§4 键示例、§6 内部结构、§7 全部流程改为思路级伪代码/文字；保留 API 设计面、雷区、验收、待办 |
| v0.2 | 2026-09-06 | 键边界修订（框架只收 address/label，业务清单移出框架，解析器列演进）；UniTask 改 UPM 后同步 |
| v0.1 | 2026-09-06 | 初稿：Addressables 速成 + 设计思路 + 伪代码 + 雷区 + 验收 |

> 审批：本文件为设计草稿，不含代码实现；由使用者据其自行实现，AI 不代写代码（见 `docs/standards/agent-role.md`）。
