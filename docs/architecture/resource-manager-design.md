# Zipper 资源管理器设计（Addressables 封装）— 思路与伪代码

> 状态：**v0.2 草稿，待审阅**（供 M1「地基」实施引用）
> 定位：本文件是 `docs/planning/technical-roadmap.md` §5.1「资源管理器（Zipper.Resources）」的**展开设计稿**：先讲清 Addressables 是什么（面向此前不熟悉 Addressables 的读者），再给封装层设计思路与 C# 风格伪代码。**含伪代码，不含可编译实现**。
> 关联文档：`docs/planning/technical-roadmap.md`（路线图）、`docs/research/unity-framework-tech-facts.md` §3（Addressables 调研事实）
> 变更记录：v0.1 初稿；**v0.2**（2026-09）——① 键体系边界修订：框架公共 API **只收 Addressables 原生 key（address/label）**，业务键清单移出框架（§4），解析器列为 M1 后演进；② UniTask 形态变更：由 vendor 源码版改为 **UPM `com.cysharp.unitask` 2.5.11（git 源）**，VContainer 宏随之生效（§7 F1 签名分支更新）；③ asmdef 引用位置与工程现状同步（§3.3/§10）。

---

## 0. 这份文档怎么读

| 读者 | 建议路径 | 目的 |
|---|---|---|
| 第一次接触 Addressables 的人 | 先读 §2（速成），再看 §7（伪代码） | 建立心智模型，看得懂代码在做什么 |
| 只想抄设计的人 | §4 → §5 → §6 → §7 | 地址用法 + API 面 + 内部结构一网打尽 |
| M1 实施前把关的人 | §3、§9、§10 | 职责边界、验收标准、待决项 |

一句话结论先行：**资源管理器 = 一层"门卫"。Addressables 是资源仓库本身，门卫负责登记进出、校验收到的地址、记录谁拿了什么、关门时统一清场——但真正的库存盘点（引用计数）始终由仓库自己负责，门卫绝不二次记账。**

---

## 1. 为什么需要「资源管理器」

### 1.1 三个时代的问题

**时代一：直接引用（Inspector 拖引用）。**
场景/预制体上直接挂引用，资源随场景一起加载、常驻。问题：想复用同一个怪物到 10 个场景，每个场景都打包一份；想换资源要手动改每个引用；想支持后续下载（热更）完全没门。

**时代二：`Resources` 文件夹。**
`Resources.Load<T>("path")` 解决了"按路径动态加载"，但 **Resources 下所有资源永远打进包体、永远常驻内存**（哪怕只用到 1%）。毕设 Demo 可能无感，但架构上讲不出口。

**时代三：Addressables。** 资源不再"在不在内存"二选一，而是**按需加载 + 用毕即还（引用计数）**，并且支持远程内容（热更潜力）。它正是 roadmap §3 定的资源层选型。

### 1.2 Addressables 解决什么

- **内存**：资源用多少加载多少，没人用了自动腾出（引用计数归零即释放）。
- **热更潜力**：资源可以放远程服务器，运行时下载。
- **依赖正确性**：打包时自动分析依赖关系，A 引用了 B，B 不会丢。
- **解耦**：业务代码不关心资源在本地还是远程、在哪台服务器——只认"地址"。

### 1.3 为什么还要再包一层（本框架的定位）

Addressables 是"开放市场"：任何脚本只要知道地址字符串就能 Load。直接裸用会带来四个实际痛点，**这正是 Zipper.Resources 存在的理由**：

| 痛点 | 裸用 Addressables | 包一层后 |
|---|---|---|
| 谁加载了什么，没人记账 | 泄漏只能靠 Event Viewer 事后查 | 簿记 + 统计 + 泄漏时警告日志 |
| 每个模块各自 `await`，取消/超时各写各的 | 代码重复 | 统一 CancellationToken 与超时策略 |
| 释放纪律靠自觉 | 忘 Release / 双 Release 各自踩 | 句柄凭证统一出口、Release 幂等、Dispose 兜底 |
| 底层升级/替换影响面大 | 到处直接调 Addressables API | 只改管理器一处 |

> **关键边界（roadmap §5.1 已定）**：引用计数**依托 Addressables 原生**，封装层只做簿记与日志，**不自造一套引用计数**。原因见 §6.2 —— 自造计数是与 Addressables 双重记账，是新手最容易写歪的地方。
>
> **本框架 v1 不建业务键表**（不会因为你加一个自己的资源就要改框架程序集）——框架公共 API 只收 Addressables 原生 key：**address / label**。地址怎么组织、要不要类型安全键，是你工程自己的事（§4）。

---

## 2. Addressables 新手速成（看不懂后面先读这节）

### 2.1 三个核心词

| 词 | 是什么 | 通俗比喻 |
|---|---|---|
| **Address（地址）** | 给某个资源起的字符串名字，如 `"game/enemy_basic"` | 货架上的**标签**。一个资源可以起多个别名，也可以没有地址（那它只能被别的资源间接引用） |
| **Group（分组）** | 编辑器里把资源分组的容器 | **仓库货架区**。打包时每个 Group 大致对应若干 AssetBundle |
| **Catalog（目录/清单）** | 构建时生成的"地址 → 打包位置"对照表 | **仓库索引册**。运行时先读它，才知道去哪找货 |

资源在编辑器里通过 Inspector 勾选 Addressable 并填地址 → 构建（Content Build）时按 Group 打成 AssetBundle 并生成 Catalog → 运行时靠 Catalog 定位、按需下载/解压/加载。

### 2.2 运行时五件事

1. **初始化**：加载 Catalog（`Addressables.InitializeAsync()`）。通常框架启动时做一次。
2. **定位**：拿地址 → 查 Catalog → 找到对应的 Bundle（可能在本地，可能要下载）。
3. **加载**：`LoadAssetAsync` / `LoadAssetsAsync`（按 label）/ `InstantiateAsync` / `LoadSceneAsync`。
4. **使用**：拿到资源/实例，正常用。
5. **释放**：`Release(handle)` / `ReleaseInstance(go)` / `ReleaseScene`。**这一步忘掉 = 泄漏。**

加载是**异步**的（Bundle 可能正在下载/解压），所以 Addressables 的加载接口全部返回"句柄"。

### 2.3 引用计数——唯一必须牢记的规则

> **每成功 Load / Instantiate 一次，资源计数 +1；每 Release / ReleaseInstance 一次，计数 -1；归零后资源才可能被卸载。**

- 同一个地址 Load 两次 → 计数 +2 → 必须 Release 两次。**谁请求、谁释放**，各管各的，不搞"全局只加载一份"的幻觉。
- 好的一面：同一资源的**并发**请求在 Addressables 底层会被合并为一次真正加载（不会重复下载/解压），但每个请求方仍持独立句柄、各增各的计数。
- 对同一资源，`LoadAssetAsync` 与 `InstantiateAsync` 是**两类操作**：Instantiate 的计数要用 `ReleaseInstance` 抵消。

**为什么 Addressables 要这样设计？** 因为一个资源可能被 UI、特效、音频同时用，谁先走谁后走不可预测。让每个使用方独立"借/还"，底层自然安全。

### 2.4 生命周期图

```
                    ┌────────────── 业务代码 ──────────────┐
                    │  LoadAsync("address")   Release(handle)│
                    ▼                                ▲
        ┌───────────────────────┐        ┌───────────┴──────────┐
        │   Zipper.Resources    │        │   Zipper.Resources   │
        │   （簿记：登记句柄）    │        │   （簿记：注销句柄）    │
        └───────────┬───────────┘        └───────────┬──────────┘
                    │ LoadAssetAsync(address)        │ Release(handle)
                    ▼                                ▼
        ┌───────────────────────┐        ┌─────────────────────┐
        │    Addressables 运行时  │◄───────│  Addressables 运行时 │
        │  查 Catalog→下载→解压    │        │  计数-1；归零→卸载    │
        └───────────────────────┘        └─────────────────────┘
```

内存走势（同一资源）：
`计数 0（未加载）→ 首次 Load 后 1 → 第二方 Load 后 2 → 一方 Release 后 1 → 最后一方 Release 后 0 → 资源被卸载，内存回落。`

### 2.5 常用 API 对照表（真实 API 名，伪代码会用到）

| 用途 | 真实 API（Addressables 1.x + UniTask.Addressables） | 说明 |
|---|---|---|
| 初始化 | `Addressables.InitializeAsync()` | 可重复调用；框架建议显式 await 一次 |
| 按地址加载资产 | `Addressables.LoadAssetAsync<T>(object key)` | key 通常是 string 地址 |
| 异步等待句柄 | `handle.ToUniTask()`（UniTask.Addressables 提供） | 也支持传 CancellationToken |
| 按 label 批量 | `Addressables.LoadAssetsAsync<T>(IList keys, Action<T> cb, MergeMode)` | label 是另一种 key（打标批量） |
| 实例化预制体 | `Addressables.InstantiateAsync(key, parent, inWorldSpace, trackHandle)` | 默认 trackHandle=true，需 ReleaseInstance |
| 释放资产 | `Addressables.Release(handle)` | 计数 -1 |
| 释放实例 | `Addressables.ReleaseInstance(instance)` | 销毁对象并计数 -1 |
| 加载场景 | `Addressables.LoadSceneAsync(key, LoadSceneMode)` | 对应 `Addressables.UnloadSceneAsync` |
| 预下载 | `Addressables.GetDownloadSizeAsync(key)` / `DownloadDependenciesAsync(key)` | 热更/预加载用 |

> 本项目 UniTask 现为 **UPM 包 `com.cysharp.unitask` 2.5.11**（git 源，缓存于 PackageCache）：包内含核心程序集 `UniTask`（`UniTask<T>`/AsyncLazy/取消扩展）与扩展程序集 `Runtime/External/Addressables` 即 **UniTask.Addressables**（`.ToUniTask()`）。Zipper.Resources 的 asmdef 需引用 **四个**程序集：`UniTask` + `Unity.Addressables` + `Unity.ResourceManager` + `UniTask.Addressables`（明细与理由见 §10）。

---

## 3. 设计思路总览

### 3.1 分层与职责边界

```
使用层   UI 管理器 / 音频管理器 / 业务逻辑 / 对象池
           │  只认识：address / label、UniTask、CancellationToken
           ▼
管理器层  Zipper.Resources（本设计）
           │  职责：簿记 → 调 Addressables → 包句柄 → 释放出口
           ▼
资源层    Addressables 1.x（官方包，前置依赖）
           │  职责：Catalog 定位、下载、Bundle 加载/卸载、引用计数、场景句柄
           ▼
存储层    本地/远程 AssetBundle
```

**资源管理器管什么（职责清单）：**

1. 统一异步入口：`LoadAsync<T>(address)` / `InstantiateAsync(address)` / `LoadPrefabAsync(address)`，全部 UniTask + 可取消。
2. 簿记：登记每个成功句柄（含谁借的），用于统计、泄漏告警、Dispose 兜底清理。
3. 句柄包装：`AssetHandle<T>`，提供安全的 `Release()` 与 `IDisposable`（支持 `using`）。
4. 预加载/预热：批量按 address 或 label 加载，供对象池、UI 常用资源预热。
5. 生命周期：随 VContainer Scope 创建/释放；Scope Dispose 时兜底释放全部未还句柄并告警。

**资源管理器不管什么（克制清单，防过度设计）：**

- ❌ 不自己维护引用计数（那是 Addressables 的事，见 §6.2）。
- ❌ 不维护"业务资源清单"（哪个枚举对应哪个地址——那是你工程的事，见 §4）。
- ❌ 不直接生成/持有资源实例的"业务所有权"（实例归对象池或调用方）。
- ❌ 不做下载进度 UI、不决定资源在哪个 Group——那是编辑器/构建侧的事。
- ❌ 不在内部缓存"用完不还"的资源（预加载是一种显式策略，不是偷偷缓存）。

### 3.2 与 roadmap、其他模块、VContainer 的关系

- roadmap §5.1 验收点：加载/释放正确性、**重复加载去重**、异步取消不泄漏 → 本文 §9 映射成可测项。
- 依赖方向（roadmap §4.2）：`Zipper.Core → Zipper.Pool → Zipper.Resources → {Audio, UI}`。Resources 可引 Pool 只用于**实例化辅助的归还联动**，反向引用禁止。
- **对象池联动语义（重要）**：池化的预制体，资源本体（prefab 资产）要**长期保活**——由池/持有方持有一个 `PrefabAsset`（内含未释放的 Addressables 句柄），克隆体进出普通对象池即可（§7 F3）。**不要把每个克隆体都当成一次 Addressables 实例化**，否则池化就失去了意义。
- **VContainer**：管理器注册为 Scope 内单例；Scope 的取消令牌即全局取消源；Scope Dispose 触发 §7 F6 的兜底释放。
- **异步栈**：对外一律 UniTask（roadmap §4.1-3），由 UniTask.Addressables 的 `ToUniTask()` 桥接。

### 3.3 与工程现状对接

- `Assets/Zipper/Resources/` 已有 `Zipper.Resources.asmdef`，M1 需确认 References 含 **四个**程序集：`UniTask`（核心）、`Unity.Addressables`、`Unity.ResourceManager`、`UniTask.Addressables`——前两者为官方包程序集；后两者随 UPM `com.cysharp.unitask@2.5.11` 提供（核心在包 `Runtime/`，UniTask.Addressables 在包 `Runtime/External/Addressables/`）。asmdef 引用**不传递**，缺一个编译期即报类型不可见（CS0012/CS0246），必须显式加齐。
- 现有 `Zipper.Pool.ZObjectPool<T>`（要求 `T : Component, IZObjectPoolItem`）构造需 `GameObject prefab`——`PrefabAsset.Prefab` 正好喂给它（§7 E1 示例）。
- UI 面板预制体、音频 Clip 都走本管理器加载（它们各自的 asmdef 依赖 Resources）。

---

## 4. 地址与键：框架只认 Addressables 原生 key

> **边界（v0.2 关键修订）**：`Zipper.Resources` **不内置任何业务资源清单**（不做 AssetKey 枚举/登记表/类型注册表）。公共 API 只收 Addressables 原生 key：**address（字符串）** 与 **label（字符串）**。资源地址在编辑器勾选 Addressable 时就定死了，它是最终契约。
> **为什么**：清单一旦进框架，你每加一个自己的资源就要改 `Zipper.Resources` 程序集并重新导包——框架被业务绑架。正确形态是框架只给"加载能力"，你的工程自己决定"有哪些地址"。
> **编辑器校验工具**（遍历地址清单查 Addressable 标记）属 Editor 程序集（roadmap 的 Zipper.Editor），禁止写进 Resources 运行时程序集（Editor API 编译期即需解析，运行时 asmdef 引 Editor 会编译失败）。

### 4.1 v1 用法：直接写地址

```csharp
// 加载一个音频：T 决定返回类型
var h = await res.LoadAsync<AudioClip>("audio/sfx_click", ct);
_audio.PlayOneShot(h.Asset);
res.Release(h);
```

- 地址 = 你在 Inspector 给资源填的 Addressable 地址（`game/enemy_basic`、`audio/sfx_click`……）。
- 泛型 `T` 提供返回类型安全；**地址本身的正确性**由 Addressables 保证（加载不存在/未标记的地址会抛 `InvalidKeyException`，编辑器下 Addressables Groups 窗口自带地址检索与校验，构建期也会报 catalog 错误）。
- label 用于批量（预加载、按标签取一组），框架提供 `LoadByLabelAsync`/`PreloadByLabelAsync`。

### 4.2 治理建议：你工程里的"键表"（可选，不进框架）

地址拼错是运行时才炸的，想提前拦就把地址收敛成你工程自己的常量（放 `Assembly-CSharp` 或未来 `Zipper.Game`，**不是框架程序集**）：

```csharp
// 你的工程代码（Assets/Script/…），不是 Zipper.Resources！
public static class ResKeys
{
    public const string EnemyBasic   = "game/enemy_basic";    // 预制体
    public const string UIPanelSet   = "ui/panel_setting";    // 预制体
    public const string SfxClick     = "audio/sfx_click";     // AudioClip
    public const string BgmMain      = "audio/bgm_main";      // AudioClip
}
```

- 新增资源 = Addressables 里打标填地址 + 这里加一行 **const**（或按需只写字符串字面量，也合法）。
- 只依赖 Unity 的常量类即可让拼写错误在编译期暴露；将来想要更强治理（枚举 + 类型绑定 + 一键校验），见 §4.3 演进。

### 4.3 演进方向（v1 之后，现在不做）：解析器

若以后遇到"地址满天飞、重构资源名要全局搜"的痛点，再给框架加**键解析器**能力，形态预想：

```csharp
// 未来演进（不在 v1）：业务键对象 → address/label
public interface IAssetKeyResolver
{
    bool TryResolve(object key, out string address);   // key 可以是你的枚举/AssetKey 壳
}
// LoadAsync<T> 收 object key，先经解析器转 address，再走 Addressables
```

触发条件：业务键复用频繁（UI/音频都引同一批键）、需要 Editor 遍历校验、或要按枚举做配置表。**单人毕设 v1 用 §4.2 常量类足够**，不要提前造。

### 4.4 与 AssetReference 的分工（两条路都要支持）

| 方式 | 适用场景 | 说明 |
|---|---|---|
| address / label（框架主推） | 代码里动态加载：UI 面板、敌人、音频等 | 管理器统一簿记/取消/兜底 |
| `AssetReference`（Unity 原生字段） | 美术/策划在 Inspector 就地拖拽引用 | 是 UnityEngine 序列化字段，**不经本管理器**，直接用 Addressables API 加载 |

两者不冲突：address 是"代码找资源"，AssetReference 是"Inspector 拖资源"。管理器只认 address/label。

---

## 5. 公共 API 一览

> 以下为设计面（签名 + 语义）。命名是建议值，M1 实施可微调；全部方法在 Scope 内单例 `ZResourceManager` 上。key 一律为 **Addressable 地址字符串**，label 用法单独列出。

| 成员 | 签名（伪代码） | 语义 |
|---|---|---|
| 初始化 | `UniTask InitializeAsync(CancellationToken ct)` | 幂等；首次 await 时初始化 Addressables Catalog |
| 加载资产 | `UniTask<AssetHandle<T>> LoadAsync<T>(string address, CancellationToken ct = default)` | 校验参数 → 加载 → 簿记，返回**句柄凭证**（`.Asset` 取资源），用毕 `Release(handle)`（§7 F2） |
| 释放 | `void Release<T>(AssetHandle<T> handle)` | 还凭证：内部即 `handle.Dispose()`（销号 + 计数-1，幂等），不提供第二条释放路径 |
| 加载并长期持有 | `UniTask<PrefabAsset> LoadPrefabAsync(string address, CancellationToken ct = default)` | 供对象池持有；Dispose 才真正释放 |
| 一次性实例化 | `UniTask<GameObject> InstantiateAsync(string address, Transform parent, CancellationToken ct = default)` | 内部经 Addressables.InstantiateAsync；默认 trackHandle 自动管理计数（见 §7 F4 取舍） |
| 预加载 | `UniTask PreloadAsync(IEnumerable<string> addresses, CancellationToken ct = default)` | 逐个加载并**立即释放**，达到"资源进内存/Bundle 就位"的预热目的 |
| 按 label 批量 | `UniTask PreloadByLabelAsync(string label, CancellationToken ct = default)` | Addressables 原生按 label 批量（后续可加 `LoadByLabelAsync`） |
| 统计/诊断 | `IReadOnlyList<AssetHandle> TrackedHandles`、`int TrackedCount`、事件 `OnLoadFailed(string address, Exception)` | 簿记只读面 |
| 兜底清理 | `void Dispose()` | 释放全部未还句柄并逐个告警（Scope 关闭时由 VContainer 调用） |

> 有意**不提供**同步加载入口（同步 `WaitForCompletion` 只在近程资源安全，见 §8 雷区 8），避免新手拿它卡死主线程。

---

## 6. 内部结构

### 6.1 类与职责

```
ZResourceManager（单例，由 VContainer 管理生命周期）
 ├─ AsyncLazy<bool> _initLazy          —— 初始化只跑一次（UniTask 的 AsyncLazy）
 ├─ HashSet<AssetHandleBase> _handles  —— 簿记台账（引用相等）：登记 Add / 注销 Remove / 遍历点名。
 │     AssetHandleBase 自带 Address/Owner → 不需要"地址→句柄组"字典；
 │     按地址聚合（统计/日志归并）用 foreach + GroupBy(h => h.Address) 即可。
 │     备选：若需 O(1) 实时"某地址持有者数"，用 Dictionary<string, HashSet<AssetHandleBase>>，
 │     但释放单句柄要 TryGetValue→set.Remove(handle)→空组删键，勿用 Remove(key, out …)。
 ├─ 簿记操作（纯内存、无锁主线程）      —— Book() / Unbook() / TrackedHandles
 └─ 统一出口                            —— LoadAsync/Release/LoadPrefabAsync/...（§5）
        │
        ▼ 持有（包装，不复制计数）
AssetHandleBase（abstract，与 AssetHandle<T> 同命名空间）：{ string Address; string Owner;
                               bool IsReleased; abstract void Dispose(); 多态释放 }
AssetHandle<T> : AssetHandleBase：{ + T Asset; internal 构造（防伪造句柄）;
                               Dispose() = 销号 + 内层 Release（幂等，唯一释放语义） }
PrefabAsset   ：{ GameObject Prefab(持有内层句柄); Instantiate(parent);
                  Dispose() → 释放内层句柄 }
```

### 6.2 簿记表为什么"只记账、不计数"（新人最常问）

**问**：Load 两次同一地址，簿记表能不能记成一个条目，Release 一次就当两次还了？
**答**：**不能。** Addressables 在底层对这两次 Load 各 +1 计数，各有各的句柄、各自对应一次真实 Release。若封装层再"合并记账、自计数"，就出现**两本账**：一次漏同步就永久泄漏或重复释放（`InvalidHandleException`）。两本账永远不会一致，这是此类封装最常见的 bug 来源。

所以本设计的铁律是：

> **计数权完全属于 Addressables；簿记表只是"谁还没还"的台账**，用途仅三样：统计、泄漏告警（Dispose 时逐个点名）、给 `TrackedHandles` 提供诊断数据。

对应的使用纪律是**句柄即凭证**：每个 `LoadAsync` 拿到一个 `AssetHandle<T>`，`Release` 时把它还回来。封装层不提供"按地址一键释放"（那会撞上别人还在用的句柄）。

### 6.3 簿记以"句柄"为单位，地址随句柄携带

Addressables 计数与释放的对象是"地址背后的资源"：同一地址可能同时存在多个句柄（各自独立计数，§6.2）。簿记以**句柄**为单位（`HashSet<AssetHandleBase>`，默认引用相等），`AssetHandleBase.Address` 记录来源地址——统计某资源持有数、Dispose 点名告警都按 `h.Address` 归并（`GroupBy`），**无需为地址单独建字典，也就不用纠结 List/Dictionary 的 out 取值问题**。

---

## 7. 核心流程伪代码

> 约定：`Cysharp.Threading.Tasks` = UniTask；`ToUniTask()` 来自 UniTask.Addressables；句柄包装为简化示意。以下为**设计级伪代码**，非最终实现。

### F1 初始化（幂等，随 Scope 启动）

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;

public sealed partial class ZResourceManager : System.IDisposable
{
    // AsyncLazy：多个调用方同时触发的首帧初始化只执行一次
    readonly AsyncLazy<bool> _initLazy;

    public ZResourceManager()
    {
        _initLazy = new AsyncLazy<bool>(InitializeCoreAsync);
    }

    public async UniTask InitializeAsync(CancellationToken ct)
        => await _initLazy.Task.AttachExternalCancellation(ct);

    static async UniTask<bool> InitializeCoreAsync()
    {
        // Addressables.InitializeAsync 加载 Catalog；内部幂等，可安全重复
        await Addressables.InitializeAsync().ToUniTask();
        // 此处可扩展：按需预下载远程内容、上报初始化耗时
        return true;
    }
}
```

**调用时机（InitializeAsync 在哪被调用）**：`InitializeAsync` 是 `ZResourceManager` 自身的方法——`LifetimeScope` 并没有同名回调。它应在游戏启动阶段被 await 一次，推荐由组装层（Zipper.DI / 未来 Zipper.Runtime）写一个 VContainer 异步入口点，Scope 启动时自动触发：

```csharp
// 组装层入口：IAsyncStartable 在 VContainer.Unity 命名空间（官方 async 生命周期）
public sealed class ResourcesBootstrapper : IAsyncStartable
{
    readonly ZResourceManager _res;
    public ResourcesBootstrapper(ZResourceManager res) => _res = res;

    // IAsyncStartable.StartAsync 返回类型是条件编译别名 Awaitable，取决于工程组合：
    //   装了 UPM 包 com.cysharp.unitask → UniTask（VCONTAINER_UNITASK_INTEGRATION 宏生效）← 本工程（2.5.11 UPM）走这里
    //   Unity 2023.1+（未装 UPM UniTask） → UnityEngine.Awaitable
    //   其他（如退回 vendor 源码版）      → System.Threading.Tasks.Task
    // 方法体只 await UniTask，返回类型不影响语义；工程组合变化时只改签名即可。
    public async UniTask StartAsync(CancellationToken cancellation = default)
        => await _res.InitializeAsync(cancellation);   // AsyncLazy：整个生命周期只真正初始化一次
}

// GameLifetimeScope.Configure 内注册：
//   builder.Register<ZResourceManager>(Lifetime.Singleton).As<IZResourceManager>();
//   builder.RegisterEntryPoint<ResourcesBootstrapper>(Lifetime.Singleton);
```

> **不调用也不会崩**：Addressables 1.x 在首次 Load 前会自动初始化 Catalog；显式 `InitializeAsync` 只是把初始化时机前移到受控阶段（可先做预下载/预热、统计耗时），并避免首个业务 Load 顺带触发初始化。若不想让 Resources 层耦合 VContainer，也可让 `ZResourceManager` 直接实现 `IAsyncStartable`（代价是 Resources asmdef 需补引 VContainer.Unity，取舍列入 §10 待办）。

### F2 加载与释放核心（簿记的主战场）

```csharp
public async UniTask<AssetHandle<T>> LoadAsync<T>(string address, CancellationToken ct = default)
{
    // 0) 参数校验：空地址尽早失败（不做业务键表，地址即契约）
    if (string.IsNullOrEmpty(address))
        throw new System.ArgumentNullException(nameof(address));

    var inner = Addressables.LoadAssetAsync<T>(address);

    // 1) 等待完成；失败/取消时不留下簿记残渣
    try
    {
        await inner.ToUniTask(ct);              // UniTask.Addressables 扩展
        var handle = new AssetHandle<T>(address, owner: "<caller>",
                                        h => _handles.Remove(h), inner);   // 注入销号回调（引用相等）
        _handles.Add(handle);                   // 成功才入账
        return handle;                          // 返回句柄凭证，业务自行保管
    }
    catch (System.OperationCanceledException)
    {
        Addressables.Release(inner);            // 取消 = 没借成，计数要退掉
        throw;
    }
    catch (System.Exception e)
    {
        Addressables.Release(inner);
        OnLoadFailed?.Invoke(address, e);       // 事件：统一失败上报（可接日志/弹窗）
        throw;
    }
}

// 释放 = 还凭证：唯一释放语义在 AssetHandle.Dispose（销号 + 计数-1），此处仅转发（幂等）
public void Release<T>(AssetHandle<T> handle) => handle?.Dispose();

// ---- AssetHandle 包装（句柄凭证）----
// 说明：正式实现中 public sealed class AssetHandle<T> : AssetHandleBase（§6.1 非泛型基类，
// 承载 Address/Owner/IsReleased 与 abstract Dispose，供 _handles 统一收纳不同 T 的句柄——零 object/零转型）。
// 下方按单类示意（: IDisposable 可视为 AssetHandleBase 的等价表述，Dispose 覆写自基类）。
// internal 说明（回答"方法能否用 internal"）：internal = 同一程序集（Zipper.Resources）内可见。
// 本类是 public，但构造标 internal → 业务无法伪造句柄，只能由管理器创建；
// 释放对外只暴露 public Dispose() 一条路，不存在第二条释放路径，杜绝 res.Release / Dispose 语义分叉。
public sealed class AssetHandle<T> : System.IDisposable
{
    public string Address { get; }                     // 簿记键（§6.3）
    public string Owner { get; }                       // 谁借的，泄漏告警点名用
    public T Asset { get; }                            // 加载完成的资源本体
    public bool IsReleased { get; private set; }

    readonly UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> _inner;
    readonly System.Action<AssetHandle<T>> _unbook;    // 簿记销号回调（管理器注入）

    internal AssetHandle(string address, string owner, System.Action<AssetHandle<T>> unbook,
        UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> inner)
    {
        Address = address; Owner = owner;
        Asset = inner.Result;                          // 在 await 完成后才构造，Result 已就绪
        _inner = inner;
        _unbook = unbook;
    }

    public void Dispose()                              // 唯一释放语义，幂等
    {
        if (IsReleased) return;                        // 防双 Release
        IsReleased = true;
        _unbook?.Invoke(this);                         // 台账销号
        UnityEngine.AddressableAssets.Addressables.Release(_inner);  // 计数-1
    }
}
```

要点回顾（新人重点）：**取消与失败路径都必须 Release**（否则计数泄漏）；成功路径才入账；`Release` 幂等防双还。调用方**持句柄即持凭证**：

```csharp
// 用法示意：Load 拿凭证 → .Asset 取资源 → 用毕 Release 还凭证
var h = await res.LoadAsync<AudioClip>(ResKeys.SfxClick, ct);   // 地址常量来自你的工程（§4.2）
_audio.PlayOneShot(h.Asset);        // 直接用资源本体
// …用完…
res.Release(h);                     // 还凭证（内部即 h.Dispose()：销号 + 计数-1）
```

### F3 预制体加载 + 对象池联动（池化复用主路径）

池化语义（§3.2）：**prefab 资产长期保活，克隆体进出普通对象池，不碰 Addressables 计数**。

```csharp
// 长期持有的预制体资产：给对象池当“母本”
public sealed class PrefabAsset : System.IDisposable
{
    public string Address { get; }
    public GameObject Prefab { get; }
    readonly AssetHandle<GameObject> _handle;   // 持有期间不 Release

    internal PrefabAsset(string address, AssetHandle<GameObject> handle)
    { Address = address; Prefab = handle.Asset; _handle = handle; }

    public GameObject Instantiate(Transform parent = null)
        => UnityEngine.Object.Instantiate(Prefab, parent); // 纯克隆，不走 Addressables

    public void Dispose() => _handle.Release();  // 池销毁时才真正释放资源本体
}

public async UniTask<PrefabAsset> LoadPrefabAsync(string address, CancellationToken ct = default)
{
    // 复用 F2：拿到句柄凭证，句柄整体交给 PrefabAsset 长期持有（Dispose 时才 Release）
    var handle = await LoadAsync<GameObject>(address, ct);
    return new PrefabAsset(address, handle);
}
```

配套使用示例（E1）：

```csharp
// 敌人波次管理：资源本体加载一次，实例全部来自对象池
// 前置：EnemyView（预制体上的业务组件）必须实现 Zipper.Pool.IZObjectPoolItem；
//       ZObjectPool 不代管显隐 → EnemyView.OnGet() 里 SetActive(true)、OnReturn() 里 SetActive(false)。
//       PrefabAsset 不需要（也不应）实现池接口——被池化的是克隆体组件，不是母本持有者。
public sealed class EnemySpawner : System.IDisposable
{
    readonly ZResourceManager _res;
    readonly Zipper.Pool.ZObjectPool<EnemyView> _pool;   // 现有 Zipper.Pool
    PrefabAsset _prefabAsset;

    public async UniTask SetupAsync(ZResourceManager res, string enemyAddress, CancellationToken ct)
    {
        _res = res;
        _prefabAsset = await res.LoadPrefabAsync(enemyAddress, ct);  // 1) 保活母本
        _pool = new Zipper.Pool.ZObjectPool<EnemyView>(_prefabAsset.Prefab,
                                                       initialSize: 8, maxSize: 32); // 2) 喂给现有池
    }

    public EnemyView Spawn() => _pool.GetItem();        // 3) 借（池内 clone，无 Addressables 开销）
    public void Despawn(EnemyView v) => v.ReturnToPool?.Invoke(v); // 4) 还

    public void Dispose()
    {
        _pool.Clear();                                  // 5a) 先清池：Destroy 全部克隆体（不影响母本句柄）
        _prefabAsset.Dispose();                         // 5b) 再释放母本（Addressables 计数归零）
        // 顺序不可颠倒：母本句柄释放后其 AssetBundle 可能被卸载，
        // 若池内实例仍引用该 bundle 的子资源（材质/贴图）会 Missing；再扩容 Instantiate 母本也会失败。
    }
}
```

> 若某对象**不打算池化**、用一次就销毁，则直接 `InstantiateAsync`（F4），**不要**先 LoadPrefab 再克隆——那样母本句柄会一直悬着。

### F4 一次性实例化（用后即焚）

```csharp
public async UniTask<GameObject> InstantiateAsync(string address, Transform parent = null,
                                                  CancellationToken ct = default)
{
    // Addressables.InstantiateAsync：内部自带句柄跟踪(trackHandle)，计数由原生管理
    var handle = UnityEngine.AddressableAssets.Addressables.InstantiateAsync(
        address, parent, false, trackHandle: true);
    try
    {
        return await handle.ToUniTask(ct);   // 返回实例；计数+1 由原生记账
    }
    catch
    {
        Addressables.Release(handle);        // 失败退计数
        throw;
    }
}

// 配套：销毁一次性实例（销毁 + 计数-1）
public void ReleaseInstance(GameObject instance)
    => UnityEngine.AddressableAssets.Addressables.ReleaseInstance(instance);
```

> **取舍说明**：`InstantiateAsync` 的实例由 Addressables 记账，销毁必须走 `ReleaseInstance`（普通 `Destroy` 会让计数泄漏）。而 F3 池化路径的克隆体是普通 GameObject（不走原生计数），用 `Destroy` 或池归还皆可——**两条路径的释放纪律不同，使用前先想清楚走哪条**。

### F5 预加载 / 预热

```csharp
// 按地址列表预热：全部加载进内存后立即释放（留温度，不留占用）
public async UniTask PreloadAsync(IEnumerable<string> addresses, CancellationToken ct = default)
{
    foreach (var address in addresses)
    {
        ct.ThrowIfCancellationRequested();
        // 预加载不关心具体类型，直接以 UnityEngine.Object 加载后立即释放
        var handle = Addressables.LoadAssetAsync<UnityEngine.Object>(address);
        try { await handle.ToUniTask(ct); }
        catch { Addressables.Release(handle); throw; }
        Addressables.Release(handle);     // 预热的本质：进内存/Bundle 就位后立刻还掉
    }
}

// 按 label 批量预热（Addressables 原生支持，适合"常用资源池"）
public async UniTask PreloadByLabelAsync(string label, CancellationToken ct = default)
{
    var handle = Addressables.LoadAssetsAsync<UnityEngine.Object>(
        new System.Collections.Generic.List<string> { label }, null, Addressables.MergeMode.Intersection);
    try { await handle.ToUniTask(ct); }
    finally { Addressables.Release(handle); }
}
```

> 预热解决的是"首帧卡顿"：把 UI 常驻面板、高频特效在切场景/加载界面时提前下载解压好；真正使用仍走 F2 正常加载。

### F6 Dispose 兜底清理（Scope 关闭）

```csharp
public void Dispose()
{
    // 台账上还没还的句柄 = 使用方漏还：逐个点名告警后统一释放，防泄漏兜底
    if (_handles.Count > 0)
    {
        var leaked = _handles.ToArray();        // 快照：Dispose 触发 _unbook 会移除自身，不能边遍历边删
        _handles.Clear();
        foreach (var h in leaked)
        {
            UnityEngine.Debug.LogWarning(
                $"[ZResource] 资源 {h.Address} 的句柄未释放（持有者 {h.Owner}），已由管理器兜底释放");
            h.Dispose();          // 唯一释放语义：销号 + 计数释放（幂等）
        }
    }
    _initLazy = null;  // 释放后禁止再加载
    // 注：若 Addressables 全局内容还需整体清理，属 M1 决策（见 §10），此处不做
}
```

---

## 8. 新手雷区自查（对照清单）

| # | 雷区 | 后果 | 本设计/纪律如何规避 |
|---|---|---|---|
| 1 | Load 了不 Release | 资源泄漏、内存只增不减 | 簿记 + Dispose 兜底点名告警（F6） |
| 2 | 同一 handle Release 两次 | `InvalidHandleException` / 误释放他人计数 | `Release` 幂等（F2） |
| 3 | 对 `InstantiateAsync` 的实例用普通 `Destroy` | 计数泄漏（实例不销毁资源） | 必须 `ReleaseInstance`（F4） |
| 4 | 地址拼错 / 忘标记 Addressable | 运行时 `InvalidKeyException` | 业务侧常量类集中地址（§4.2）+ Addressables Groups 校验；将来解析器可更强（§4.3） |
| 5 | 取消时不 Release | 取消即泄漏 | F2 catch 路径补 Release |
| 6 | 封装层自造引用计数 | 两本账，迟早对不上 | 计数只信 Addressables（§6.2） |
| 7 | 把每个池化克隆体当 Addressables 实例化 | 计数爆炸、池化失效 | F3 母本保活 + 普通池（E1） |
| 8 | 用 `WaitForCompletion` 同步等远程资源 | 主线程卡死 | 管理器不开放同步入口 |
| 9 | 共享资源未标 Addressable，被多 Group 引用 | 打进多份 Bundle，资源重复/两份实例 | 编辑器依赖分析 + 共享资源标 Addressable（构建侧） |
| 10 | 场景里 Addressable 场景外的对象直接引用 Addressable 场景内资源 | 场景加载顺序/释放顺序踩坑 | 场景资源生命周期 M1 专项设计（§10 待办） |

> 调试利器：Addressables **Event Viewer** 窗口（Window → Asset Management → Event Viewer）可实时看每个资源的加载/释放与引用计数，怀疑泄漏先开它。

---

## 9. 验收标准与测试点（对接 roadmap §5.1）

M1 实施后，以下每条都要有 EditMode/PlayMode 证据：

| roadmap 验收点 | 测试场景 | 断言 |
|---|---|---|
| 加载正确性 | 对已标记的地址各 Load 一次 | 返回类型正确、资产非空 |
| 释放正确性 | Load 后 Release，再 Load | 簿记清空、无异常 |
| **重复加载去重** | 并发 10 次 `LoadAsync` 同一地址 | 底层仅一次实际加载（Event Viewer/诊断计数），10 次各自 Release 后计数归零无泄漏 |
| 双 Release 安全 | 同一 handle Release 两次 | 第二次静默幂等，无异常 |
| 取消不泄漏 | Load 中途取消（TokenSource 提前 Cancel） | 无残留簿记、计数归零、无泄漏日志 |
| 失败路径 | Load 一个未标记/不存在的地址 | `OnLoadFailed` 触发、无句柄残留 |
| 空参数拦截 | `LoadAsync<T>("")` / `null` | 立即 `ArgumentNullException`（F2 第 0 步） |
| 池化路径 | E1 场景：借 20 还 20 | 活跃计数正确、Destroy 后 Event Viewer 无泄漏 |
| 预加载 | 预热 10 个地址后 | 内存中资源就位、句柄台账为空 |
| Dispose 兜底 | 故意不 Release 后 Dispose | 逐条告警日志 + 全部句柄释放 |

---

## 10. 开放问题与 M1 待办

| 项 | 内容 | 建议 |
|---|---|---|
| asmdef 引用 | `Zipper.Resources.asmdef` References 含 **四个**程序集：① `UniTask`（核心，随 `com.cysharp.unitask@2.5.11`）② `Unity.Addressables` ③ `Unity.ResourceManager`（官方包）④ `UniTask.Addressables`（同包 `Runtime/External/Addressables/`）。asmdef 引用**不传递**，缺一个编译期即报类型不可见（CS0012/CS0246） | M1 实施第 1 步 |
| **UniTask 形态决策** | 已改 **UPM `com.cysharp.unitask`（git 源）**，与 roadmap §6 的"vendor 随包分发"策略冲突（UPM/git 依赖进不了 .unitypackage；且 UPM 版点亮了 VContainer 的 `VCONTAINER_UNITASK_INTEGRATION` 宏） | 分发给第三方前必须决策：回 vendor 还是修订 roadmap 分发策略 |
| 场景资源生命周期 | Addressables 场景加载（`LoadSceneAsync`）与场景切换时"整场资源卸载策略" | 专项小设计，跟随 M1 |
| Addressables 工程设置 | 编辑器安装包、Settings/Group 初始化、构建脚本、Play Mode Script（开发期选 Use Asset Database） | 随 M1 落地，写入配置文档 |
| Owner 标记 | F2 中 `Owner` 暂为占位符 `"<caller>"`，是否引入更友好诊断上下文 | 可在簿记需求明确后定 |
| 解析器演进 | v1 后按需加 `IAssetKeyResolver`（业务键对象 → address，§4.3） | 单人阶段用常量类（§4.2），暂不实现 |
| 同步入口 | 是否提供受限同步加载（`WaitForCompletion`）给启动必经路径 | 默认不开放，出现明确需求再议 |
| 预下载 | 远程内容 `GetDownloadSizeAsync` / `DownloadDependenciesAsync` 接入点 | 热更需求出现时再加 |

---

## 11. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.2 | 2026-09 | 键体系边界修订（框架只收 address/label，业务清单移出框架，解析器列演进）；UniTask 改 UPM 2.5.11 后同步 §2.5/§3.3/§7 F1/§10 |
| v0.1 | 2026-09 | 初稿：Addressables 速成 + 设计思路 + 伪代码 F1–F6/E1 + 雷区 + 验收 |

> 审批：本文件为设计草稿，不产生代码变更；据其进入 M1 编码前需按协调者规范另行审批。
