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
            if (_root == null)
            {
                _logger.Error("未配置 ZPanelRoot");
                return UniTask.CompletedTask;
            }

            for (var layer = ZPanelLayer.Background; layer <= ZPanelLayer.Loading; layer++)
            {
                if (_root.GetLayer(layer) == null)
                {
                    _logger.Error($"ZPanelRoot 的 {layer} 层未绑定 Transform");
                }
            }

            return UniTask.CompletedTask;
        }
    }
}