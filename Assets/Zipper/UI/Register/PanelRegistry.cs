using System;
using System.Collections.Generic;
using Zipper.Core.Logging;

namespace Zipper.UI.Register
{
    internal sealed class PanelRegistry
    {
        Dictionary<Type, Registration> _byViewModel;

        IZLogger _logger;

        public IReadOnlyCollection<Registration> All => _byViewModel.Values;

        public PanelRegistry(IZLogger logger)
        {
            _byViewModel = new Dictionary<Type, Registration>();
            _logger = logger;
        }

        public void Register<TViewModel, TView>(string address)
                            where TViewModel : ZPanelViewModel
                            where TView : ZPanel
        {
            if (string.IsNullOrEmpty(address))
            {
                _logger.Error($"面板注册失败，Address 为空，ViewModel: {typeof(TViewModel).Name}, View: {typeof(TView).Name}");
                return;
            }

            var vmType = typeof(TViewModel);
            if (_byViewModel.ContainsKey(vmType))
            {
                _logger.Warning($"面板注册失败，重复注册，ViewModel: {vmType.Name}, View: {typeof(TView).Name}");
                return;
            }

            var registration = new Registration(
                vmType,
                typeof(TView),
                address,
                (poolManager, prefab) => poolManager.CreatePool(new Pool.ZPoolOptions<TView> { Prefab = prefab }),
                (poolManager) => poolManager.DestroyPool<TView>(),
                (poolManager) => poolManager.Get<TView>()
            );
            _byViewModel[vmType] = registration;
        }

        public bool TryGet(Type viewModelType, out Registration registration)
        {
            return _byViewModel.TryGetValue(viewModelType, out registration);
        }

        public void Clear()
        {
            _byViewModel.Clear();
        }
    }
}