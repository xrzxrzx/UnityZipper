using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Zipper.Audio;
using Zipper.Core.Boot;
using Zipper.Core.Events;
using Zipper.Core.Logging;
using Zipper.Pool;
using Zipper.Resources;
using Zipper.UI;
using Zipper.Demo;
using Cysharp.Threading.Tasks.Triggers;

namespace Zipper.DI
{
    public class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] ZPanelRoot _panelRoot;

        readonly List<Action<IZPanelManager>> _pendingPanelRegistrations = new List<Action<IZPanelManager>>();

        protected void RegisterPanel<TViewModel, TPanel>(IContainerBuilder builder, Lifetime lifetime, string address, bool ownsViewModel = true)
        where TViewModel : ZPanelViewModel
        where TPanel : ZPanel
        {
            builder.Register<TViewModel>(lifetime);                    
            _pendingPanelRegistrations.Add(p => p.Register<TViewModel, TPanel>(address, ownsViewModel));
        }

        protected override void Configure(IContainerBuilder builder)
        {
            // ── UI 层级根 ──
            // 只接受【场景里】的实例：prefab 资产里的 GameObject 其 scene 是 invalid ✗
            //   （在 prefab 实例的 Inspector 上拖引用只能拖到【资产】→ 那个引用无法作为层级父节点，
            //     会让 ZPanelManager.Push 报 "SetParent ... resides in a Prefab Asset" ✗）
            // 没配 / 配成了资产 → 直接运行时自建 5 层层级 ✓（从此不依赖任何场景引用 ✓）
            if (_panelRoot != null && _panelRoot.gameObject.scene.IsValid())
            {
                builder.RegisterInstance(_panelRoot);
            }
            else
            {
                if (_panelRoot != null)
                    Debug.LogWarning("[Zipper] GameLifetimeScope 的 ZPanelRoot 指向的是 Prefab 资产（不是场景实例）→ 已忽略，改为自动创建层级");
                else
                    Debug.Log("[Zipper] 未配置 ZPanelRoot → 已自动创建 5 层层级（Background/Main/Popup/Toast/Loading）");

                builder.RegisterInstance(ZPanelRoot.CreateRuntime());
            }

            #region Bootstrappers

            builder.RegisterEntryPoint<ZBootstrapper>(Lifetime.Singleton);//引导器

            builder.Register<IZModuleBootstrap, ZLoggerBootstrapper>(Lifetime.Singleton);//日志模块引导器
            builder.Register<IZModuleBootstrap, ZResourcesBootstrapper>(Lifetime.Singleton);//资源模块引导器
            builder.Register<IZModuleBootstrap, ZAudioBootstrapper>(Lifetime.Singleton);//音频模块引导器
            builder.Register<IZModuleBootstrap, ZPanelBootstrapper>(Lifetime.Singleton);//UI模块引导器

            #endregion

            #region Modules

            builder.Register<IZObjectPoolManager, ZObjectPoolManager>(Lifetime.Singleton);//对象池管理器
            builder.Register<IZResourceManager, ZResourceManager>(Lifetime.Singleton);//资源管理器
            builder.Register<IZLogger, ZLogger>(Lifetime.Singleton).AsSelf();//日志模块，使用AsSelf()是为了暴露ZLogger的具体实现方便Bootstrapper初始化
            builder.Register<IZEventBus, ZEventBus>(Lifetime.Singleton);//事件总线
            builder.Register<IZAudioManager, ZAudioManager>(Lifetime.Singleton).As<ITickable>();//音频管理器
            builder.RegisterInstance(new ZAudioOptions()).AsSelf();//音频配置
            builder.Register<IZPanelManager, ZPanelManager>(Lifetime.Singleton);//UI面板管理器
            builder.Register<IZUISfx, ZUiSfxAdapter>(Lifetime.Singleton);//UI接音频

            #endregion

            #region 面板自动注册

            builder.RegisterBuildCallback(container => 
            {
                var panels = container.Resolve<IZPanelManager>();
                foreach (var r in _pendingPanelRegistrations) r(panels);
                _pendingPanelRegistrations.Clear();
            });

            ConfigureProject(builder);

            #endregion

            builder.Register<CounterViewModel>(Lifetime.Transient);   // ★ VM 必须注册到容器
            builder.Register<SyncViewModel>(Lifetime.Transient);
        }

        /// <summary>
        /// Panel面板注册
        /// </summary>
        /// <param name="builder"></param>
        protected virtual void ConfigureProject(IContainerBuilder builder) 
        {
            //示例
            //RegisterPanel<TestPanelViewModel, TestPanel>(builder, Lifetime.Transient, "UI/TestPanel");
            RegisterPanel<CounterViewModel, CounterPanel>(builder, Lifetime.Transient, "UI/CounterPanel");
            RegisterPanel<SyncViewModel, SyncPanel>(builder, Lifetime.Transient, "UI/SyncPanel");
        }
    }
}
