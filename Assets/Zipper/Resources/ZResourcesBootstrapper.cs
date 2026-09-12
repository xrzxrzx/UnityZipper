using Cysharp.Threading.Tasks;
using System.Threading;
using Zipper.Core.Boot;

namespace Zipper.Resources
{
    public class ZResourcesBootstrapper : IZModuleBootstrap
    {
        public ZBootPhase Phase => ZBootPhase.Resources;
    
        readonly IZResourceManager _resourceManager;

        public ZResourcesBootstrapper(IZResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
        }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            await _resourceManager.InitializeAsync(ct);
        }
    }
}