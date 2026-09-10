# Zipper 命名约定

> 状态：v0.2 草案（2026-09-06，使用者给出核心规则：品牌名 Zipper 取首字母 **Z**；并明确"内部机制类型不加前缀"）
> 定位：框架自有类型/成员的命名规则，避免同一套代码里出现多种风格。**待使用者确认。**
> 关联：`docs/standards/agent-role.md`、`docs/architecture/core-design.md`（§4.3 事件总线）

---

## 1. 核心规则：Z 前缀

| 类别 | 规则 | 正例 |
|---|---|---|
| 框架自有类型（类/结构/枚举） | **`Z*`** | `ZLog`、`ZEventBus`、`ZResourceManager`、`ZObjectPool`、`ZPrefabAsset` |
| 框架自有接口 | **`IZ*`** | `IZLogger`、`IZEventBus`、`IZResourceManager`、`IZObjectPoolItem` |
| 事件类型 | `Z*` + **过去式**（表达"已发生"） | `ZResourceLoaded`、`ZSceneChanged`、`ZUIPanelOpened` |
| 命名空间 | `Zipper.<模块>` | `Zipper.Core.Logging`、`Zipper.Resources` |

之所以不用 `ZipperXxx` 全名：类型名太长会拖累可读性（`ZipperResourceManager.LoadAssetAsync` 啰嗦），首字母 Z 已能一眼区分"框架自有"与"业务/第三方"。

## 2. 例外（技术栈标识优先）

**体现第三方技术栈的实现类，技术栈名放前缀位，不再叠 Z**：如 R3 封装的事件总线实现命名为 **`ZR3EventBus`**（Z = 框架自有，R3 = 明确其实现栈）。

> 若将来觉得 `ZR3EventBus` 拗口，可改为 `ZEventBusR3` 之类的形态——**同一套代码里保持一致即可**，此处不强制。

## 3. 成员与文件命名

| 项 | 规则 |
|---|---|
| 公共成员 / 属性 | PascalCase |
| 私有字段 | `_camelCase` |
| 局部变量 / 参数 | camelCase |
| 常量 | PascalCase（不用全大写） |
| 异步方法 | 后缀 `Async` |
| 文件名 | PascalCase，与主类型同名；一个文件一个主类型 |
| asmdef | 与程序集名一致（`Zipper.Core.asmdef`） |
| 目录 | PascalCase；命名空间与目录保持一致 |
| 文档文件 | kebab-case 英文（`core-design.md`、`naming-convention.md`） |

## 4. 前缀的适用范围与例外（v0.2 明确）

**加 Z / IZ 前缀的**：框架的**对外概念与入口**——模块级管理器、公开服务、公共设施及其接口。
例：`ZLog` / `IZLogger`、`ZEventBus` / `IZEventBus`、`ZR3EventBus`、`ZResourceManager` / `IZResourceManager`、`ZObjectPool` / `IZObjectPoolItem`。

**不加前缀的**：**内部机制类型**——服务于某个模块的内部实现；虽然类型是 `public`（外部可访问、可出现在签名里），但**不属于框架对外的品牌 API**，因此保持朴素命名。
例（使用者 2026-09-06 明确）：`AssetHandle<T>`、`AssetHandleBase`、`PrefabAsset` —— 资源管理器签发/持有的句柄与母本载体，属内部机制。

**判定口径**：问一句"这是框架给使用者看的**概念/入口**，还是某个模块**内部流转用的机制/数据载体**？"—— 前者加前缀，后者不加。**`public` ≠ 品牌 API。**

## 5. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.2 | 2026-09-06 | §4 由"存量不一致项（待决定）"改为**明确规则**：前缀只用于"对外概念与入口"，**内部机制类型不加前缀**（`AssetHandle<T>`/`AssetHandleBase`/`PrefabAsset` 为例）；补判定口径"`public` ≠ 品牌 API" |
| v0.1 | 2026-09-06 | 初版：Z/IZ 前缀规则、事件过去式、成员与文件命名 |
