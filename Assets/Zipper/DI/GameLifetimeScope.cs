using VContainer;
using VContainer.Unity;

using Zipper.Resources;
using Zipper.Core.Logging;
using Zipper.Core.Events;
using Zipper.Pool;
using Zipper.Core.Boot;
using Zipper.Audio;

namespace Zipper.DI
{
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            #region Bootstrappers

            builder.RegisterEntryPoint<ZBootstrapper>(Lifetime.Singleton);//引导器

            builder.Register<IZModuleBootstrap, ZLoggerBootstrapper>(Lifetime.Singleton);//日志模块引导器
            builder.Register<IZModuleBootstrap, ZResourcesBootstrapper>(Lifetime.Singleton);//资源模块引导器
            builder.Register<IZModuleBootstrap, ZAudioBootstrapper>(Lifetime.Singleton);//音频模块引导器

            #endregion

            #region Modules

            builder.Register<IZObjectPoolManager, ZObjectPoolManager>(Lifetime.Singleton);//对象池管理器
            builder.Register<IZResourceManager, ZResourceManager>(Lifetime.Singleton);//资源管理器
            builder.Register<IZLogger, ZLogger>(Lifetime.Singleton).AsSelf();//日志模块，使用AsSelf()是为了暴露ZLogger的具体实现方便Bootstrapper初始化
            builder.Register<IZEventBus, ZEventBus>(Lifetime.Singleton);//事件总线
            builder.Register<IZAudioManager, ZAudioManager>(Lifetime.Singleton).As<ITickable>();//音频管理器
            builder.RegisterInstance(new ZAudioOptions()).AsSelf();//音频配置

            #endregion
        }
    }
}
