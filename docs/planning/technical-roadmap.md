# Zipper Unity 框架 — 技术路线规划（v0.3 定稿待审批）

> 状态：**定稿，待审批**
> 定位：本文件是框架开发的总体技术路线，回答「做什么、怎么做、按什么顺序做」；**不含代码实现**。
> 依据：协调者规范（先文档、后编码；无批准、不实施）。
> 变更记录：v0.2 融入调研结论（DOTS/Addressables 版本基线）；v0.3 依据用户决策改为 **.unitypackage 右键导出、拆箱即用**分发形态（放弃 UPM 包形态），第三方依赖随包 vendor。

---

## 1. 背景与目标

- 在 Unity 2022.3 LTS 工程中自研一套自用游戏框架插件，覆盖四大模块：**资源管理器、音频管理器、UI 管理器、对象池（普通 + ECS）**。
- 定位：**自用为主**，服务于 2027 年 4-6 月毕业设计答辩演示，因此**开发速度优先于完备性**，但需保证架构可讲、可演示、可扩展。
- **分发形态（本版核心变更）**：**右键导出 .unitypackage、导入拆箱即用**——目标工程拿到包双击导入即可跑，无需手动配包名/引用。为此框架源码全部位于 Assets/ 下（Export Package 只能导出 Assets/），第三方开源依赖随包 vendor。

## 2. 现状盘点

| 项 | 现状 | 影响 |
|---|---|---|
| Unity 版本 | 2022.3.62f2c1（LTS） | 支持 Entities 1.0.x、Addressables 1.x |
| 渲染管线 | URP 14.0.12 | 无特殊限制 |
| UI | uGUI 1.0.0 + TextMeshPro 3.0.7 | UI 方案定为 uGUI |
| DI | VContainer 1.19.0（**现以 UPM 包安装，位于 Packages/**） | 需 vendor 源码到 Assets/ 随包分发 |
| 异步 | UniTask（**已在 Assets/Plugins/UniTask 源码版**） | 已满足 vendor，无需迁移 |
| 绑定层 | 无 | 引入 R3，vendor 到 Assets/Plugins/ |
| 资源层 | **Addressables 主包未安装** | 官方 UPM 包无法进 .unitypackage，作前置依赖（见 §6） |
| DOTS | 未安装（无 Entities/Burst/Collections/Mathematics/Jobs） | 官方 UPM 包，作前置依赖；版本见 §5.4.2 |
| 已有代码 | `Assets/Script/Zipper/{DI, Manager, Pool}`（`ObjectPool<T>` 质量良好） | M0 迁移并重组到框架根目录（见 §4.3） |
| asmdef | Script 目录无 asmdef（Assembly-CSharp） | 需程序集化，见 §4 |
| 工程治理 | 无 git、无 docs/、无 AGENTS.d/、无编码规范 | 需初始化，见 §7 |

## 3. 技术选型总览（已确认决策）

| 决策点 | 结论 | 理由 |
|---|---|---|
| 分发形态 | **.unitypackage 右键导出（原生 Export Package）、导入拆箱即用** | 用户决策：简单直接，无需 UPM 包名/引用配置；毕设 Demo 工程导入即跑 |
| 依赖策略 | **第三方开源依赖随包 vendor**（VContainer/UniTask/R3 源码进 Assets/，自带 LICENSE） | 用户决策：真拆箱即用；官方 UPM 包（Addressables/DOTS）只能前置依赖 |
| 资源加载 | **Addressables 1.x**（+ UniTask.Addressables） | 生态成熟、异步友好、引用计数；2.x 面向 Unity 6，2022.3 不冒险 |
| ECS 深度 | **完整 DOTS**：Entities + Burst + Jobs + Collections | 对象池性能最大化；毕设可展示技术含量 |
| UI 体系 | **uGUI + TMP** | 已装、运行时 UI 生态最成熟 |
| UI 架构 | **MVVM**，绑定层用 **R3** | ViewModel 纯 C# 可测；R3 与 UniTask 同作者 |
| 依赖注入 | VContainer | 模块经容器装配，官方支持 async 生命周期 |
| 异步 | UniTask | 所有 Manager 异步接口统一 UniTask |
| 对象池（普通） | **保留自研** `ObjectPool<T>` 演进 | 调研确认优于内置池；临时集合缓存用内置 CollectionPool |
| 命名空间 | `Zipper.*`（沿用现有） | 与现状一致 |

**版本基线（调研初核，实施前以本机 Package Manager 实际解析为准）**
- Entities **1.0.16**；配套 Burst 1.8.x / Collections 2.1.4 / Mathematics 1.2.6 / Jobs 1.1.1（前置依赖）
- Addressables **1.21.21 / 1.22.3**（前置依赖）
- 关键约束：结构变更必须走 ECB 且在主线程播放；实体不能挂 MonoBehaviour；Baking 走 Baker

## 4. 架构与程序集

### 4.1 设计原则

1. **程序集级解耦**：每个模块独立 asmdef，依赖方向单向（见 4.2），模块可裁剪（拆箱即用 = 导入后即编译，asmdef 全部随包生效）。
2. **容器装配**：模块实例统一由 VContainer 组装与注入；禁止全局单例直接访问。
3. **异步统一**：对外异步接口一律 UniTask，支持 CancellationToken 取消与生命周期绑定。
4. **依赖倒置**：模块面向接口编程（UI/音频只依赖资源管理器接口）。
5. **先核心后外围**：对象池、资源为地基，UI/音频为上层，ECS 池独立。

### 4.2 程序集依赖（设计）

```
Zipper.Core          — 基础设施：日志、事件、扩展、公共工具
Zipper.Pool          — 普通对象池（现有 ObjectPool<T> 演进）
Zipper.Resources     — 资源管理器（Addressables 封装）  依赖 Core
Zipper.Audio         — 音频管理器                       依赖 Core/Pool/Resources
Zipper.UI            — UI 管理器（uGUI + MVVM/R3）      依赖 Core/Pool/Resources
Zipper.ECS.Pool      — ECS 对象池（完整 DOTS）          依赖 Core（独立）
Zipper.Runtime       — 组装层：VContainer Scope、引导    依赖以上全部
Zipper.Editor        — 编辑器扩展、校验                 依赖 Zipper.*（仅 Editor 平台）
```

- 单向依赖：`Core → Pool → Resources → {Audio, UI}`；`ECS.Pool` 自成一体。
- 所有 Zipper.* 运行时程序集需引用（asmdef References）：UniTask / VContainer；UI 额外引 R3；Resources 与 ECS.Pool 额外引对应官方包程序集。

### 4.3 目录与文件布局（M0 落地目标）

```
Assets/
├── Zipper/                        ← 框架根（导出包时勾选本目录）
│   ├── Core/         Zipper.Core.asmdef + 源码
│   ├── Pool/         Zipper.Pool.asmdef + 源码（现有 ObjectPool 迁入）
│   ├── Resources/    Zipper.Resources.asmdef + 源码
│   ├── Audio/        Zipper.Audio.asmdef + 源码
│   ├── UI/           Zipper.UI.asmdef + 源码
│   ├── ECS.Pool/     Zipper.ECS.Pool.asmdef + 源码
│   ├── Runtime/      Zipper.Runtime.asmdef（引导/Scope）
│   ├── Editor/       Zipper.Editor.asmdef（Editor Only：菜单、校验）
│   └── Samples/      （示例场景/预制体，随包分发）
├── Plugins/                       ← 第三方 vendor（导出时随包）
│   ├── UniTask/      UniTask.asmdef（已 vendor，不动）
│   ├── VContainer/   从 Packages 迁源码至此（自带 asmdef）
│   └── R3/           引入后 vendor 至此（自带 asmdef）
└── Script/Zipper/                 ← 现状骨架，M0 迁移并入 Assets/Zipper/ 后删除
```

- **迁移动作（M0 执行，需批准）**：`Assets/Script/Zipper/{DI,Pool}` → 并入 `Assets/Zipper/{Runtime,Pool}`；命名空间不变。
- asmdef 创建与配置的操作规范见 §4.4。

### 4.4 程序集创建与配置指引（操作规范）

**何时需要 asmdef**：只要代码需要被「其他工程/模块按编译边界引用、控制依赖、区分 Editor/运行时」就需要。拆箱即用分发下 asmdef 随包自动生效，导入后无需任何手动配置。

**创建步骤**：
1. 在目标目录右键 → Create → Assembly Definition，生成 `<模块名>.asmdef`（如 `Zipper.Pool.asmdef`）。
2. 选中该文件，Inspector 配置：
   - **General**：程序集名与文件名一致（如 `Zipper.Pool`）。
   - **Assembly Definition References**：Add → 选择要依赖的程序集（本项目 asmdef、vendor 程序集、官方包程序集均可直接搜名添加；Unity 自动记录依赖并拓扑排序编译）。**引用关系必须与 §4.2 单向依赖一致，禁止循环引用（Unity 直接报错）。**
   - **Platforms**：运行时程序集保持 Any Platform；编辑器工具程序集取消全部勾选、仅勾 **Editor**（JSON 中 `"includePlatforms": ["Editor"]`）；测试程序集只勾 Editor。
   - **Auto Referenced**：默认开（供无 asmdef 脚本引用）；框架模块间用显式 References，保持显式更清晰。
3. 现有无 asmdef 的脚本一旦被 asmdef 目录包含即脱离 Assembly-CSharp，自动进新程序集——**目录迁移与 asmdef 落地需同步完成，避免编译悬空**。

**常见坑**：引用官方包程序集需确认该包已安装（未装 Unity.Addressables 就引用会编译失败）；asmdef 目录嵌套会导致程序集嵌套冲突（每个 asmdef 目录下不能再放子 asmdef，子 asmdef 须在独立子目录）；改名 asmdef 会连带改引用它的所有 asmdef GUID，尽量一次命名到位。

## 5. 各模块技术路线

### 5.1 资源管理器（Zipper.Resources）

- **底层**：Addressables **1.x**（前置依赖，初核 1.21.21/1.22.3）；**接入**：UniTask.Addressables 扩展。
- 对外能力：类型安全异步加载/释放（泛型 + 枚举键约定）；预加载/预热（供对象池与 UI 复用）；实例化辅助（加载预制体→实例化→归还池联动）；场景切换资源生命周期处理；引用计数依托 Addressables 原生，封装层只做簿记与日志。
- 生命周期：随 VContainer Scope 创建/释放；支持全局取消。
- **兜底**：Resources 仅作框架自身最小引导资源，业务资源一律 Addressables。
- 验收标准：加载/释放正确性、重复加载去重、异步取消不泄漏（EditMode 单测）。

### 5.2 音频管理器（Zipper.Audio）

- **底层**：原生 AudioSource + AudioMixer；AudioSource 走普通对象池。
- 对外能力：2D 音效 / 3D 空间音效 / BGM 三类播放接口；播放句柄（停止/暂停/续播）；淡入淡出（UniTask 驱动）；AudioMixer 分组（Master/SFX/BGM/UI）与音量持久化；BGM 独占通道；可选事件接口。
- 验收标准：播放/停止/淡入淡出正确性、AudioSource 池复用无泄漏、音量持久化生效。

### 5.3 UI 管理器（Zipper.UI）— 重点模块，uGUI + MVVM

- **面板管理**：面板栈（Open/Close/Back）；UI 层级（Layer：背景/主界面/弹窗/提示/加载）；全屏遮罩与弹窗排序；面板生命周期异步化；面板 View 关闭不销毁、归还对象池、再次打开重绑。
- **MVVM 三件套**：View（UGUI 组件，持控件引用与绑定声明，无业务逻辑）；ViewModel（纯 C# 类，R3 属性/命令，可脱离 Unity 单测）；绑定层（R3 驱动轻量绑定：属性→文本/显隐/颜色等，命令→按钮点击；含 View 池化复用的解绑/重绑协议）。
- **加载**：面板预制体经资源管理器异步加载；ViewModel 由 VContainer 构造。
- **MVP 边界**：先「面板栈 + 生命周期 + 基础绑定（属性/命令）」，高级绑定（列表等）随用随扩。
- 验收标准：面板开/关/压栈/返回正确；View 复用无绑定泄漏；ViewModel 单测通过；设置面板 Demo 走通 MVVM 全链路。

### 5.4 对象池

#### 5.4.1 普通对象池（Zipper.Pool）
- 现有 `ObjectPool<T>` 演进（保留自研）：补齐预热、容量上限与超限策略、统计（活跃/空闲/总数）、自动回收（可选）、预制体来源接入资源管理器、GameObject 级池补充。
- **与内置池关系**：临时集合/轻量复用直接用内置 `CollectionPool`/`GenericPool`；框架级 GameObject 池保留自研。
- 验收标准：预热/获取/归还/清理全路径正确；超容量/重复归还/跨池归还等边界有明确行为与报错。

#### 5.4.2 ECS 对象池（Zipper.ECS.Pool）— 完整 DOTS
- **版本（初核，前置依赖）**：Entities **1.0.16** + Burst 1.8.x + Collections 2.1.4 + Mathematics 1.2.6 + Jobs 1.1.1。
- **硬约束**：结构变更必须走 ECB 且主线程播放；实体不能挂 MonoBehaviour；Baking 走 Baker；实施前以本机 Package Manager 解析为准。
- **设计**：按 Archetype 批量预分配；空闲实体入 NativeQueue（O(1) 获取/归还，**防重复入池**是主要坑）；池系统与使用侧系统分离，支持 ECB 延迟与立即两种语义；热路径 Burst+Job，结构变更帧尾统一提交；池统计监控。
- **适用**：弹幕/粒子/批量敌人等大量实体；与普通池接口不强行统一，文档说明取舍。
- 验收标准：万级实体获取/归还稳定；与普通池性能对比数据（答辩素材）；Burst 编译通过、无 Job 依赖错误。

### 5.5 组装层（Zipper.Runtime）
- VContainer LifetimeScope 注册全部 Manager/ViewModel/池注册表；async 生命周期（InitializeAsync/StartAsync）与 CancellationToken 注入按官方一等支持使用；提供包内「一键引导」组件；场景切换策略由 Scope 层级控制。

## 6. 打包与分发（本版核心新增）

### 6.1 前置依赖清单（目标工程必须手动安装，无法随 .unitypackage）

| 依赖 | 版本（初核） | 用途 |
|---|---|---|
| Addressables | 1.21.21 / 1.22.3 | 资源管理器底层 |
| Entities + Burst + Collections + Mathematics + Jobs | 1.0.16 + 1.8.x + 2.1.4 + 1.2.6 + 1.1.1 | ECS 对象池（未用到 DOTS 的工程可跳过） |

> 备一份 `README`/导入指引文档随包，写明上述依赖与版本。Addressables/DOTS 属官方 UPM 包，经 Package Manager 安装。

### 6.2 拆箱即用自包含内容（随包导出）

- `Assets/Zipper/` 全部（框架源码 + asmdef + Samples）；
- `Assets/Plugins/UniTask`、`Assets/Plugins/VContainer`、`Assets/Plugins/R3`（第三方 vendor 源码 + 各自 asmdef + **LICENSE 文件**，MIT 许可合规）。
- 导出动作（原生 Export Package）：Assets 窗口多选上述目录 → 右键 **Export Package** → 勾选全部 → 导出 `.unitypackage`。目录结构即打包结构，导入目标工程后 asmdef/依赖全部就位。
- **Vendor 动作（M0，需批准）**：把 Packages 下 VContainer 源码拷入 `Assets/Plugins/VContainer`（保留 LICENSE），然后从 manifest 移除 UPM 引用，避免双份；R3 引入时直接 vendor。
- 场景/预制体引用注意：Sample 场景引用的脚本因 asmdef 随包完整迁移，导入后引用不丢（同工程导出/导入天然保 GUID）。

### 6.3 分发验证（每阶段验收含此项）

- 建立「干净测试工程」：新建空 Unity 2022.3 工程 → 装 §6.1 前置依赖 → 导入当前 .unitypackage → 打开 Sample 场景直接运行。每完成一个模块阶段导出一版验证。

## 7. 工程治理（随 M0 落地）

1. **git init**：首次基线提交；主线 + 特性分支；禁止 rebase/force-push，合并用 Squash。
2. **文档体系 docs/**：`planning/`（本路线及阶段计划）、`standards/`（C# 编码规范、asmdef 规范）、`architecture/`（模块设计文档）、`research/`（外部调研）、`memory/`（项目记忆，与 memory 工具库同步）。
3. **AGENTS.d/**：落地协调者规范体系（roles / workflow / git / coding / testing / docs / review / memory 八份）。
4. **编码规范**：C# 编码规范先行（命名、程序集、注释、异常、性能约定），无规范不编码。
5. **测试**：EditMode 单测为主，PlayMode 冒烟；每阶段验收含测试证据。
6. **提交规范**：Conventional Commits；阶段成果自动提交，临时/小改动先询问。

## 8. 开发里程碑

> 时间假设：答辩 **2027 年 4-6 月**（待确认）。每阶段含验收标准与完成证据。

| 阶段 | 时间（预估） | 内容 | 完成证据 |
|---|---|---|---|
| M0 工程治理 | 2026-09 上旬 | git init；docs/ 与 AGENTS.d/；目录重组（Script/Zipper → Assets/Zipper）；**asmdef 全量创建**（§4.3/§4.4）；C# 与 asmdef 规范；VContainer vendor；Addressables 引入 | 仓库可提交、全部程序集编译通过、规范齐备 |
| M1 地基 | 2026-09~10 | 普通对象池完善；资源管理器（Addressables+UniTask、预加载、释放）；R3 vendor | 池单测通过；资源 Demo；干净工程导入验证 |
| M2 UI 核心 | 2026-10~11 | UI 管理器 MVP：面板栈+生命周期+MVVM 绑定层+View 池化 | 设置面板 Demo 走通 MVVM；导入验证 |
| M3 音频+整合 | 2026-11~12 | 音频管理器；三模块整合；Samples | 音频 Demo；整合工程导入即跑 |
| M4 ECS 池 | 2027-01~02 | DOTS 引入；ECS 对象池+性能验证 | 万级实体压测数据；导入验证 |
| M5 毕设收尾 | 2027-03 | 毕设演示工程集成；文档/架构图收尾；答辩材料 | 可现场演示完整 Demo 工程 |

- 缓冲：M1-M4 后各留 1-2 周；答辩前 1 个月冻结新特性。裁剪顺序：ECS 池高级特性 → 音频高级特性 → UI 高级绑定。

## 9. 风险与待决项

| 风险/待决项 | 说明 | 应对 |
|---|---|---|
| .unitypackage 与官方包边界 | Addressables/DOTS 无法随包，目标工程须手动装 | 导入指引文档 + 干净工程验证流程（§6） |
| vendor 双份风险 | VContainer 从 UPM 迁 Assets 时若忘移除 manifest 引用将双份冲突 | M0 迁移清单化，迁移后编译验证 |
| DOTS 版本兼容 | 初核版本 9 项细节未获权威背书（调研报告末尾汇总） | 实施前以本机 Package Manager 解析为准，冲突熔断报告 |
| MVVM 复杂度失控 | 绑定层自研易过度设计 | MVP 边界硬约束（§5.3） |
| 毕设时间假设 | 答辩是否 2027 年 4-6 月 | **待用户确认** |
| ECS 池适用边界 | 与普通池职责划分 | 各自独立，不做强行统一 |

## 附录：外部调研

- 调研报告：`docs/research/unity-framework-tech-facts.md`（7 节 + 与现有代码关系 + 9 项待核实清单，含来源 URL）。

## 审批记录

- [ ] 用户批准 v0.3（日期：____，意见：____）
