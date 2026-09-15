using System;
using System.Runtime.CompilerServices;

namespace Zipper.Core.Logging
{
    public class ZLogger : IZLogger
    {
        ZLogRouter _router;

        internal void Attach(ZLogRouter router)
        {
            _router = router;
        }

        public void Debug(string message, string className = null, [CallerMemberName] string member = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null)
        {
            if (_router == null)
                return;

           _router.Dispatch(ZLogLevel.Debug, null, className, member, filePath, line, message, context);
        }

        public void Info(string message, string className = null, [CallerMemberName] string member = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null)
        {
            if (_router == null)
                return;

            _router.Dispatch(ZLogLevel.Info, null, className, member, filePath, line, message, context);
        }

        public void Warning(string message, string className = null, [CallerMemberName] string member = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null)
        {
            if (_router == null)
                return;

            _router.Dispatch(ZLogLevel.Warning, null, className, member, filePath, line, message, context);
        }

        public void Error(string message, Exception ex = null, string className = null, [CallerMemberName] string member = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null)
        {
            if (_router == null)
                return;

            _router.Dispatch(ZLogLevel.Error, ex, className, member, filePath, line, message, context);
        }

        public void Fatal(string message, Exception ex = null, string className = null, [CallerMemberName] string member = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null)
        {
            if (_router == null)
                return;

            _router.Dispatch(ZLogLevel.Fatal, ex, className, member, filePath, line, message, context);
        }

        public bool IsEnabled(ZLogLevel level)
        {
            if (_router == null)
                return false;

            return _router.IsEnable(level);
        }

        public void SetGlobalLevel(ZLogLevel level)
        {
            if (_router == null)
                return;

            _router.SetGlobalLevel(level);
        }

        public void Dispose()
        {
            _router = null;
        }
    }
}