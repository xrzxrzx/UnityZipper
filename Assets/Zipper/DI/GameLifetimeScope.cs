using VContainer;
using VContainer.Unity;

using Zipper.Resources;

namespace Zipper.DI
{
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            #region Zipper

            builder.RegisterEntryPoint<ResourcesBootstrapper>(Lifetime.Singleton);

            builder.Register<IZResourceManager, ZResourceManager>(Lifetime.Singleton);

            #endregion
        }
    }
}
