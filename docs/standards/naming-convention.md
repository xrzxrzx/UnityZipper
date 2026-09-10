# Zipper 命名约定

> 状态：v0.1 草案（2026-09-06，使用者给出核心规则：品牌名 Zipper 取首字母 **Z**）
> 定位：框架自有类型/成员的命名规则，避免同一套代码里出现多种风格。**待使用者确认；存量类型改名另行决定（见 §4）。**
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

## 4. 存量不一致项（待使用者决定）

以下类型已存在于代码中但**没有 Z 前缀**，是否需要改名由使用者决定（改名会连带改引用与 `.meta` 内的引用关系，建议一次到位）：

| 现名 | 按规则应为 | 备注 |
|---|---|---|
| `AssetHandle<T>` | `ZAssetHandle<T>` | 资源管理器句柄 |
| `AssetHandleBase` | `ZAssetHandleBase` | 句柄基类 |
| `PrefabAsset` | `ZPrefabAsset` | 预制体母本持有者 |

> 若决定不改，本规范应补一条"句柄类例外"说明，避免规则与代码长期不一致。

## 5. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.1 | 2026-09-06 | 初版：Z/IZ 前缀规则、事件过去式、成员与文件命名、存量不一致项清单 |
