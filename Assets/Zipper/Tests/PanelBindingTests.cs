using NUnit.Framework;
using Zipper.Tests.Fakes;
using Zipper.UI;

namespace Zipper.Tests
{
    /// <summary>
    /// View 复用的解绑/重绑协议（对应设计稿 §11-4 / §11-5 / §11-9 / §7.3）——
    /// **这是本模块最核心的一组断言**：复用不串台、订阅不叠加、VM 所有权正确。
    ///
    /// <para>关于取证手段：这里没有用 <c>ObservableTracker</c>，而是在测试 View 上放了
    /// <c>ApplyCallCount</c> 计数探头 —— 判据更直接（订阅残留必然让回调次数 &gt; 1），
    /// 且不依赖 R3 与 Unity 测试框架的集成情况。</para>
    /// </summary>
    public class PanelBindingTests : PanelTestBase
    {
        [SetUp]
        public void RegisterPanelUnderTest() => RegisterTestPanel();

        /// <summary>
        /// ★ §11-4：同一面板 Open/Close 50 次 → 订阅不得累积。
        /// 判据：第 51 次打开后推一个值，回调只应命中 1 次（残留 N 份订阅就会命中 N 次）。
        /// </summary>
        [Test]
        public void Reuse_50Times_SubscriptionDoesNotStack()
        {
            MakePoolReady();

            for (int i = 0; i < 50; i++)
            {
                var h = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
                h.Close();
            }

            Assert.AreEqual(0, Manager.GetState().StackDepth, "50 次开关后栈应为空");
            Assert.AreEqual(1, FindPanels().Length, "50 次开关应始终复用同一个 View 实例");

            var h51 = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var view = FindPanels()[0];
            view.ApplyCallCount = 0;                                   // 从本次开始单独计数

            TestPanelViewModel.All[TestPanelViewModel.All.Count - 1].Push(42);

            Assert.AreEqual(1, view.ApplyCallCount,
                "订阅残留：回调次数 > 1 说明旧的 R3 订阅没在 Unbind 里退掉");
            Assert.AreEqual(42, view.LastAppliedValue);

            h51.Close();
            Assert.AreEqual(50 + 1, TestPanelViewModel.All.Count, "每次打开应构造一个新 VM（Transient）");
        }

        /// <summary>★ §11-5：关闭后旧 VM 推值，不得再影响复用的 View（不串台）。</summary>
        [Test]
        public void Unbind_OldViewModelPush_DoesNotReachView()
        {
            // ★ 必须用【常驻 VM】(ownsViewModel: false)：
            //   若用默认的 Transient，框架会在 Close 时把 VM Dispose 掉 ✗
            //   那 oldVm.Push(...) 会先抛 ObjectDisposedException（R3 的 ThrowIfDisposed）
            //   → 测到的是"VM 已释放所以推不了"，而不是"订阅已退掉" ✗ 判据用错了。
            //   常驻 VM 关闭后依然活着 → 此时推值若仍能到 View，才是真的串台 ✓
            RebuildManagerWithEmptyContainer();     // 基类 [SetUp] 已用默认值注册过，重复注册会被拒绝
            Manager.Register<TestPanelViewModel, TestPanel>("UI/TestPanel", ownsViewModel: false);
            MakePoolReady();

            var h1 = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var oldVm = TestPanelViewModel.All[TestPanelViewModel.All.Count - 1];
            h1.Close();

            Assert.AreEqual(0, oldVm.DisposeCount, "常驻 VM 不该被释放（否则本用例的前提不成立）");

            var h2 = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var newVm = TestPanelViewModel.All[TestPanelViewModel.All.Count - 1];
            var view = FindPanels()[0];
            view.ApplyCallCount = 0;

            oldVm.Push(999);
            Assert.AreEqual(0, view.ApplyCallCount, "旧 VM 推值竟到了新 View → 串台");
            Assert.AreEqual(-1, view.LastAppliedValue, "View 不该被旧 VM 写过");

            newVm.Push(7);
            Assert.AreEqual(1, view.ApplyCallCount, "当前 VM 推值应恰好命中一次");
            Assert.AreEqual(7, view.LastAppliedValue);

            h2.Close();
        }

        /// <summary>关闭后 uGUI 事件与显示态由 View 自己清（框架只保证何时调 OnUnbind）。</summary>
        [Test]
        public void Unbind_IsCalledBeforeReturningToPool()
        {
            MakePoolReady();

            var h = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var view = FindPanels()[0];
            int unbindAtThatMoment = -1;

            h.Close();

            unbindAtThatMoment = view.UnbindCount;
            Assert.AreEqual(1, unbindAtThatMoment, "关闭应调一次 OnUnbind");
            Assert.IsFalse(view.gameObject.activeSelf, "Unbind 之后才归还池（SetActive(false) 已发生）");
        }

        /// <summary>§11-9：VM 是纯 C# —— 不依赖 Unity、不依赖容器就能构造与释放。</summary>
        [Test]
        public void ViewModel_IsPlainCSharp()
        {
            var vm = new TestPanelViewModel();          // 不需要 GameObject、不需要容器
            vm.Push(3);

            Assert.AreEqual(3, vm.Value.Value);
            Assert.AreEqual(0, vm.DisposeCount);

            vm.Dispose();
            Assert.AreEqual(1, vm.DisposeCount);
        }

        /// <summary>§7.3 / T10b：Transient VM 在面板关闭时由框架释放。</summary>
        [Test]
        public void TransientViewModel_IsDisposedWhenPanelCloses()
        {
            MakePoolReady();

            var h = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var vm = TestPanelViewModel.All[TestPanelViewModel.All.Count - 1];
            Assert.AreEqual(0, vm.DisposeCount, "打开期间不该被释放");

            h.Close();

            Assert.AreEqual(1, vm.DisposeCount, "Transient VM 应由框架在关闭时释放");
        }

        /// <summary>
        /// §7.3 / T10b：常驻 VM（<c>ownsViewModel: false</c>）**不得**被框架释放 ——
        /// 否则容器还会再 Dispose 一次（双重释放），且下次打开状态全丢。
        /// </summary>
        [Test]
        public void PersistentViewModel_IsNotDisposedByFramework()
        {
            // ★ 与上一条同理：基类 [SetUp] 已用默认值（true）注册过 → 必须先换成空注册表再登记
            RebuildManagerWithEmptyContainer();
            Manager.Register<TestPanelViewModel, TestPanel>("UI/TestPanel", ownsViewModel: false);
            MakePoolReady();

            var h = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var vm = TestPanelViewModel.All[TestPanelViewModel.All.Count - 1];

            h.Close();

            Assert.AreEqual(0, vm.DisposeCount, "常驻 VM 归容器管，框架不许碰");
        }

        /// <summary>关闭时清掉框架注入的两个通道（音效接口 + 关闭出口），防归池后仍握着引用。</summary>
        [Test]
        public void Close_ClearsInjectedChannels()
        {
            MakePoolReady();

            var h = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var view = FindPanels()[0];

            Assert.IsTrue(view.HasSfx, "打开时应注入音效接口");
            Assert.IsTrue(view.HasCloseRequester, "打开时应注入关闭出口");

            h.Close();

            Assert.IsFalse(view.HasSfx, "归池前应清空音效接口");
            Assert.IsFalse(view.HasCloseRequester, "归池前应清空关闭出口");
        }
    }
}
