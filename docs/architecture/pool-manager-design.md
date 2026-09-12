# Zipper 对象池管理器设计（ZObjectPoolManager）

> 状态：**v0.2 草稿，待审阅**
> 定位：`docs/planning/technical-roadmap.md` §5.4.1「普通对象池」的展开设计稿，同时是使用者 `IZObjectPoolManager` 初稿的**评审定稿**。**只给设计思路与接口形态，不含实现代码**。
> **实施归属**：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：`docs/architecture/resource-manager-design.md`（§7 F3 母本与池化）、`docs/architecture/core-design.md`、`docs/standards/naming-convention.md`
> 变更记录：v0.1 初稿；**v0.2（使用者纠正）**——撤销"`CreatePoolAsync(address)` + 管理器托管母本"的设计：**池管理器只接收 `GameObject prefab`，不碰资源句柄、不依赖 `Zipper.Resources`**；"按 address 建池 + 母本托管"改由**上层组合器**承担（§5.3）。

---

## 0. 这份文档怎么读

| 读者 | 建议路径 |
|---|---|
| 要立刻改接口的人 | §3 寻址模型 → §4 设计要素 → §9 差异清单 |
| 要把实现写出来的人 | §2 边界 → §5 内部结构（含 §5.3 组合器）→ §6 流程 → §7 雷区 |
| 要验收的人 | §8 验收标准 |

---

## 1. 为什么要池管理器

| 价值 | 说明 |
|---|---|
| **统一托管** | 调用方不再自己 `new` 池、自己记着清理——池的创建/销毁/获取都走一个入口 |
| **隐藏构造** | `ZObjectPool` 构造已定为 `internal`（✅ 已实施）——只有管理器能建池，避免"到处 new 池、配置不一致" |
| **集中生命周期** | Scope 关闭时一次 `Dispose()` 销毁全部池（实例全部回收），不留"忘了清的池" |
| **集中统计** | 每个池的活跃/空闲/总数可统一查询（答辩演示、性能观察都用得上） |
| **为上层组合留好接缝** | 管理器只做"给 prefab 就建池"这一件事；"按 address 建池 + 母本托管"由上层的**组合器**（同时依赖 Pool 与 Resources）拼接——见 §5.3 |

> **边界一句话**：池管理器**只认 `GameObject`**。它不需要知道 `PrefabAsset`、更不需要引用 `Zipper.Resources`——"资源怎么来的"是别人的事。

> **为什么"管理器抽接口"与"池本身不抽接口"不矛盾**：池只有一种实现、调用方是直接使用者（YAGNI，抽了只多一层）；而管理器是**跨模块服务**——要被 UI/音频/业务共用、要注册进容器、要能被替换或 mock，接口是它的正常形态。

---

## 2. 边界

**管什么**：
1. 普通池（`ZObjectPool<T>`）的创建 / 销毁 / 获取 / 归还转发 / 统计
2. 池内实例的统一回收（`Clear` / `Dispose`）

**不管什么**：
- ❌ **资源句柄**：母本的加载与释放不在这里（见 §5.3 组合器）——池管理器只接收 `GameObject prefab`
- ❌ **ECS 对象池**——roadmap §5.4.2 已定"两池各自独立"，将来若需要另设管理器
- ❌ 实例的**显隐/重置**——组件实现 `IZObjectPoolItem` 的职责（`OnGet`/`OnReturn` 里自己 `SetActive`）
- ❌ "按时间自动回收""LRU 驱逐"等高级策略——YAGNI
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
| 一个类型 `T` ↔ 一个池（**一类型一池**） | 与初稿注释一致，够用且简单 |
| `DestroyPool<T>()` / `ClearPool<T>()` 改为泛型 | 消除"prefab 与 T 混用"的歧义 |
| `prefab` 只在 `CreatePool` 出现（作为池的来源） | prefab 是"输入材料"，不是"池的身份" |
| **同一 `T` 重复 `CreatePool<T>` → 抛异常**（不静默覆盖） | 静默覆盖会**丢失旧池**：旧实例散落场景、无人归还、也不会被销毁 → 泄漏 + 幽灵对象 |
| 注释写明"**仅主线程**、**一类型一池**、**只接收 GameObject（不持有资源句柄）**" | 约束前置 |

### 3.3 演进路径（v2 再议，现在不做）

若真需要"一类型多池"（精英兵池/普通兵池共用 `EnemyView`），必须引入**显式 key**：

```
CreatePool<T>(PoolKey key, …)     // key 用枚举/字符串，而不是继续拿 prefab 当身份
Get<T>(PoolKey key = default)
```

届时是**破坏性变更**（所有调用点带 key），一次性做完，别半途混用两种寻址。

---

## 4. 设计要素（接口形态）

> 形态建议，命名与拆分由实现者定；`ZPoolStats` 是统计结构（活跃/空闲/总数）。

| 成员 | 形态 | 语义 / 备注 |
|---|---|---|
| 创建池 | `void CreatePool<T>(in ZPoolOptions<T> options)` | options 内含 `GameObject prefab` / initialSize / maxSize / 四个委托 / 超限策略。**参数收敛成配置对象**，以后加字段不改签名 |
| 销毁池 | `void DestroyPool<T>()` | 语义：**先清池（销毁全部实例）→ 移除登记**。**不涉及任何资源句柄** |
| 清空池 | `void ClearPool<T>()` | 只销毁实例，**保留池**（下次还能借） |
| 获取 | `T Get<T>()` | 未注册池 → 抛**信息明确**的异常（含类型名 + "请先 CreatePool"） |
| 批量获取 | `List<T> GetItemsByCount<T>(int count)` / `void GetItemsByCount<T>(int count, List<T> result)` | 保留现有两个重载 |
| 统计 | `bool TryGetStats<T>(out ZPoolStats stats)` | 未注册返回 false（查询类接口不炸） |
| 全部清理 | `void Dispose()` | Scope 关闭：遍历销毁全部池（实例回收）+ 清空登记 |
| 主线程约定 | 注释声明 | 所有成员仅主线程调用 |

**两个必须收敛的细节**：

1. **委托类型上移**：初稿签名里出现了 `ZObjectPool<T>.InitializeActionDelegate` 这类**嵌套在具体类里的委托类型** → 等于让"接口依赖具体类"。应提升为独立类型（如 `ZPoolAction<T>`）或放进 `ZPoolOptions<T>`，接口只依赖 options。
2. **超限策略要可配**（对应你代码里的 TODO）：现在 maxSize 到顶就 `throw`——"太激进"（你的原话）。建议 `ZPoolOverflowPolicy { Throw, ReturnNull, Expand }`：`Throw` 保持现状 / `ReturnNull` 让调用方降级（这一帧不再生成）/ `Expand` 忽略 maxSize。**默认值待你定**（§10）。

---

## 5. 内部结构（思路级）

```
ZObjectPoolManager（Scope 单例，主线程独占）
 ├─ Dictionary<Type, PoolEntry> _pools
 │     PoolEntry = { ZObjectPoolBase Pool; GameObject Prefab; }
 └─ 统一出口：Create / Destroy / Clear / Get / TryGetStats / Dispose
```

### 5.1 存储为什么要非泛型基类

不同 `T` 的池类型不同（`ZObjectPool<EnemyView>`、`ZObjectPool<Bullet>`），要放进同一个字典只有两条路：

| 方案 | 评价 |
|---|---|
| `Dictionary<Type, object>` + 取出时 `(ZObjectPool<T>)` 转型 | ⚠️ 能跑但丢类型安全；`typeof(T)` 精确匹配（用接口/基类查不到）；`Dispose` 遍历要二次判断 |
| **池加非泛型基类/基接口**（承载 `Count`/`Clear`/`Dispose`/统计等与 `T` 无关的操作），字典存基类 | ✅ **推荐**：容器级 `Dispose` 能干净遍历；与 `AssetHandleBase` 同一手法（你已熟悉） |

### 5.2 池管理器**不持有**资源句柄（v0.2 明确）

管理器里**没有** `PrefabAsset`、没有 `OwnsPrefab` 标志——它只记着"这个池是用哪个 `GameObject` 建的"（用于诊断/重建提示）。因此：

- `DestroyPool<T>()` 只清池、不释放任何资源
- 池管理器**不依赖** `Zipper.Resources`（asmdef 无需加引用）
- 资源什么时候加载、什么时候释放，完全由**上层组合器**掌握

### 5.3 按 address 建池 + 母本托管：放上层组合器（v0.2 新增）

"从 Addressables 加载母本 → 建池 → 销毁时先清池再释放母本"这组编排，放在**同时依赖两者**的地方（`Zipper.Runtime` 的服务，或业务侧的薄封装）：

```
组合器（依赖 Pool + Resources，落在上层）
  CreateAsync<T>(address, options):
      母本 = await 资源管理器.LoadPrefabAsync(address, owner)
      池管理器.CreatePool<T>(options with prefab = 母本.Prefab)   ← 只传 GameObject
      _hosted[typeof(T)] = 母本                                  ← 母本登记在组合器里

  Destroy<T>():
      池管理器.DestroyPool<T>()          ← 先清池（销毁全部实例）
      _hosted[typeof(T)].Dispose()       ← 再释放母本句柄
      _hosted.Remove(typeof(T))
```

要点：
- 顺序纪律（**先清池 → 再释放母本**，见资源管理器设计稿 §7 F3）由**组合器**保证，池管理器不必知道
- 依赖方向保持单向：`Core ← Pool`、`Core ← Resources`，组合器落在上层（依赖两者），**没有循环依赖**
- 若多个业务点都要"按 address 建池"，就把组合器做成 **Runtime 层的一个服务**，避免每处各写一遍（顺序写错就出事故）
- 组合器自身也应支持"统一销毁全部托管的母本"（Scope 关闭时先清池再释放）

---

## 6. 关键流程（思路级伪代码）

#### F1 创建池（只吃 prefab）

```
CreatePool<T>(options)：
  1) 已存在 T 的池 → 抛异常（一类型一池，不静默覆盖）
  2) 校验 options（prefab 非空、尺寸合法）——沿用池内已有的健壮性检查风格
  3) new ZObjectPool<T>(options...)（构造 internal，故必须在管理器内创建）
  4) 登记 _pools[typeof(T)] = { Pool, Prefab = options.Prefab }
```

#### F2 获取 / 归还

```
Get<T>()：
  取 _pools[typeof(T)]；没有 → 抛"未注册池：<T>，请先 CreatePool"
  → 转发给池.GetItem()

归还：由池在构造实例时注入的 ReturnToPool 委托完成（组件自己还），管理器不参与
```

#### F3 销毁池

```
DestroyPool<T>()：
  1) 取出 entry；不存在 → 静默返回 + 一条 Warn 日志（销毁类接口不该炸）
  2) entry.Pool.Clear()      ← 销毁全部克隆体
  3) 移除登记
  （母本释放不在这里 —— 由上层组合器负责，见 §5.3）
```

#### F4 管理器 Dispose（Scope 关闭）

```
Dispose()：
  遍历所有 entry → 逐个 Clear() → 清空字典
  追加一条 Warn：若仍有"活跃实例未归还"，点名类型与数量（防幽灵对象）
  （托管母本由组合器释放）
```

#### F5 统计

```
TryGetStats<T>(out stats)：
  有池 → stats = { 活跃/空闲/总数 } 返回 true；无池 → false（不抛）
```

---

## 7. 雷区（对照清单）

| # | 雷区 | 后果 | 规避 |
|---|---|---|---|
| 1 | prefab 与 `T` 两套寻址混用 | `Get<T>` 行为不可预期 | §3 全程用 `T` |
| 2 | `Dictionary<Type, object>` + 盲目转型 | 转型异常 / 丢类型安全 | 用非泛型基类（§5.1） |
| 3 | **组合器里漏掉"先清池再释放母本"** | 实例子资源 Missing、扩容失败 | 顺序纪律收敛到组合器（§5.3），别散落在业务代码 |
| 4 | 重复 `CreatePool<T>` 静默覆盖 | 旧池实例变孤儿 | 抛异常（§3.2） |
| 5 | 忘记 `Dispose()` | Scope 关闭后池与实例泄漏 | 容器 `Dispose` 统一清理 |
| 6 | 跨线程使用池 | Unity 对象跨线程未定义行为 | 注释声明 + 仅主线程 |
| 7 | `initialSize` 设太大 | 首帧卡顿/内存尖峰 | 按需预热；大池考虑分帧预热 |
| 8 | 让池管理器去管资源（v0.1 的错法） | Pool↔Resources 互相牵扯、循环依赖风险 | 池只认 GameObject（§5.2） |
| 9 | 用管理器塞 ECS 池 | 职责混淆 | §2 边界：各自独立 |
| 10 | 同 `T` 用不同 prefab（绕过一类型一池） | 拿到"另一种对象"的实例，逻辑错乱 | 约束写进注释；需要就上 PoolKey（§3.3） |

---

## 8. 验收标准与测试点

| 验收点 | 场景 | 断言 |
|---|---|---|
| 创建/获取/归还 | 创建池 → 借 20 → 还 20 | 活跃/空闲计数正确，无异常 |
| 未注册池 | 未创建就 `Get<T>()` | 抛异常且**信息含类型名与提示** |
| 重复创建 | 同一 `T` 调两次 `CreatePool` | 第二次抛异常（不覆盖旧池） |
| 销毁池 | `CreatePool` 后 `DestroyPool<T>` | 实例全部销毁、登记移除；**传入的 prefab 对象不受影响** |
| Dispose | 建 3 个池后 `Dispose()` | 全部池销毁、字典清空、场景中无残留实例 |
| 统计 | 借还后 `TryGetStats<T>` | 数值正确；未注册返回 false |
| 压力 | 借 1000 还 1000，再 `Dispose` | 无泄漏、无残留实例 |
| 容量策略 | maxSize 到顶，按策略配置 | `Throw` 抛异常 / `ReturnNull` 返回空 / `Expand` 继续建（按你选定的默认值） |
| **组合器顺序**（上层） | 组合器 Destroy 后 | 池内实例先销毁 → 母本计数归零（Event Viewer 无泄漏） |

---

## 9. 与现有代码的差异清单（需你改的地方）

> 只列**差异**，不给实现代码。

| 位置 | 差异 |
|---|---|
| `IZObjectPoolManager.cs` | ① `DestroyPool`/`ClearPool` 去掉 `prefab` 参数 → `DestroyPool<T>()`/`ClearPool<T>()`；② `CreatePool` 参数收敛为 `ZPoolOptions<T>`；③ **委托类型上移**（不再引用 `ZObjectPool<T>.XXXDelegate`）；④ 新增 `TryGetStats<T>`、`Dispose`；⑤ **不提供** `CreatePoolAsync(address)`（按 address 建池放上层组合器）；⑥ 注释写明"仅主线程 / 一类型一池 / 只接收 GameObject、不持有资源句柄 / 重复创建抛异常" |
| `ZObjectPool.cs` | ① 新增非泛型基类/基接口（承载 `Count`/`Clear`/`Dispose`/统计）；② 超限行为策略化（替换你标 TODO 的 `throw`）；③ 构造 `internal` ✅ 已做 |
| `ZObjectPoolManager.cs` | 实现接口 + `PoolEntry { Pool, Prefab }` 登记 + 固定销毁流程（清池 → 移除登记） |
| `Zipper.Pool.asmdef` | **无需改动**：不引用 `Zipper.Resources`（池只认 `GameObject`）✅ |
| 上层组合器（新增，位置待定） | `Zipper.Runtime` 的服务或业务侧薄封装：`加载母本 → 建池 → 销毁时先清池再释放母本`（§5.3） |
| `docs/architecture/resource-manager-design.md` | §7 F3 的托管建议改为"由**上层组合器**负责" ✅ 本次已同步 |
| `GameLifetimeScope` | 注册 `IZObjectPoolManager → ZObjectPoolManager`（Singleton） |

---

## 10. 开放问题与待决项

| 项 | 内容 | 建议 |
|---|---|---|
| 超限策略默认值 | `Throw` / `ReturnNull` / `Expand` | 倾向 `ReturnNull`（业务可降级）或 `Throw`（显式）；你定 |
| 重复创建行为 | 抛异常 or 返回既有池 | 建议抛异常（防静默丢失旧池） |
| 是否需要 `TryCreatePool` | 有些团队不喜欢用异常表达"已存在" | 可加 `bool TryCreatePool<T>(…)`；按你的偏好 |
| 组合器落点 | Runtime 服务 vs 业务侧封装 | 若多处需要 → 放 Runtime 做统一服务，顺序纪律只实现一次 |
| 一类型多池（PoolKey） | 何时需要 | v2 再议 |
| 池统计是否广播事件 | 走事件总线 | YAGNI，暂不做 |
| ECS 池 | 是否也做管理器 | roadmap 定各自独立；将来单独设计 |

---

## 11. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.2 | 2026-09-10 | **使用者纠正**：撤销"`CreatePoolAsync(address)` + 池管理器托管母本"设计——**池管理器只接收 `GameObject prefab`，不碰资源句柄、不依赖 `Zipper.Resources`**；新增 §5.3「按 address 建池 + 母本托管放上层组合器」（含伪代码与顺序纪律归属）；§1/§2/§4/§5/§6/§7/§8/§9/§10 全面改写；删除"依赖方向待决"（已定：Pool 与 Resources 互不依赖） |
| v0.1 | 2026-09-10 | 初稿：基于使用者 `IZObjectPoolManager` 初稿的评审定稿——一类型一池 / 全程用 `T` 寻址 / 泛型存储 / 接口收敛 / 十条雷区 / 验收与差异清单 |

> 审批：本文件为设计草稿，不含代码实现；由使用者据其自行实现，AI 不代写代码（见 `docs/standards/agent-role.md`）。
