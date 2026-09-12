using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VContainer.Unity;
using Zipper.Core.Boot;

namespace Zipper.DI
{
    internal class ZBootstrapper : IAsyncStartable
    {
        readonly IReadOnlyList<IZModuleBootstrap> _bootstraps;

        public ZBootstrapper(IEnumerable<IZModuleBootstrap> bootstraps)
        {
            _bootstraps = bootstraps.ToList();
        }

        public async UniTask StartAsync(CancellationToken cancellation = default)
        {
            await RunPhaseAsync(ZBootPhase.Logging, cancellation);
            await RunPhaseAsync(ZBootPhase.Resources, cancellation);
            await RunPhaseAsync(ZBootPhase.Pools, cancellation);
            await RunPhaseAsync(ZBootPhase.Events, cancellation);
            await RunPhaseAsync(ZBootPhase.UI, cancellation);
        }

        private async UniTask RunPhaseAsync(ZBootPhase phase, CancellationToken cancellation)
        {
            foreach (var bootstrap in _bootstraps.Where(b => b.Phase == phase))
            {
                await bootstrap.InitializeAsync(cancellation);
            }
        }
    }
}