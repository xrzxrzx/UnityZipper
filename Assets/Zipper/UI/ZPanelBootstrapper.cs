using Cysharp.Threading.Tasks;
using System.Threading;
using Zipper.Core.Boot;
using Zipper.Core.Logging;
using Zipper.UI.Contract;

namespace Zipper.UI
{
    public class ZPanelBootstrapper : IZModuleBootstrap
    {
        public ZBootPhase Phase => ZBootPhase.UI;

        readonly IZPanelManager _panelManager;
        readonly ZPanelRoot _root;
        readonly IZLogger _logger;

        public ZPanelBootstrapper(IZPanelManager panelManager, ZPanelRoot root, IZLogger logger)
        {
            _panelManager = panelManager;
            _root = root;
            _logger = logger;
        }

        public UniTask InitializeAsync(CancellationToken ct)
        {
            // 层级根由组装层保证已就绪：
            //   配了场景实例 → 用它；没配或配成了 prefab 资产 → 组装层已自动创建 ✓
            // 这里只做一次体检：5 层都必须有 Transform
            for (var layer = ZPanelLayer.Background; layer <= ZPanelLayer.Loading; layer++)
            {
                if (_root.GetLayer(layer) == null)
                    _logger.Error($"ZPanelRoot 的 {layer} 层未绑定 Transform（实例被破坏，或运行时创建失败）");
            }

            return UniTask.CompletedTask;
        }
    }
}