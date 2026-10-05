using System;
using NUnit.Framework;
using UnityEngine.TestTools;
using Zipper.Tests.Fakes;
using Zipper.UI;

namespace Zipper.Tests
{
    /// <summary>
    /// 失败语义（对应设计稿 §11-7、§7.1 与 S3 的 T12）：
    /// 未注册、VM 未注册到容器、母本加载失败 —— 都要**明确失败且不污染栈**。
    ///
    /// <para>注意：这里**故意不调</b> <c>MakePoolReady()</c>，让 <c>EnsurePoolAsync</c> 走真加载路径，
    /// 由 <see cref="FakeResourceManager.ThrowOn"/> 制造失败。</para>
    /// </summary>
    public class PanelAsyncTests : PanelTestBase
    {
        [SetUp]
        public void RegisterPanelUnderTest() => RegisterTestPanel();

        /// <summary>§7.1：未注册的 VM 类型 → 抛明确异常 + 记 Error + 栈不动。</summary>
        [Test]
        public void OpenAsync_UnregisteredViewModel_Throws()
        {
            var manager = CreateManagerWithEmptyRegistry();

            var ex = Assert.Throws<InvalidOperationException>(
                () => manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult());

            StringAssert.Contains("面板未注册", ex.Message);
            Assert.AreEqual(0, manager.GetState().StackDepth, "失败不得入栈");
            Assert.IsTrue(Log.Errors.Count > 0, "应记 Error（不静默）");
        }

        /// <summary>T12：VM 没注册到容器 → 抛出的信息应直接指向"忘了注册"。</summary>
        [Test]
        public void OpenAsync_VmNotRegisteredInContainer_Throws()
        {
            RebuildManagerWithEmptyContainer();     // 容器里没有任何 VM
            RegisterTestPanel();                    // 面板类型表已注册
            MakePoolReady();                        // 池已就绪（把失败点精确落在 Resolve 上）

            var ex = Assert.Throws<InvalidOperationException>(
                () => Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult());

            StringAssert.Contains("未注册到容器", ex.Message);
            Assert.AreEqual(0, Manager.GetState().StackDepth);
        }

        /// <summary>★ §11-7：母本加载失败 → 抛 + 记 Error + **栈无残留**。</summary>
        [Test]
        public void OpenAsync_PrefabLoadFails_ThrowsAndStackStaysClean()
        {
            Resources.ThrowOn = _ => new Exception("boom（模拟 Addressables 加载失败）");

            var ex = Assert.Throws<InvalidOperationException>(
                () => Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult());

            StringAssert.Contains("母本", ex.Message);
            Assert.AreEqual(0, Manager.GetState().StackDepth, "加载失败不得污染栈");
            Assert.IsTrue(Log.Errors.Count > 0, "加载失败应记 Error");
            Assert.AreEqual(0, FindPanels().Length, "失败时不该留下任何面板实例");
        }

        /// <summary>母本加载失败后**可以重试**：第二次（修好之后）应能正常打开。</summary>
        [Test]
        public void OpenAsync_AfterLoadFailure_CanRetry()
        {
            Resources.ThrowOn = _ => new Exception("第一次失败");
            Assert.Throws<InvalidOperationException>(
                () => Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult());
            Assert.AreEqual(0, Pools.GetPoolsState().AllPoolsCount, "失败不该留下池");

            Resources.ThrowOn = null;      // 修好
            MakePoolReady();               // 用真池接管（见基类注释）
            var h = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();

            Assert.IsTrue(h.IsOpen);
            Assert.AreEqual(1, Manager.GetState().StackDepth);
            h.Close();
        }

        /// <summary>
        /// Dispose 之后调 OpenAsync：当前实现是"静默返回 default"（即一个 **null handle**）。
        /// 本用例把这个**行为**钉住（防回归），并暴露它的风险：调用方拿到 null 再调 Close() 会 NRE。
        /// 见验收报告 §4 的 P3 登记。
        /// </summary>
        [Test]
        public void OpenAsync_AfterDispose_ReturnsNullHandleSilently()
        {
            Manager.Dispose();

            var handle = Manager
                .OpenAsync<TestPanelViewModel>()
                .GetAwaiter().GetResult();

            Assert.IsNull(handle, "当前实现：Dispose 后静默返回 default（null handle）—— 已登记为待改进项");
        }

        /// <summary>Dispose 应关掉全部面板并拆池、释放母本（§7.2）。</summary>
        [Test]
        public void Dispose_ClosesEverythingAndReleasesPools()
        {
            // ⚠️ EditMode 的固有限制：对象池销毁实例时用 Object.Destroy，
            //    而 EditMode 下只允许 DestroyImmediate → Unity 会打一条
            //    [Error] "Destroy may not be called from edit mode!"
            //    这是环境限制（运行时用 Destroy 是正确的，生产代码无需改）→ 本用例内忽略该日志。
            LogAssert.ignoreFailingMessages = true;
            try
            {
                MakePoolReady();
                var h = Manager.OpenAsync<TestPanelViewModel>().GetAwaiter().GetResult();

                Assert.AreEqual(1, Pools.GetPoolsState().AllPoolsCount);

                Manager.Dispose();

                Assert.IsFalse(h.IsOpen, "Dispose 应关闭在栈面板");
                Assert.AreEqual(0, Pools.GetPoolsState().AllPoolsCount, "Dispose 应拆掉池");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>造一个"注册表为空"的管理器（用于测未注册路径）。</summary>
        ZPanelManager CreateManagerWithEmptyRegistry()
        {
            RebuildManagerWithEmptyContainer();   // 顺带得到全新的（空）注册表
            Resources.ThrowOn = null;
            return Manager;
        }
    }
}
