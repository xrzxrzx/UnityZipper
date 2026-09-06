using Cysharp.Threading.Tasks;
using System.Threading;
using VContainer;
using VContainer.Unity;
using Zipper.Resources;

namespace Zipper.DI
{
    public class ResourcesBootstrapper : IAsyncStartable
    {
        [Inject]
        readonly IZResourceManager _resourceManager;

        public async UniTask StartAsync(CancellationToken cancellation = default)
        {
            await _resourceManager.InitializeAsync(cancellation);
        }
    }
}