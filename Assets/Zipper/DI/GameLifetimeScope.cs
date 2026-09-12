using VContainer;
using VContainer.Unity;

using Zipper.Resources;
using Zipper.Core.Logging;
using Zipper.Core.Events;
using Zipper.Pool;
using Zipper.Core.Boot;

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

            #endregion

            #region Modules

            builder.Register<IZObjectPoolManager, ZObjectPoolManager>(Lifetime.Singleton);//对象池管理器
            builder.Register<IZResourceManager, ZResourceManager>(Lifetime.Singleton);//资源管理器
            builder.Register<IZLogger, ZLogger>(Lifetime.Singleton);//日志模块
            builder.Register<IZEventBus, ZEventBus>(Lifetime.Singleton);//事件总线

            #endregion
        }
    }
}
