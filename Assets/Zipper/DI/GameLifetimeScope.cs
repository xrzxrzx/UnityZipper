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
            if (_panelRoot != null)
            {
                builder.RegisterInstance(_panelRoot);
            }
            else
            {
                Debug.LogError("[Zipper] GameLifetimeScope 未配置 ZPanelRoot");
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
        }

        /// <summary>
        /// Panel面板注册
        /// </summary>
        /// <param name="builder"></param>
        protected virtual void ConfigureProject(IContainerBuilder builder) 
        {
            //示例
            //RegisterPanel<TestPanelViewModel, TestPanel>(builder, Lifetime.Transient, "UI/TestPanel");
        }
    }
}
