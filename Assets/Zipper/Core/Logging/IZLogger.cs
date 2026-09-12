using System;
using System.Runtime.CompilerServices;

namespace Zipper.Core.Logging
{
    public enum ZLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    public interface IZLogger
    {
        bool IsEnabled(ZLogLevel level);
        void SetGlobalLevel(ZLogLevel level);

        void Debug(string message, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
        void Info(string message, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
        void Warning(string message, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
        void Error(string message, Exception ex = null, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
        void Fatal(string message, Exception ex = null, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
    }
}
