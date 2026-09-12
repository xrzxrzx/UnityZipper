using Cysharp.Threading.Tasks;
using System.Threading;
using Zipper.Core.Boot;

namespace Zipper.Core.Logging
{
    public class ZLoggerBootstrapper : IZModuleBootstrap
    {
        public ZBootPhase Phase => ZBootPhase.Logging;
        readonly IZLogger _logger;
        public ZLoggerBootstrapper(IZLogger logger)
        {
            _logger = logger;
        }
        public async UniTask InitializeAsync(CancellationToken ct)
        {
            
        }
    }
}