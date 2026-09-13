using UnityEngine;

namespace Zipper.Core.Logging
{
    public class ZMainThreadDispatcherDriver : MonoBehaviour
    {
        ZMainThreadDispatcher _dispatcher;

        public void Init(ZMainThreadDispatcher dispatcher) => _dispatcher = dispatcher;

        public void Update()
        {
            _dispatcher?.Pump();
        }
    }
}