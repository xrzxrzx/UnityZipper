using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using Zipper.Pool;
using Zipper.Tests.Fakes;
using Zipper.UI;
using Zipper.UI.Register;

namespace Zipper.Tests
{
    /// <summary>
    /// 面板测试基类：为每个用例搭出"真管理器 + 真池 + 真场景层级"的最小环境。
    ///
    /// <para><b>为什么能做到"真开面板"</b>：<c>PrefabAsset</c> / <c>AssetHandle&lt;T&gt;</c> 的构造是 internal
    /// （只能由 <c>ZResourceManager</c> 签发）→ 假资源管理器造不出真母本。
    /// 所以这里绕过 <c>LoadPrefabAsync</c>：先用【真池管理器 + 一个挂了 TestPanel 的 GameObject 当母本】建池，
    /// 再把 <c>Registration.PoolCreated</c> 置 true，让 <c>EnsurePoolAsync</c> 短路。
    /// 于是"取实例 → Bind → Open → 入栈 → Close → Unbind → 归池"这条链路全走真的 ✓</para>
    ///
    /// <para><b>因此不覆盖</b>：真的 Addressable 母本加载（属冒烟/手工验收范畴，已登记在验收报告的"未覆盖项"）。</para>
    /// </summary>
    public abstract class PanelTestBase
    {
        protected FakeLogger Log;
        protected ZObjectPoolManager Pools;
        protected FakeResourceManager Resources;
        protected FakeUiSfx Sfx;
        protected ZPanelRoot Root;
        protected ZPanelManager Manager;
        protected IObjectResolver Resolver;

        readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>池母本。★ 它也挂着 TestPanel 组件（池要求母本带该组件），所以会被 FindPanels 找到 → 必须排除。</summary>
        GameObject _prefabGo;

        [SetUp]
        public void SetUp()
        {
            TestPanelViewModel.All.Clear();

            Log = new FakeLogger();
            Pools = new ZObjectPoolManager(Log);
            Resources = new FakeResourceManager();
            Sfx = new FakeUiSfx();

            Root = SpawnRoot();

            var builder = new ContainerBuilder();
            builder.Register<TestPanelViewModel>(Lifetime.Transient);   // VM 必须注册，OpenAsync 才能 Resolve
            Resolver = builder.Build();

            Manager = new ZPanelManager(Root, Log, Pools, Resources, Sfx, Resolver);
        }

        [TearDown]
        public void TearDown()
        {
            // 先清掉池 Instantiate 出来的面板实例（它们不在 _spawned 里），再清本测试创建的场景对象
            foreach (var p in FindPanels())
                if (p != null) Object.DestroyImmediate(p.gameObject);

            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);

            _spawned.Clear();
            TestPanelViewModel.All.Clear();
        }

        // ── 环境搭建 ──

        protected GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        ZPanelRoot SpawnRoot()
        {
            var go = Spawn("ZPanelRoot");
            var root = go.AddComponent<ZPanelRoot>();

            // ZPanelRoot 的 5 个字段是 private [SerializeField] → 用 SerializedObject 设置（不改生产代码）
            var so = new UnityEditor.SerializedObject(root);
            so.FindProperty("_background").objectReferenceValue = Spawn("Layer_Background").transform;
            so.FindProperty("_main").objectReferenceValue       = Spawn("Layer_Main").transform;
            so.FindProperty("_popup").objectReferenceValue      = Spawn("Layer_Popup").transform;
            so.FindProperty("_toast").objectReferenceValue      = Spawn("Layer_Toast").transform;
            so.FindProperty("_loading").objectReferenceValue    = Spawn("Layer_Loading").transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        /// <summary>注册测试面板（框架侧的面板类型表）。</summary>
        protected void RegisterTestPanel()
            => Manager.Register<TestPanelViewModel, TestPanel>("UI/TestPanel");

        /// <summary>让注册表以为"池已建"，从而跳过需要真 Addressable 母本的加载步骤（见类注释）。</summary>
        protected void MakePoolReady()
        {
            _prefabGo = Spawn("TestPanelPrefab");
            _prefabGo.AddComponent<TestPanel>();
            Pools.CreatePool(new ZPoolOptions<TestPanel> { Prefab = _prefabGo });

            var registry = (PanelRegistry)typeof(ZPanelManager)
                .GetField("_registry", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(Manager);

            Assert.IsTrue(registry.TryGet(typeof(TestPanelViewModel), out var reg), "面板未注册");
            reg.PoolCreated = true;      // ★ 短路 EnsurePoolAsync
        }

        /// <summary>用一个【不注册任何 VM】的容器重建管理器（用于验证"VM 未注册到容器"的失败语义）。</summary>
        protected void RebuildManagerWithEmptyContainer()
        {
            Resolver = new ContainerBuilder().Build();
            Manager = new ZPanelManager(Root, Log, Pools, Resources, Sfx, Resolver);
        }

        /// <summary>
        /// 重建为"**空注册表 + 已注册 VM**"的干净管理器。
        /// 用途：需要"只有本用例注册过面板"的场景 —— 基类 [SetUp] 已用默认值注册过，
        /// 而 registry 拒绝重复注册（记 Warning），所以必须先换一个空注册表。
        /// </summary>
        protected void ResetManagerFresh()
        {
            var builder = new ContainerBuilder();
            builder.Register<TestPanelViewModel>(Lifetime.Transient);   // ★ 容器必须有 VM，否则 Resolve 抛
            Resolver = builder.Build();
            Manager = new ZPanelManager(Root, Log, Pools, Resources, Sfx, Resolver);   // 内部是新的空 PanelRegistry
        }

        // ── 查询辅助 ──

        /// <summary>
        /// 当前场景里的面板**实例**（含归池后 inactive 的）。
        /// ★ 必须排除池母本：母本也挂着 TestPanel 组件 ✗ 不排除会让所有"取第一个实例"的断言全部错位。
        /// </summary>
        protected TestPanel[] FindPanels()
        {
            var all = Object.FindObjectsByType<TestPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var result = new List<TestPanel>();
            foreach (var p in all)
                if (p != null && p.gameObject != _prefabGo)
                    result.Add(p);
            return result.ToArray();
        }
    }
}
