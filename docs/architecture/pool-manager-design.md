# Zipper 对象池管理器设计（ZObjectPoolManager）

> 状态：**v0.1 草稿，待审阅**
> 定位：`docs/planning/technical-roadmap.md` §5.4.1「普通对象池」的展开设计稿，同时是使用者 `IZObjectPoolManager` 初稿的**评审定稿**。**只给设计思路与接口形态，不含实现代码**。
> **实施归属**：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：`docs/architecture/resource-manager-design.md`（§7 F3 母本与池化）、`docs/architecture/core-design.md`（命名/日志）、`docs/standards/naming-convention.md`
> 变更记录：v0.1 初稿（基于使用者初稿 + 评审结论）。

---

## 0. 这份文档怎么读

| 读者 | 建议路径 |
|---|---|
| 要立刻改接口的人 | §3 寻址模型 → §4 设计要素 → §9 差异清单 |
| 要把实现写出来的人 | §2 边界 → §5 内部结构 → §6 流程 → §7 雷区 |
| 要验收的人 | §8 验收标准 |

---

## 1. 为什么要池管理器

| 价值 | 说明 |
|---|---|
| **统一托管** | 调用方不再自己 `new` 池、自己管母本、自己记着清理 |
| **隐藏构造** | `ZObjectPool` 构造已定为 `internal`（✅ 已实施）——只有管理器能建池，避免"到处 new 池、配置不一致" |
| **封住释放顺序纪律（核心价值）** | 母本由管理器托管后，"**先清池 → 再释放母本**"变成管理器内部实现，**调用方没有机会搞错**（这条纪律在资源管理器设计稿 §7 F3 里是重点） |
| **统一生命周期** | Scope 关闭时一次 `Dispose()` 销毁全部池 + 释放全部托管母本，避免"某个池忘了清" |
| **集中统计** | 每个池的活跃/空闲/总数可统一查询（答辩演示、性能观察都用得上） |
| **按 address 建池** | 业务侧只给地址，不碰 `PrefabAsset`，与资源管理器自然衔接 |

> **为什么"管理器抽接口"与"池本身不抽接口"不矛盾**：池只有一种实现、且调用方是直接使用者（YAGNI，抽了只多一层）；而管理器是**跨模块服务**——要被 UI/音频/业务共用、要注册进容器、要能被替换或 mock，接口是它的正常形态。

---

## 2. 边界

**管什么**：
1. 普通池（`ZObjectPool<T>`）的创建 / 销毁 / 获取 / 归还转发 / 统计
2. **母本的托管与释放**（按 address 创建时）
3. 生命周期（Scope 关闭统一清理）

**不管什么**：
- ❌ **ECS 对象池**——roadmap §5.4.2 已定"两池各自独立、接口不强行统一"；将来若需要，另设管理器，不要塞进这里
- ❌ 实例的**显隐/重置**——那是组件实现 `IZObjectPoolItem` 的职责（`OnGet`/`OnReturn` 里自己 `SetActive`）
- ❌ "按时间自动回收""LRU 驱逐"等高级策略——YAGNI，需要时再加
- ❌ 线程调度——池只允许**主线程**使用（Unity 对象约束）

---

## 3. 寻址模型（v1 定案：一类型一池）

### 3.1 初稿的问题：两套寻址混用

使用者初稿里：

| 方法 | 用什么找池 |
|---|---|
| `CreatePool<T>(GameObject prefab, …)` | `T` + `prefab` |
| `DestroyPool(GameObject prefab)` / `ClearPool(GameObject prefab)` | **仅 prefab** |
| `Get<T>()` / `GetItemsByCount<T>(…)` | **仅 T** |

→ 当**同一个 `T` 对应多个 prefab**（精英兵/普通兵都挂 `EnemyView`）时，`Get<EnemyView>()` 该返回哪个池？行为不可预期。

### 3.2 定案：**全程用 `T` 寻址**

| 决定 | 理由 |
|---|---|
| 一个类型 `T` ↔ 一个池（**一类型一池**） | 与使用者初稿注释一致，够用且简单 |
| `DestroyPool<T>()` / `ClearPool<T>()` 改为泛型 | 消除"prefab 与 T 混用"的歧义 |
| `prefab` 只在 `CreatePool` 出现（作为池的来源） | prefab 是"输入材料"，不是"池的身份" |
| **同一 `T` 重复 `CreatePool<T>` → 抛异常**（不静默覆盖） | 静默覆盖会**丢失旧池**：旧池的实例散落在场景里、无人归还、也不会被 Destroy → 泄漏 + 幽灵对象 |
| 接口注释写明"**仅主线程**、**一类型一池**" | 约束前置，避免误用 |

### 3.3 演进路径（v2 再议，现在不做）

若真需要"一类型多池"（如"精英兵池/普通兵池"共用 `EnemyView`），必须引入**显式 key**：

```
CreatePool<T>(PoolKey key, …)     // key 可用枚举/字符串，而不是继续拿 prefab 当身份
Get<T>(PoolKey key = default)
```

届时是**破坏性变更**（所有调用点要带 key），所以 v2 一次性做完，别半途混用两种寻址。

---

## 4. 设计要素（接口形态）

> 形态建议，命名与拆分由实现者定；`ZPoolStats` 是统计结构（活跃/空闲/总数）。

| 成员 | 形态 | 语义 / 备注 |
|---|---|---|
| 创建（同步） | `void CreatePool<T>(in ZPoolOptions<T> options)` | options 里含 prefab / initialSize / maxSize / 四个委托 / 超限策略；**参数收敛成配置对象**，以后加字段不改签名 |
| 创建（异步） | `UniTask CreatePoolAsync<T>(string address, in ZPoolOptions<T> options)` | 内部：加载母本（`PrefabAsset`）→ 建池 → 登记**托管母本**；业务侧不碰 `PrefabAsset` |
| 销毁池 | `void DestroyPool<T>()` | 语义：**先清池（销毁全部实例）→ 再释放母本（仅托管时）→ 移除登记** |
| 清空池 | `void ClearPool<T>()` | 只销毁实例，**保留池与母本**（下次还能借） |
| 获取 | `T Get<T>()` | 未注册池 → 抛**信息明确**的异常（含类型名 + "请先 CreatePool"） |
| 批量获取 | `List<T> GetItemsByCount<T>(int count)` / `void GetItemsByCount<T>(int count, List<T> result)` | 保留你现有的两个重载 |
| 统计 | `bool TryGetStats<T>(out ZPoolStats stats)` | 未注册返回 false（不抛异常，查询类接口不该炸） |
| 全部清理 | `void Dispose()` | Scope 关闭：**遍历销毁全部池 + 释放全部托管母本** |
| 主线程约定 | 注释声明 | 所有成员仅主线程调用 |

**两个必须收敛的细节**：

1. **委托类型上移**：初稿签名里出现了 `ZObjectPool<T>.InitializeActionDelegate` 这类**嵌套在具体类里的委托类型** → 等于让"接口依赖具体类"。应把委托类型提升为独立类型（如 `ZPoolAction<T>`），或直接放进 `ZPoolOptions<T>` 内部，接口只依赖 options。
2. **超限策略要可配**（你代码里的 TODO 正是这个）：`ZObjectPool` 现在 maxSize 到顶就 `throw`——"太激进"（你的原话）。建议在 options 里给 `ZPoolOverflowPolicy { Throw, ReturnNull, Expand }`：
   - `Throw`：保持现状（调用方必须处理）
   - `ReturnNull`：调用方做降级（如"这一帧不再生成敌人"）
   - `Expand`：忽略 maxSize 继续建（真正需要"软上限"时用）
   - 建议**默认值由你定**（倾向 `Throw` 保持显式，或 `ReturnNull` 更友好）——见 §10。

---

## 5. 内部结构（思路级）

```
ZObjectPoolManager（Scope 单例，主线程独占）
 ├─ Dictionary<Type, PoolEntry> _pools        —— T → （池 + 母本登记 + 是否托管）
 ├─ 统一出口：Create / CreateAsync / Destroy / Clear / Get / Stats / Dispose
 └─ 母本登记：PoolEntry 里含 PrefabAsset（仅"按 address 创建"时非空）

PoolEntry = { ZObjectPoolBase Pool; PrefabAsset HostedPrefab; bool OwnsPrefab }
```

**存储为什么要非泛型基类**：不同 `T` 的池类型不同（`ZObjectPool<EnemyView>`、`ZObjectPool<Bullet>`），要放进同一个字典只有两条路：

| 方案 | 评价 |
|---|---|
| `Dictionary<Type, object>` + 取出时 `(ZObjectPool<T>)` 转型 | ⚠️ 能跑但丢类型安全；`typeof(T)` 精确匹配（用接口/基类查不到）；`Dispose` 遍历时要二次判断 |
| **池加非泛型基类/基接口**（承载 `Count/Clear/Dispose/统计` 等与 `T` 无关的操作），字典存基类 | ✅ **推荐**：容器级 `Dispose` 能干净遍历；与 `AssetHandleBase` 同一手法（你已熟悉） |

**托管 vs 非托管母本（必须在设计里分开）**：

| 创建方式 | 母本归属 | `DestroyPool` 行为 |
|---|---|---|
| `CreatePoolAsync<T>(address, …)` | **管理器托管** | 先清池 → **释放母本** → 移除登记 |
| `CreatePool<T>(prefab, …)`（外部传入的 prefab 引用） | **调用方所有** | 只清池 → **不动那个 prefab**（可能别人还在用 / 它是场景对象） |

> 混淆这两者会出现两种事故：**误释放别人的资源**（bundle 卸载 → 别处 Missing）或**该释放的没释放**（母本句柄悬着 → 泄漏）。

---

## 6. 关键流程（思路级伪代码）

#### F1 同步创建（外部 prefab）

```
CreatePool<T>(options)：
  1) 已存在 T 的池 → 抛异常（一类型一池，不静默覆盖）
  2) 校验 options（prefab 非空、尺寸合法）——沿用你池内已有的健壮性检查风格
  3) new ZObjectPool<T>(options...)（构造 internal，故必须在管理器内）
  4) 登记 _pools[typeof(T)] = { Pool, HostedPrefab = null, OwnsPrefab = false }
```

#### F2 异步创建（按 address，托管母本）

```
CreatePoolAsync<T>(address, options)：
  1) 已存在 → 抛异常
  2) 母本 = await 资源管理器.LoadPrefabAsync(address)   ← 业务侧不接触 PrefabAsset
  3) 用 母本.Prefab 建池（配置来自 options）
  4) 登记 { Pool, HostedPrefab = 母本, OwnsPrefab = true }
  ⚠ 失败路径：建池抛异常 → 立即释放已加载的母本，别留悬空句柄
```

#### F3 获取 / 归还

```
Get<T>()：
  取 _pools[typeof(T)]；没有 → 抛"未注册池：<T>，请先 CreatePool/CreatePoolAsync"
  → 转发给池.GetItem()

归还：由池在构造实例时注入的 ReturnToPool 委托完成（组件自己还），管理器不参与
  （若将来想统一"归还即隐藏"，那是池/组件的职责，不是管理器的）
```

#### F4 销毁池

```
DestroyPool<T>()：
  1) 取出 entry；不存在 → 抛异常或静默返回（建议：静默返回 + 一条 Warn 日志，销毁类接口不该炸）
  2) entry.Pool.Clear()                      ← 先清池：销毁全部克隆体
  3) 若 entry.OwnsPrefab → entry.HostedPrefab.Dispose()   ← 再释放母本
  4) 移除登记
  ⚠ 顺序不可颠倒（资源管理器 §7 F3 的纪律）
```

#### F5 管理器 Dispose（Scope 关闭）

```
Dispose()：
  遍历所有 entry → 逐个执行 F4 的 2)~3)（先清池、再释放托管母本）→ 清空字典
  追加一条 Warn：若仍有"活跃实例未归还"，点名类型与数量（防幽灵对象）
```

#### F6 统计

```
TryGetStats<T>(out stats)：
  有池 → stats = { 活跃/空闲/总数 } 返回 true；无池 → false（不抛）
```

---

## 7. 雷区（对照清单）

| # | 雷区 | 后果 | 规避 |
|---|---|---|---|
| 1 | prefab 与 `T` 两套寻址混用 | `Get<T>` 行为不可预期 | §3 全程用 `T` |
| 2 | `Dictionary<Type, object>` + 盲目转型 | 转型异常 / 丢类型安全 | 用非泛型基类（§5） |
| 3 | 托管/非托管母本混淆 | 误释放别人资源，或母本泄漏 | `OwnsPrefab` 标志 + 两套销毁语义（§5） |
| 4 | 重复 `CreatePool<T>` 静默覆盖 | 旧池实例变孤儿（无人归还、不被销毁） | 抛异常（§3.2） |
| 5 | 忘记 `Dispose()` | Scope 关闭后池与母本全泄漏 | 容器 `Dispose` 统一清理 |
| 6 | 跨线程使用池 | Unity 对象跨线程未定义行为 | 注释声明 + 仅主线程 |
| 7 | `initialSize` 设太大 | 首帧卡顿/内存尖峰 | 按需预热；大池考虑分帧预热 |
| 8 | 母本先释放、池后清 | 实例子资源 Missing、扩容失败 | 管理器内部固定顺序（§6 F4） |
| 9 | 用管理器塞 ECS 池 | 职责混淆、接口被拉扯 | §2 边界：各自独立 |
| 10 | 同 `T` 用不同 prefab（绕过一类型一池） | 拿到"另一种对象"的实例，逻辑错乱 | 约束写进注释；需要就上 PoolKey（§3.3） |

---

## 8. 验收标准与测试点

| 验收点 | 场景 | 断言 |
|---|---|---|
| 创建/获取/归还 | 创建池 → 借 20 → 还 20 | 活跃/空闲计数正确，无异常 |
| 未注册池 | 未创建就 `Get<T>()` | 抛异常且**信息包含类型名与提示** |
| 重复创建 | 对同一 `T` 调两次 `CreatePool` | 第二次抛异常（不覆盖旧池） |
| 销毁池（托管） | `CreatePoolAsync` 后 `DestroyPool<T>` | 实例全部销毁 → 母本计数归零（Event Viewer 无泄漏） |
| 销毁池（非托管） | `CreatePool(prefab)` 后 `DestroyPool<T>` | 实例销毁，但**外部 prefab 不受影响** |
| Dispose | 建 3 个池后 `Dispose()` | 全部池销毁、托管母本全释放、字典清空 |
| 统计 | 借还后 `TryGetStats<T>` | 数值正确；未注册返回 false |
| 压力 | 借 1000 还 1000，再 `Dispose` | 无泄漏、无残留实例 |
| 容量策略 | maxSize 到顶，按策略配置 | `Throw` 抛异常 / `ReturnNull` 返回空 / `Expand` 继续建（按你选定的默认值） |

---

## 9. 与现有代码的差异清单（需你改的地方）

> 只列**差异**，不给实现代码。

| 位置 | 差异 |
|---|---|
| `IZObjectPoolManager.cs` | ① `DestroyPool`/`ClearPool` 去掉 `prefab` 参数 → 改 `DestroyPool<T>()`；② `CreatePool` 参数收敛为 `ZPoolOptions<T>`；③ **委托类型上移**（不再引用 `ZObjectPool<T>.XXXDelegate`）；④ 新增 `CreatePoolAsync<T>(address, …)`、`TryGetStats<T>`、`Dispose`；⑤ 注释写明"仅主线程 / 一类型一池 / 重复创建抛异常" |
| `ZObjectPool.cs` | ① 新增非泛型基类/基接口（承载 `Count/Clear/Dispose/统计`）；② 超限行为策略化（替换你标 TODO 的 `throw`）；③ 构造 `internal` ✅ 已做 |
| `ZObjectPoolManager.cs` | 实现上述接口 + `PoolEntry` 登记（含 `OwnsPrefab`）+ 固定销毁顺序 |
| `Zipper.Pool.asmdef` | 若池管理器要"按 address 建池"，则**要引用 `Zipper.Resources`**（`PrefabAsset`/`IZResourceManager`）——这会打破 roadmap §4.2 里 `Resources → Pool` 的既有方向吗？见 §10 待决项 |
| `docs/architecture/resource-manager-design.md` | §7 F3 补一句"母本推荐由对象池管理器托管（见本文）" ✅ 本次已同步 |
| `GameLifetimeScope` | 注册 `IZObjectPoolManager → ZObjectPoolManager`（Singleton） |

---

## 10. 开放问题与待决项

| 项 | 内容 | 建议 |
|---|---|---|
| **依赖方向（重要）** | 若池管理器要按 address 建池，`Zipper.Pool` 需引用 `Zipper.Resources`；而 roadmap §4.2 写的是 `Core → Pool → Resources`（Resources 在 Pool 之后） | 两个选择：**(a)** 接受反转为 `Pool → Resources`（资源是更底层的能力）；**(b)** 把"按 address 建池"挪到上层（如 `Zipper.Runtime` 或 UI/业务层），池管理器只吃 prefab。**需要你拍板** |
| 超限策略默认值 | `Throw` / `ReturnNull` / `Expand` | 倾向 `ReturnNull`（业务可降级）或 `Throw`（显式）；你定 |
| 重复创建行为 | 抛异常 or 返回既有池 | 建议抛异常（防静默丢失旧池） |
| 是否需要 `TryCreatePool`（避免异常控制流） | 有些团队不喜欢用异常表达"已存在" | 可加 `bool TryCreatePool<T>(…)`；按你的偏好 |
| 一类型多池（PoolKey） | 何时需要 | v2 再议，避免半途混用两套寻址 |
| 池统计是否广播事件 | 走事件总线 | YAGNI，暂不做 |
| ECS 池 | 是否也做管理器 | roadmap 定各自独立；将来单独设计 |

---

## 11. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.1 | 2026-09-10 | 初稿：基于使用者 `IZObjectPoolManager` 初稿的评审定稿——确定"一类型一池 / 全程用 T 寻址"；指出寻址混用、泛型存储、接口耦合具体类委托、托管母本等关键点；给出接口形态、内部结构、六条关键流程、十条雷区、验收标准与差异清单 |

> 审批：本文件为设计草稿，不含代码实现；由使用者据其自行实现，AI 不代写代码（见 `docs/standards/agent-role.md`）。
