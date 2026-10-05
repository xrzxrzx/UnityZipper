using System.Runtime.CompilerServices;

// 让测试相关程序集能看到内部机制类型与成员（照 Audio 模块 AssemblyInfo 的做法）：
//   internal 类型：PanelRegistry / Registration / PanelStackEntry
//   internal 成员：ZPanelManager.Close / ZPanel.Bind / Unbind / Open / Close / SetSfx / CloseRequester
// 说明：不为了「可测性」把这些改成 public —— 内部机制不该出现在对外契约面上。
//
// Zipper.Tests       ：EditMode 测试代码（断言 internal 机制与公开契约）
// Zipper.TestSupport ：测试用的运行时组件。它必须编译在【非 Editor】程序集里，
//                      否则 MonoBehaviour 无法 AddComponent 到 GameObject 上
//                      （Unity 报 "Can't add script behaviour ... because it is an editor script"）✗
//                      它的 TestPanel 探针需要读 ZPanel.CloseRequester（internal），
//                      用来验证"归池前已清空注入的关闭出口"——这是外部唯一可验证该行为的手段。
[assembly: InternalsVisibleTo("Zipper.Tests")]
[assembly: InternalsVisibleTo("Zipper.TestSupport")]
