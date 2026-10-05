using System;
using NUnit.Framework;
using Zipper.UI;
using Zipper.UI.Contract;

namespace Zipper.Tests
{
    /// <summary>
    /// 栈、层级与生命周期钩子（对应设计稿 §11-1 / §11-3、§4.2、§4.3）。
    /// </summary>
    public class PanelStackTests : PanelTestBase
    {
        [SetUp]
        public void RegisterPanelUnderTest() => RegisterTestPanel();

        /// <summary>§11-1：空栈时 TryHandleBack 必须返回 false（把返回键透传给上层，框架不吞）。</summary>
        [Test]
        public void TryHandleBack_OnEmptyStack_ReturnsFalse()
        {
            Assert.IsFalse(Manager.TryHandleBack());
            Assert.AreEqual(0, Manager.GetState().StackDepth);
        }

        /// <summary>§11-1：空栈时 CloseAll 不得抛异常。</summary>
        [Test]
        public void CloseAll_OnEmptyStack_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Manager.CloseAll());
            Assert.AreEqual(0, Manager.GetState().StackDepth);
        }

        /// <summary>打开面板 → 入栈、签发打开着的凭据、并挂到对应层级的父节点下（§4.3）。</summary>
        [Test]
        public void Open_PushesIntoStack_AndParentsToItsLayer()
        {
            MakePoolReady();

            var handle = Manager
                .OpenAsync<TestPanelViewModel>(new ZPanelOpenOptions { Layer = ZPanelLayer.Popup })
                .GetAwaiter().GetResult();

            Assert.IsTrue(handle.IsOpen, "打开时凭据应为 open");

            var state = Manager.GetState();
            Assert.AreEqual(1, state.StackDepth);
            Assert.AreEqual(1, state.LayerCounts[(int)ZPanelLayer.Popup]);
            Assert.AreEqual(0, state.LayerCounts[(int)ZPanelLayer.Main]);

            var panel = FindPanels()[0];
            Assert.AreEqual("Layer_Popup", panel.transform.parent.name, "应挂在 Popup 层父节点下");
        }

        /// <summary>
        /// ★ §11-1 的核心：TryHandleBack 取"层级最高"的那个，而**不是**"最后打开"的那个。
        /// 先开 Popup、再开 Main —— 栈顶是 Main，但返回键必须先关 Popup。
        /// </summary>
        [Test]
        public void TryHandleBack_PrefersHigherLayer_NotLastOpened()
        {
            MakePoolReady();

            var popup = Manager.OpenAsync<TestPanelViewModel>(
                new ZPanelOpenOptions { Layer = ZPanelLayer.Popup }).GetAwaiter().GetResult();
            var main = Manager.OpenAsync<TestPanelViewModel>(
                new ZPanelOpenOptions { Layer = ZPanelLayer.Main }).GetAwaiter().GetResult();

            Assert.IsTrue(Manager.TryHandleBack());

            Assert.IsFalse(popup.IsOpen, "应关闭层级更高的 Popup");
            Assert.IsTrue(main.IsOpen, "Main 不该被关（这正是「不能简单取栈顶」的反例）");
        }

        /// <summary>同层内按打开顺序：后开的先关。</summary>
        [Test]
        public void TryHandleBack_WithinSameLayer_ClosesLastOpened()
        {
            MakePoolReady();

            var first = Manager.OpenAsync<TestPanelViewModel>(
                new ZPanelOpenOptions { Layer = ZPanelLayer.Main }).GetAwaiter().GetResult();
            var second = Manager.OpenAsync<TestPanelViewModel>(
                new ZPanelOpenOptions { Layer = ZPanelLayer.Main }).GetAwaiter().GetResult();

            Assert.IsTrue(Manager.TryHandleBack());

            Assert.IsFalse(second.IsOpen, "同层应先关后开的");
            Assert.IsTrue(first.IsOpen);
        }

        /// <summary>§11-3：重复 Close 幂等 —— 不抛、且栈只减一次。</summary>
        [Test]
        public void Close_IsIdempotent()
        {
            MakePoolReady();

            var handle = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            handle.Close();

            Assert.AreEqual(0, Manager.GetState().StackDepth);
            Assert.IsFalse(handle.IsOpen);

            Assert.DoesNotThrow(() => handle.Close());
            Assert.DoesNotThrow(() => handle.Close());
            Assert.AreEqual(0, Manager.GetState().StackDepth, "重复关闭不该再次减栈");
        }

        /// <summary>§4.2 的钩子映射：OnCreate 只在首次创建时调；复用打开只调 OnOpen。</summary>
        [Test]
        public void Hooks_CreateOnceThenOpenOnly_OnReuse()
        {
            MakePoolReady();

            var h1 = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var panel = FindPanels()[0];

            Assert.AreEqual(1, panel.CreateCount, "首次创建应调一次 OnCreate");
            Assert.AreEqual(1, panel.OpenCount);
            Assert.AreEqual(1, panel.BindCount);

            h1.Close();
            Assert.AreEqual(1, panel.CloseCount);
            Assert.AreEqual(1, panel.UnbindCount);

            var h2 = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            Assert.AreSame(panel, FindPanels()[0], "应复用同一个实例");
            Assert.AreEqual(1, panel.CreateCount, "复用不该再调 OnCreate");
            Assert.AreEqual(2, panel.OpenCount, "复用应调 OnOpen");
            Assert.AreEqual(2, panel.BindCount, "复用应重新 Bind");

            h2.Close();
        }

        /// <summary>凭据的关闭出口：RequestClose（面板上的按钮走的就是这条路）能真正关闭面板。</summary>
        [Test]
        public void RequestClose_ClosesThePanel()
        {
            MakePoolReady();

            var handle = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var panel = FindPanels()[0];

            panel.SimulateRequestClose();     // = 按钮 onClick → RequestClose()

            Assert.IsFalse(handle.IsOpen, "RequestClose 必须真的关掉面板（走 manager.Close）");
            Assert.AreEqual(0, Manager.GetState().StackDepth);
        }

        /// <summary>关闭后实例归池：inactive 但仍在场景里（设计稿 §5.2：关闭不销毁）。</summary>
        [Test]
        public void Close_ReturnsInstanceToPool_NotDestroyed()
        {
            MakePoolReady();

            var handle = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();
            var panel = FindPanels()[0];
            Assert.IsTrue(panel.gameObject.activeSelf);

            handle.Close();

            Assert.IsTrue(panel != null, "关闭不应销毁实例");
            Assert.IsFalse(panel.gameObject.activeSelf, "归池后应为 inactive");
        }

        /// <summary>GetState 汇总正确（栈深 + 各层数量 + 明细）。</summary>
        [Test]
        public void GetState_ReflectsStackAndLayerCounts()
        {
            MakePoolReady();

            Manager.OpenAsync<TestPanelViewModel>(new ZPanelOpenOptions { Layer = ZPanelLayer.Background }).GetAwaiter().GetResult();
            Manager.OpenAsync<TestPanelViewModel>(new ZPanelOpenOptions { Layer = ZPanelLayer.Main }).GetAwaiter().GetResult();
            Manager.OpenAsync<TestPanelViewModel>(new ZPanelOpenOptions { Layer = ZPanelLayer.Main }).GetAwaiter().GetResult();

            var state = Manager.GetState();
            Assert.AreEqual(3, state.StackDepth);
            Assert.AreEqual(1, state.LayerCounts[(int)ZPanelLayer.Background]);
            Assert.AreEqual(2, state.LayerCounts[(int)ZPanelLayer.Main]);
            Assert.AreEqual(0, state.LayerCounts[(int)ZPanelLayer.Popup]);
            Assert.AreEqual(3, state.PanelStates.Count);
        }

        /// <summary>CloseAll：全部关闭、栈清空（§11-8 的一半）。</summary>
        [Test]
        public void CloseAll_ClosesEveryPanel()
        {
            MakePoolReady();

            var a = Manager.OpenAsync<TestPanelViewModel>(new ZPanelOpenOptions { Layer = ZPanelLayer.Main }).GetAwaiter().GetResult();
            var b = Manager.OpenAsync<TestPanelViewModel>(new ZPanelOpenOptions { Layer = ZPanelLayer.Popup }).GetAwaiter().GetResult();

            Manager.CloseAll();

            Assert.AreEqual(0, Manager.GetState().StackDepth);
            Assert.IsFalse(a.IsOpen);
            Assert.IsFalse(b.IsOpen);
        }
    }
}
