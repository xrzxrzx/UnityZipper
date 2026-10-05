using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using VContainer;
using Zipper.Core.Logging;
using Zipper.Pool;
using Zipper.Resources;
using Zipper.Resources.Asset;
using Zipper.UI.Contract;
using Zipper.UI.Register;

namespace Zipper.UI
{
    public class ZPanelManager : IZPanelManager
    {
        PanelRegistry _registry;
        ZPanelRoot _root;//bootstrapper传入

        List<PanelStackEntry> _stack;

        IZLogger _logger;
        IZObjectPoolManager _poolManager;
        IZResourceManager _resourceManager;
        IZUISfx _sfx;
        IObjectResolver _resolver;

        long _sequence;
        bool _disposed;

        ZPanelOpenOptions DefaultOptions = new ZPanelOpenOptions();

        public ZPanelManager(ZPanelRoot root, IZLogger logger, IZObjectPoolManager poolManager, IZResourceManager resourceManager, IZUISfx sfx, IObjectResolver resolver)

        {
            _root = root;
            _logger = logger;
            _poolManager = poolManager;
            _sfx = sfx;
            _resolver = resolver;
            _resourceManager = resourceManager;
            _registry = new PanelRegistry(logger);
            _stack = new List<PanelStackEntry>();
        }

        public async UniTask<ZPanelHandle> OpenAsync<T>(ZPanelOpenOptions options = null) where T : ZPanelViewModel
        {
            if (_disposed)
                return default;

            var vmType = typeof(T);
            if (!_registry.TryGet(vmType, out var reg))
            {
                _logger.Error($"面板未注册：{vmType.Name}");
                throw new InvalidOperationException($"面板未注册：{vmType.Name}");
            }

            var opts = options ?? DefaultOptions;

            if (!await EnsurePoolAsync(reg))
            {
                throw new InvalidOperationException($"面板母本不可用：{reg.Address}");
            }

            if (!_resolver.TryResolve(vmType, out var vmObj) || vmObj is not ZPanelViewModel vm)
            {
                _logger.Error($"VM 未注册到容器：{vmType.Name}");
                throw new InvalidOperationException($"VM 未注册到容器：{vmType.Name}");
            }
            if (vm == null)
            {
                _logger.Error($"VM 构造失败：{vmType.Name}");
                throw new InvalidOperationException($"VM 构造失败：{vmType.Name}");
            }

            var view = GetPanelItem(reg);
            if (view == null)
            {
                if (reg.OwnsViewModel)
                    vm.Dispose();
                throw new InvalidOperationException($"取面板实例失败：{reg.ViewType.Name}");
            }

            view.SetSfx(_sfx);//注入音效接口

            try
            {
                view.Bind(vm);
                view.Open();
            }
            catch (Exception ex)
            {
                _logger.Error($"面板打开失败，已回滚：{reg.ViewType.Name}", ex);

                try
                {
                    view.Unbind();
                }
                catch
                {

                }
                ReturnPanel(view);

                if (reg.OwnsViewModel)
                    vm.Dispose();

                throw;
            }

            var handle = new ZPanelHandle(this);
            view.CloseRequester = () => Close(handle);

            var entry = new PanelStackEntry(view, vm, handle, opts.Layer, opts, ++_sequence, reg.OwnsViewModel);
            Push(entry);

            return handle;
        }

        private async UniTask<bool> EnsurePoolAsync(Register.Registration reg)
        {
            if (reg.PoolCreated)
                return true;

            PrefabAsset prefab = null;
            try
            {
                prefab = await _resourceManager.LoadPrefabAsync(reg.Address, nameof(ZPanelManager));
            }
            catch (Exception ex)
            {
                _logger.Error($"面板母本加载失败：{reg.Address}", ex);
                return false;
            }

            if (prefab == null || prefab.Prefab == null)
            {
                _logger.Error($"面板母本为空：{reg.Address}");
                return false;
            }

            reg.Prefab = prefab;
            try
            {
                reg.CreatePool(_poolManager, prefab.Prefab);
            }
            catch (Exception ex)
            {
                _logger.Error($"面板池创建失败：{reg.Address}", ex);
                prefab.Dispose();
                reg.Prefab = null;
                return false;
            }
            reg.PoolCreated = true;

            return true;
        }

        public void Register<TViewModel, TView>(string address, bool ownsViewModel = true)
                            where TViewModel : ZPanelViewModel where TView : ZPanel
        {
            _registry.Register<TViewModel, TView>(address, ownsViewModel);
        }

        public bool TryHandleBack()
        {
            if (_disposed || _stack.Count == 0)
                return false;

            PanelStackEntry top = null;
            foreach (var entry in _stack)
            {
                if (top == null
                    || entry.Layer > top.Layer
                    || (entry.Layer == top.Layer && entry.Sequence > top.Sequence))
                    top = entry;
            }

            CloseEntry(top);
            return true;
        }

        private void Push(PanelStackEntry e)
        {
            _stack.Add(e);
            e.View.transform.SetParent(_root.GetLayer(e.Layer), worldPositionStays: false);
            e.View.transform.SetAsLastSibling();
        }

        private ZPanel GetPanelItem(Register.Registration reg)
        {
            var panel = reg.GetItem(_poolManager);
            if (panel == null)//理论上不会未满，因为没有设置上限
                _logger.Warning($"取面板实例失败（池已满或未建）：{reg.ViewType.Name}");

            return panel;
        }

        private void ReturnPanel(ZPanel panel)
        {
            panel.ReturnSelf();
        }

        void ReleaseAllPools()
        {
            foreach (var reg in _registry.All)
                ReleasePool(reg);
        }

        void ReleasePool(Register.Registration reg)
        {
            if (!reg.PoolCreated)
                return;

            reg.DestroyPool(_poolManager);
            reg.Prefab?.Dispose();
            reg.Prefab = null;
            reg.PoolCreated = false;
        }

        internal void Close(ZPanelHandle handle)
        {
            if (!handle.IsOpen)
                return;

            var entry = _stack.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry == null)
            {
                handle.MarkClosed();
                return;
            }

            CloseEntry(entry);
        }

        private void CloseEntry(PanelStackEntry entry)
        {
            if (!_stack.Remove(entry))
                return;

            entry.View.SetSfx(null);
            entry.View.Close();
            entry.View.Unbind();

            entry.View.CloseRequester = null;
            ReturnPanel(entry.View);

            if (entry.OwnsViewModel)
                entry.ViewModel?.Dispose();

            entry.Handle.MarkClosed();
        }

        public void CloseAll(bool destroy = false)
        {
            if (_disposed)
                return;

            var ordered = new List<PanelStackEntry>(_stack);

            ordered.Sort((a, b) => a.Layer != b.Layer
                ? b.Layer.CompareTo(a.Layer)
                : b.Sequence.CompareTo(a.Sequence));

            foreach (var e in ordered)
                CloseEntry(e);

            if (destroy)
                ReleaseAllPools();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CloseAll(true);

            _disposed = true;
        }

        public ZPanelsState GetState()
        {
            var layerCounts = new int[5];
            var panels = new List<ZPanelState>(_stack.Count);

            foreach (var e in _stack)
            {
                layerCounts[(int)e.Layer]++;
                panels.Add(new ZPanelState(e.ViewModel.GetType(), e.Layer, e.Handle.IsOpen));
            }

            return new ZPanelsState(_stack.Count, layerCounts, panels);
        }
    }
}