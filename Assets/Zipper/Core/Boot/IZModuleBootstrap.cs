using Cysharp.Threading.Tasks;
using System.Threading;

namespace Zipper.Core.Boot
{
    public enum ZBootPhase
    {
        Logging,
        Events,
        Resources,
        Pools,
        Audio,
        UI,
    }

    public interface IZModuleBootstrap
    {
        ZBootPhase Phase { get; }
        UniTask InitializeAsync(CancellationToken ct);
    }
}