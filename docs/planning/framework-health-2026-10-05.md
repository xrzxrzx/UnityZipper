# 框架体检报告 · 2026-10-05（非 UI 模块：Core / Pool / Resources / Audio）

> 依据：`zipper-test`（独立验证规程）§三 证据分级 / §四 定级与放行 / §五 报告模板
> 验证者：AI（独立于作者）　范围提出者：使用者　日期：2026-10-05
> **版本基线**：`8809808`（工作区干净 ✓，无并发修改 → 隔离手法①固定基线）
> **测试数据**：无真实/敏感数据；harness 为构造数据，日志落在 `%TEMP%`
> **本次范围**：**非 UI 模块**。UI 自本次起**封存暂停开发**（见 §6），不在体检范围内
> ⚠️ **本报告不构成"可放行"结论**：存在 6 条 P1（§4），按 `zipper-test` §4.2 判定为 **打回**

---

## 0. 本次要回答的三个问题

1. UI 封存 → 其余模块还有没有问题？（→ §4）
2. 拿得出什么级别的证据？（→ §1，L1 实测 4 项 + 静态审查 3 模块）
3. 没验到的是什么？（→ §3，**显式列出**，不许沉默跳过）

---

## 1. 跑过的命令与原始输出（**强制章节**）

### 1.1 编译验证（L1 —— 只证明"能编过"，不证明运行时行为）

```powershell
foreach ($p in @('Zipper.Core','Zipper.Pool','Zipper.Resources','Zipper.Audio')) { dotnet build "$p.csproj" -v:m --nologo }
```
输出（逐模块）：
```
########## Zipper.Core ##########      -> Temp\bin\Debug\Zipper.Core.dll        已成功生成。 0 个警告 0 个错误
########## Zipper.Pool ##########      -> Temp\bin\Debug\Zipper.Pool.dll        已成功生成。 0 个警告 0 个错误
########## Zipper.Resources ########## -> Temp\bin\Debug\Zipper.Resources.dll   已成功生成。 0 个警告 0 个错误
########## Zipper.Audio ##########     -> Temp\bin\Debug\Zipper.Audio.dll       已成功生成。 0 个警告 0 个错误
```
> 产物落在 `Temp\bin\Debug\`（Unity 生成的 csproj 自带 `OutputPath`）→ **不污染仓库** ✓
> 对照：上一轮 UI MVP 验收（`ui-mvp-acceptance.md` §1.1）同样是 5 个模块 0 错误，本次非 UI 四模块复现一致 ✓

### 1.2 运行时 harness（**L1 —— 本报告唯一的"真跑"证据**）

**做法**：`Assets/Zipper/Core/{Events,Logging}` 里除 `ZLoggerBootstrapper` / `ZMainThreadDispatcherDriver`（依赖 UnityEngine/UniTask）以外的源码**全是纯 C#** → 在工程外的 `%TEMP%\zipper-harness\` 建临时控制台工程，把真实源码编进 exe 直接跑（UnityEngine 仅做 `Object` / `Debug` 最小 stub）。

```powershell
dotnet run --project %TEMP%\zipper-harness\harness.csproj -v:q --nologo
```
**输出（末尾汇总与全部 FAIL 原样照录）**：
```
  PASS  A1 订阅后发布能收到事件（且载荷正确）
  PASS  A2 多个订阅者都收到
  PASS  A3 未订阅类型发布不抛异常
  FAIL  A4【缺陷复现】契约(§3.3 / §9-4)要求：回调内 Unsubscribe(别人) 本轮立即生效 → B 本轮不应被调用
        <<< 实际本轮被调用 1 次（契约要求 0 次）
  PASS  A4 发布中退订他人：下一轮不再收到
  PASS  A5 回调内 Dispose 自己 → 只收一次
  PASS  A6 Unsubscribe 首次 true、再次 false / 退订失败有 Warning
  PASS  A7 凭据 Dispose 后不再收到 / 订阅数归零
  PASS  A8 UnsubscribeAll(owner) 只摘该 owner / UnsubscribeAll(null) 不抛
  PASS  A9 Subscribe(null) 抛 ArgumentNullException 且记 Error
  PASS  A10 handler 抛异常被隔离，后续 handler 仍执行
  PASS  A11 非主线程 Publish 被拒（无异常、无投递、记 Error）
  PASS  A12 Dispose 后 Publish 不投递 / Subscribe 返回空凭据 / 计数清空
  PASS  A13 GetState 聚合：2 个事件类型 / 3 个订阅
  PASS  A14 5000 订阅 / 全部 Dispose 后归零
  PASS  B1 全局级别门槛（Debug 丢 / Info 通）/ IsEnable / SetGlobalLevel
  PASS  B2 sink 级门槛独立生效
  PASS  B3 类名分支格式： l1=[18:30:02] [MyClass:128 | Setup]  hello
  PASS  B3 异常消息进消息体： l2=[18:30:02] [MyClass:128 | Setup] bad oops
  PASS  B4 路径分支截断： l=[18:30:02] [Assets/Zipper/Core/A.cs:7 | Tick]  m
  PASS  B5 主线程 sink：非主线程调用先入队、Pump 后才写入
  PASS  B6 一个 sink 抛异常不影响后续 sink
  PASS  B7 ZLogger 未 Attach：不抛且 IsEnabled=false
  FAIL  B9【缺陷复现】Pump(256) 应只抽 256 条（限量出队）  <<< 实际一帧抽干 1000 条（契约要求 256）
  FAIL  B10 FileSink 真落盘：3 条写入后文件含 3 行且格式正确
        <<< lines=1  first=[18:30:59] [FS:1 | Case]  line0[18:30:59] [FS:2 | Case]  line1[18:30:59] [FS:3 | Case]  line2
  === 结果：30 passed / 3 failed ===
```
> **3 条 FAIL 全是缺陷复现**（不是 harness 写错）：断言按**设计稿契约**写，实现不满足 → FAIL 即证据。
> harness 位置：`%TEMP%\zipper-harness\`（工程外，用完可删；`Assets/` 内一行未改）。

### 1.3 独立佐证（工程外的真实产物）

`%LocalAppData%Low\DefaultCompany\Component Developer\ZipperLogs\zipper-20261005.log`：**970 字节 / 共 1 行**，4 条日志首尾相接无换行 —— 与 B10 的 FAIL **互相印证**（不是 harness 的假象）✓

### 1.4 静态审查（**L4 —— 声明降级，不能支撑"通过"**）

| 模块 | 谁审 | 我是否复核 |
|---|---|---|
| Core（Logging/Events/Boot） | 独立子代理静态审查 | 关键 3 条**逐条读源码复核** ✓ |
| Pool + Fakes | 独立子代理静态审查 | **逐条复核，否决 2 条**（§5）✓ |
| Audio + 4 个测试类 | 独立子代理静态审查 | 最高优先那条（BGM Pause NRE）**读源码复核** ✓ |
| Resources | 独立子代理静态审查 | 最高优先那条（`Dispose` 无"已关闭"守卫）**读源码复核** ✓ |

---

## 2. 逐条核对结果（历史遗留项）

| 历史项（来自 `docs/memory/`） | 结论 | 证据 |
|---|---|---|
| 池 `GetItemsByCount` 返回 null List → NRE | ✅ **已修** | `ZObjectPoolManager.cs:105` 返回 `new List<T>(0)` |
| 池 `Return<T>` 未防 `ReturnToPool` 为 null | ✅ **已修** | `ZObjectPoolManager.cs:134-140` + 池侧 `:73-81` |
| 池 `Get<T>()` 无日志 | ✅ **已修** | `ZObjectPoolManager.cs:151` 记 Error |
| 事件总线 `Remove` 返回移除个数 / `UnsubscribeAll(null)` 记 Warning 不抛 | ✅ **已实现** | `EventChannel.cs:27-46`、`ZEventBus.cs:103-107` + harness A6/A8 |
| 音频 `_bgm?.Stop` 假 null 守卫 | ✅ **已修** | `ZAudioManager.cs:187` |
| 音频"淡出未实现" | ⚠️ **只修了一半** | SFX 侧已实现（`ZAudioManager.cs:199-209`）；**BGM 侧 `fadeOutSeconds` 至今未被读取**（`AudioBusChannel.cs:133-141`） |
| 日志"按日期切分（跨零点写新文件）" | ❌ **未实现** | 全仓仅 `ZLoggerBootstrapper.cs:48-49` 一处算文件名，`FileSink._path` 只读、writer 只开一次 → §4 P1 |
| 日志"退出 / OnApplicationPause 必 flush" | ❌ **未接线** | 全仓 `OnApplicationQuit|OnApplicationPause|Application.quitting` **零命中** → §4 P2 |
| `Zipper.Tests` 覆盖情况 | ❌ **Core/Pool/Resources 零用例** | 8 个测试类共 53 用例：音频 29（4 类）+ 面板 24（3 类）+ `PanelTestBase`；**Logging/Events/Pool/Resources 一条都没有** |
| 曾报的 `InvalidKeyException`（UI/SyncPanel）是否属代码缺陷 | ✅ **非代码缺陷**（维持原判） | 真实日志里 `ZResourceManager.cs:73` 的"加载失败"行确实打印了 → 说明失败路径 `:72` 的 `Addressables.Release(inner)` **未抛异常**，清理是干净的（Resources 审查复核） |

---

## 3. 未覆盖项与原因（**不许沉默跳过**）

| # | 未覆盖 | 原因 | 影响 |
|---|---|---|---|
| 1 | **EditMode 测试（53 用例）未运行** | Unity 编辑器**正开着**；本工程历史上被"双 Unity 实例 + 包缓存污染"坑过 → 我**不启第二个实例** | 音频/面板用例"全绿"是 **L5 转述**（记忆里的旧结论），**不能作为本次通过依据** |
| 2 | 真机/运行时行为（Audio 播放、Addressables 加载、Mixer、DontDestroyOnLoad） | 同上，需 Unity 内运行 | 音频的听觉验收项（`audio-manager-design.md` §13）仍未逐条验收 |
| 3 | ~~Resources 模块~~（**已补齐**） | 初稿落笔时该路子代理尚未返回；定稿前已返回，P2 我读源码复核过 → 结论见 §1.4 / §4.2 P2-15 / §4.3 P3-7、P3-8 | 该模块**能放行**（无 P0/P1），仅 1 条 P2 + 2 条 P3 建议修 |
| 4 | FileSink 跨零点切分 / 退出刷盘 的实际丢行量 | 未实机验证；跨零点需改系统时间 | 只有静态结论（§4 P1/P2） |
| 5 | 池/音频的 Unity 运行时路径（Instantiate、Destroy、假 null 时序） | 无 Unity | 池与音频的 P1/P2 全为**静态路径推断**，未实测复现 |
| 6 | `FileSink` 运行期文件被独占（本次实测撞到） | 我试图读活文件时抛 `IOException: being used by another process` | 观察项：**运行期日志文件无法被其它进程读取**（可诊断性 P3） |

---

## 4. 缺陷清单

### 4.1 P1（6 条）

| # | 模块 | 缺陷 | 证据 | 证据级别 |
|---|---|---|---|---|
| **P1-1** | Events | **退订不立即生效**：回调内 `Unsubscribe(别人)` / `UnsubscribeAll(别人)` 摘掉的订阅，**本轮仍被调用一次**（只有"凭据 Dispose"路径会打 `IsDisposed`）。违反 `event-bus-design.md` §3.3 精确规则表 + §9 验收第 4/21 条（设计稿 §2 差异表第 2 条**预见了**这个坑："只做快照会出现'退订后本轮仍收到一次'的经典意外"） | 复现：harness A4 FAIL；代码：`EventChannel.cs:27-46`（两个 `Remove` 只 `FindAll` 重建数组，**未 `MarkAsDisposed`**）+ `:50-55`（遍历的是替换前的旧数组） | **L1** |
| **P1-2** | Logging | ✅ **已修（2026-10-05 批次 1）** —— 原缺陷：**日志文件所有条目粘成一行**：`LogFormatter.Format` 不带换行、`Commit` 用 `writer.Write(_buffer)`（只有"丢弃告警"用 `WriteLine`）→ 文件无法 `grep`/`tail` 逐条定位。修法：`FileSink.Write` 入队时补 `Environment.NewLine`（`LogFormatter` 与 `ConsoleSink` **未动**——Unity `Debug.Log` 自带换行） | 修前复现：harness B10 FAIL（`lines=1`）+ 真实 `zipper-20261005.log` = 970 B / **1 行**；修后回归：**B10 PASS（`lines=3`）**，Core 编译 0/0 | **L1** |
| **P1-3** | Logging | **`FileSink` 无按日期切分**：设计要求（`logging-design.md` 约束 #10、§7.2 四条流程、§10 验收"跨日期切分"）跨零点写新文件，实现里文件名与 writer 都只初始化一次 | 静态 + `ZLoggerBootstrapper.cs:48-49`、`FileSink.cs:41/59`；全仓仅一处算日期 | L4 |
| **P1-4** | Audio | **BGM 句柄 `Pause()` 必 NRE**：`_item` 为 null 的 BGM 句柄，`IsPlaying` 走 `_manager.IsBgmPlaying` 为 true → `_item.PauseSource()` 抛 NRE；而 `Resume()` **有** `_item == null` 守卫（不对称） | `ZAudioHandle.cs:32-33` / `:14` / `:38`（**我已逐行复核**） | L4（代码事实确定） |
| **P1-5** | Audio | **BGM 淡出仍未实现**：`Stop(1f)` 走硬切，`fadeOutSeconds` 从未被读取 | `AudioBusChannel.cs:133-141`、调用点 `ZAudioManager.cs:187` | L4 |
| **P1-6** | Audio | **音量启动不读回**：`EnsureVolumesLoaded` 只被 `GetVolume/SetVolume` 触发，初始化/`AttachRoot` 都不调 → 重启后 `PlayerPrefs` 里的音量不生效（除非恰好有人调过音量 API） | `ZAudioManager.cs:145-157`（唯一调用点 `:128`、`:137`）；设计稿 §7.3 要求"初始化时读回并 SetFloat，先于任何播放" | L4 |

### 4.2 P2（已复核，可排期）

| # | 模块 | 缺陷 | 证据 |
|---|---|---|---|
| P2-1 | Logging | `ZMainThreadDispatcher.Pump(maxPerFrame)` **形同虚设**：循环变量 `i` 从不自增 → **一帧抽干整个队列**（日志风暴时主线程卡帧） | 复现：harness B9 FAIL（1000/1000）；代码 `ZMainThreadDispatcher.cs:34-45`；**L1** |
| P2-2 | Logging | 退出 / `OnApplicationPause` 刷盘**完全没接线**（设计要求必 flush；移动端被杀 → 最多 500ms 未落盘的行丢失） | 全仓关键字零命中；`ZLoggerBootstrapper.cs:25-56` |
| P2-3 | Logging | `FileSink` 后台线程**一抛异常就永久退出**（`try` 包在 `while` 外）→ 之后只入队不落盘；`Dispose` 里 `Join(1000)` 超时后仍无条件 `_signal.Dispose()`（竞态） | `FileSink.cs:57-74`、`:111-116` |
| P2-4 | Logging | **输出前缀与文档 v1.5 不符**：文档要求"传了 `nameof` → 不输出行号"，实现无条件拼 `:行号`（时间戳也无毫秒、`\|` 两侧空两格；另：**异常为空时消息前会留两个空格**，因格式串写的是 `…] {exception} {message}`） | 实测 `[MyClass:128 \| Setup]  hello`（harness B3/B4）；`LogFormatter.cs:9-18` vs `logging-design.md:106-107`、`:386-387`。注：代码注释显示 2026-09-26 是**有意**改的 → 属**文档滞后**，建议改文档而非改代码 |
| P2-5 | Audio | 带 fade 的 `Stop()` 不校验句柄存活/池实例是否已被复用 → 会**淡掉别人的音效** | `ZAudioManager.cs:199`（对比无 fade 路径 `:192-196` 有 `IndexOf` 兜底） |
| P2-6 | Audio | 外部 ct 打断交叉淡入 → BGM 永久停在半音量，且仍返回"有效"句柄 | `AudioBusChannel.cs:93` 提前 return，跳过 `:108` 收尾赋值 |
| P2-7 | Audio | 句柄身份只认 `_item == null`（Unity **假 null**）→ 池被拆/清后池化句柄被误判为 BGM 句柄；旧 BGM 句柄再 `Stop()` 会切掉当前新曲 | `ZAudioManager.cs:185-187`、`ZAudioHandle.cs:16`、`ZObjectPool.cs:212` |
| P2-8 | Audio | `Dispose` 顺序与设计稿"强制顺序"相反（先 `ReleaseAll` 再停 BGM） | `ZAudioManager.cs:290-292` vs 设计稿 §5.2/§11（置信度中：取决于 Addressables 引用计数） |
| P2-9 | Audio | Mixer 缺失静默降级 → `SetVolume` 变空操作，**且被测试固化**（断言"不报错"） | `ZAudioBootstrapper.cs:64-65`、`ZAudioManager.cs:161-171`、`Tests/AudioVolumeTests.cs:137-142` vs 设计稿 v0.2.1 §7.1（"不做降级、直接失败"） |
| P2-10 | Pool | `NewItem` 对 `Instantiate/GetComponent` 结果零校验 → 组件缺失时 NRE，且**已实例化的克隆体无人回收**（泄漏到场景） | `ZObjectPool.cs:111-112`（注：`CreatePool` 建池时校验过组件，故触发需母本中途变更 → 我定级 **P2**，子代理原报 P1） |
| P2-11 | Pool | `NewItem` 里 `allItems.Add` / `totalCount++` 排在 `OnInitialize()` **之后** → 用户回调抛异常即**永久泄漏 + 计数漂移** | `ZObjectPool.cs:114-117` |
| P2-12 | Pool | 四处回调无异常保护：`OnReturn` 抛异常 → 实例"不在池也不在活跃表"，**永久丢失** | `ZObjectPool.cs:95-96`、`:114-115`、`:144-145`、`:205-206` |
| P2-13 | Pool | 已销毁实例（Unity 假 null）被外部 Destroy 后仍会被出队/归还 → `OnGet/OnReturn` 抛 MissingReference，计数虚高 | `ZObjectPool.cs:128-131`、`:144`、`:88-95`（`ClearItem` 有守卫，`ReturnItemToPool` 没有） |
| P2-14 | Pool | `CreatePool<T>(options)` 未校验 `options` 本身为 null → 直接 NRE（而非像其它非法参数那样记 Error 后 return） | `ZObjectPoolManager.cs:28-41`（`ZPoolOptions<T>` 是 class） |
| P2-15 | Resources | **兜底清理没有"已关闭"守卫**：`Dispose()` 快照→`Clear()`→逐个释放，但**没有 `_disposed` 状态标记**；而 `LoadAssetAsync` 的 `_book.Add(handle)` 排在 `await` **恢复之后** → Dispose 期间/之后落地的句柄被加进一个**再也不会被清理的台账**（Addressables 引用计数永久 +1、资源不卸载，**连告警都没有**）。设计 F6「清空台账；此后禁止再加载」未实现（`resource-manager-design.md:291`，子代理引用）；单 Scope 现状影响有限，迁多 Scope/多场景即升 P1 | `ZResourceManager.cs:93-102`（无 `_disposed`）、`:58-61`（`Add` 在 await 之后）——**我已读源码复核** ✓ |

### 4.3 P3（登记，不阻塞）

| # | 模块 | 缺陷 | 证据 |
|---|---|---|---|
| P3-1 | Logging | 队列溢出告警行格式**自成一派**（`[HH:mm:ss.fff] [Warn] [Zipper.Core] …`），与 `LogFormatter` 的两分支格式都不一致 | `FileSink.cs:96` |
| P3-2 | Logging | `ZLoggerBootstrapper` 无 `_disposed` 门、`Dispose` 不清 `_router` → 同一实例二次 `InitializeAsync` 会静默跳过，日志挂到已释放的 sink 上（实际触发需 Scope 复用，故 P3） | `ZLoggerBootstrapper.cs:27-28`、`:58-76` |
| P3-3 | Logging | `ConsoleSink` 未把 `Exception` 传给 `Debug.LogError` → Unity 控制台丢堆栈 | `ConsoleSink.cs:26-29` |
| P3-4 | Logging | 热路径分配：非主线程每条日志一个闭包（`ZLogRouter.cs:34`）；`FileSink` 每条做一次 `ConcurrentQueue.Count`（`FileSink.cs:132`） | 同左 |
| P3-5 | Pool | 重复 `CreatePool<T>` 只记 Warning 不抛（**设计稿要求抛异常**），调用方会误以为池已按新母本重建 | `ZObjectPoolManager.cs:61-65` vs `pool-manager-design.md`（"重复创建抛异常"） |
| P3-6 | Logging | 运行期日志文件被 writer 独占，其它进程读不了（实测 `IOException`） | harness B10 首次运行日志 |
| P3-7 | Resources | 释放失败不可恢复：`AssetHandle` 先置 `IsReleased` / 先销号、再真正 `Release()`，且 `Dispose` 先 `Clear` 再逐个释放 → `Release()` 抛异常时该句柄"既不在台账、又已标记释放"，引用计数永远退不掉 | `AssetHandle.cs:24-27`、`ZResourceManager.cs:95-101`（子代理静态，我**未**复核） |
| P3-8 | Tests | `FakeResourceManager` 收了 `ct` 却从不读、`Release` 无条件自增计数 → "取消路径"测不到、"失败路径不应 Release"类断言会**假绿** | `Tests/Fakes/FakeResourceManager.cs:26-39`（子代理静态，我**未**复核） |

---

## 5. 我的复核结论：**否决 2 条子代理判断** + 1 条自我纠错

> `zipper-test` §一：审查者结论**必须**由编排者自己核对，不能只看摘要。

| 被否决项 | 子代理说法 | 复核结论 |
|---|---|---|
| Pool "池满时 `Get<T>()` 静默返回 null（P1）" | 说"唯一线索是一行 Error，Get 路径至少 Warn 未满足" | ❌ **不成立**：`ZObjectPool.cs:137` 明确 `_logger.Error("已达到最大容量")`；`GetItem` 真正静默的路径只有"池已 Dispose"（`:124-125`，**注释写明是有意为之**）。原记忆里"Get 静默无日志"的隐患**已修** → 降为无问题 |
| Pool "`Clear()` 不把已销毁实例从 `inactiveItems` 摘除（P2）" | 称 `:192-198` 漏摘除 | ❌ **不成立**：`:194-195` 每轮 `Dequeue` 后都 `inactiveItems.Remove(item)` → `Clear()` 后该集合必然清空 → 误报 |
| **我自己** harness A4 第一版断言 | 断言写成"退订后本轮仍收到（快照语义）"→ 通过 | ⚠️ **这是我的假验证**：断言的是**实现行为**而非**契约**（正是 `zipper-test` §二 假验证 1 的形态）。改按 §3.3 契约断言后立刻 FAIL → 教训「断言的依据必须是需求/契约，不是代码现状」 |

---

## 6. UI 封存（本次决定，记录在案）

| 项 | 内容 |
|---|---|
| **决定** | 自 2026-10-05 起，**UI 部分封存暂停开发**（使用者拍板） |
| **封存范围** | `Assets/Zipper/UI/`、`Assets/Zipper/DI/{GameLifetimeScope,ZUiSfxAdapter}.cs` 的 UI 接线、场景/预制体侧的 UI 装配（`SampleScene` 的 `ZPanelRoot` 接线、`Assets/Prefabs/Panel*.prefab`、`Assets/Script/Demo*`、`AddressableAssetsData` 的面板地址） |
| **封存时状态** | **已知不可用**：UI MVP 代码已落地并有 24 条 EditMode 用例，但**运行时接线三处断点未修**（详见 `docs/memory/progress-2026-10-05.md` §1：面板未注册 / 层级根指向 prefab 资产 / 层级根不在 Canvas 下）。使用者选择"先封存、不修" |
| **封存后仍成立的 UI 未决项** | ① 上一轮验收发现的 P1：`ZPanelOpenOptions` 的 `SingleInstance`/`CloseOnMaskClick`/`CacheView` **只定义、从未被读取**（`ui-mvp-acceptance.md` §1.3）；② 面板接线三处断点；③ `ui-manager-design.md` §4.3 缺"层级根必须在 Canvas 之下 + 层级父节点须铺满"这条约束 |
| **解封门槛（建议）** | 三处接线断点修复 + 1 条 UI 冒烟（开 / 关 / 复用 / Esc）拿到 L1 证据 + 设计稿补上 Canvas 约束 |
| **对其它模块的影响** | 无：`Zipper.UI` 是独立程序集，Core/Pool/Resources/Audio 不依赖它（`Zipper.Core → {Pool,Resources} → {Audio, UI}`）✓ 本次编译验证也只跑非 UI 四模块 |

---

## 7. 放行结论

> 判定口径：`zipper-test` §4.2

**结论：打回（存在 P1，共 6 条）**

- 直接原因：§4.1 的 6 条 P1（Events 退订语义 1 条 / Logging 2 条 / Audio 3 条）
- 其中 **2 条已用 L1 实证复现**（P1-1 事件总线退订、P1-2 日志粘成一行），**3 条为纯静态结论但代码事实确定**（P1-3 / P1-4 / P1-5），**1 条契约缺口**（P1-6）
- **附加条件（即使 P1 修完也不足以放行）**：§3 的 6 项未覆盖里，第 1 项（EditMode 测试未运行）必须补 —— 否则音频/面板模块永远停留在 L5 转述

**批次 1 进展（2026-10-05，使用者批准的最小范围）**

| 项 | 状态 | 证据 |
|---|---|---|
| P1-2 日志换行 | ✅ **已修** | harness B10 `FAIL → PASS`；`Zipper.Core` 编译 0 warning / 0 error；回归总览 **31 passed / 2 failed**（剩余 2 条 FAIL 即下列未修项） |
| P1-1 事件总线退订 | ⏸ 未修（本次未批） | harness A4 仍 FAIL |
| P2-1 `Pump` 限量 | ⏸ 未修（本次未批） | harness B9 仍 FAIL |
| 其余 P1/P2/P3 | ⏸ 未修 | 见 §4；其中 P1-3/P1-5/P1-6/P2-2 涉及后台线程或 Unity 生命周期，**无 Unity 无法给出可信验证**，故未纳入最小范围 |

**建议修复顺序**（按"影响 × 成本"）：

1. **P1-2 日志换行**（1 行改动，收益最大：日志文件立刻可用）
2. **P1-1 事件总线退订**（给两个 `Remove` 加 `MarkAsDisposed`；harness A4 可直接当回归用例）
3. **P1-4 BGM `Pause()` NRE**（补一句 `_item == null` 守卫，与 `Resume()` 对齐）
4. **P2-1 `Pump` 的 `i++`**（1 个字符级改动，防日志风暴卡帧）
5. **P1-3 日志日期切分 / P2-2 退出刷盘**（同属 FileSink 一族，建议一并做）
6. **P1-5 BGM 淡出 / P1-6 音量读回**（音频契约缺口）
7. **P2-15 Resources 关闭守卫**（加一个 `_disposed` 标记即可收口，代价极小）
8. 其余 P2 排期；P3 登记即可

---

## 8. 复跑指引

```powershell
# ① 非 UI 模块编译（L1）
foreach ($p in @('Zipper.Core','Zipper.Pool','Zipper.Resources','Zipper.Audio')) { dotnet build "$p.csproj" -v:m --nologo }

# ② 纯 C# 运行时 harness（L1）——工程外临时目录，源码直接引用工程文件
dotnet run --project "$env:TEMP\zipper-harness\harness.csproj" -v:q --nologo
#    期望：修完 P1-1/P1-2/P2-1 后为 33 passed / 0 failed

# ③ EditMode 测试（**必须由使用者在 Unity 内跑**，本机不允许启第二个 Unity 实例）
#    Unity → Window > General > Test Runner > EditMode > Run All
```

---

## 9. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| **v1.1** | 2026-10-05 | **批次 1 修复回填**：P1-2（日志文件粘成一行）**已修** —— `FileSink.Write` 入队补 `Environment.NewLine`，harness B10 `FAIL→PASS`，`Zipper.Core` 编译 0/0，回归 **31 passed / 2 failed**（剩余 FAIL = 未批的 P1-1 与 P2-1）；§7 新增「批次 1 进展」表；P2-4 补充"异常为空时消息前两个空格"这一实测细节。代码已**本地提交**（`fix(logging): …`，未 push）；本报告与记忆追加**仍未提交**（使用者要求） |
| v1.0 | 2026-10-05 | 初版：非 UI 模块（Core/Pool/Resources/Audio）体检。L1：四模块编译 0/0 + 纯 C# harness 30 passed/3 failed（3 条为缺陷复现）+ 真实日志文件佐证；L4：**四**模块静态审查（Resources 在定稿前补齐并复核其 P2）。缺陷合计 P1×6 / P2×15 / P3×8；复核否决子代理 2 条、自我纠错 1 条（假验证）；UI 封存决定与解封门槛入档。**提交状态：未提交（使用者要求）** |
