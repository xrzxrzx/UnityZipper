using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using Zipper.Core.Logging;
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

        long _sequence;
        bool _disposed;

        public ZPanelManager(ZPanelRoot root, IZLogger logger)
        {
            _root = root;
            _logger = logger;
            _registry = new PanelRegistry(logger);
            _stack = new List<PanelStackEntry>();
        }

        public UniTask<ZPanelHandle> OpenAsync<T>(ZPanelOpenOptions options = null) where T : ZPanelViewModel
        {
            throw new System.NotImplementedException();
        }

        public bool TryHandleBack()
        {
            if(_disposed || _stack.Count == 0)
                return false;

            PanelStackEntry top = null;
            foreach (var entry in _stack)
            {
                if(top == null
                    || entry.Layer > top.Layer
                    || (entry.Layer == top.Layer && entry.Sequence > top.Sequence))
                    top = entry;
            }

            CloseEntry(top);
            return true;
        }

        private void CloseEntry(PanelStackEntry top)
        {
            Remove(top);
        }

        private void Push(PanelStackEntry e)
        {
            _stack.Add(e);
            e.View.transform.SetParent(_root.GetLayer(e.Layer), worldPositionStays: false);
            e.View.transform.SetAsLastSibling();
        }

        private bool Remove(PanelStackEntry e)
        {
            return _stack.Remove(e);
        }

        public void Register<TViewModel, TView>(string address) where TViewModel : ZPanelViewModel where TView : ZPanel
        {
            _registry.Register<TViewModel, TView>(address);
        }

        public void CloseAll(bool destroy = false)
        {
            throw new System.NotImplementedException();
        }

        public void Dispose()
        {
            _disposed = true;
        }

        public ZPanelsState GetState()
        {
            return new ZPanelsState();
        }
    }
}