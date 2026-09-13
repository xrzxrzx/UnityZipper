using System;

namespace Zipper.Core.Logging
{
    public readonly struct ZLogEntry
    {
        public readonly ZLogLevel Level;
        public readonly string ClassName;
        public readonly string Member;
        public readonly int Line;
        public readonly string Message;
        public readonly DateTime Time;
        public readonly int ThreadId;
        public readonly UnityEngine.Object Context;
        public readonly Exception Exception;

        public ZLogEntry(ZLogLevel level, string className, string member, int line, string message, DateTime time,
                         int threadId, UnityEngine.Object context, Exception exception = null)
        {
            Level = level;
            ClassName = className;
            Member = member;
            Line = line;
            Message = message;
            Time = time;
            ThreadId = threadId;
            Context = context;
            Exception = exception;
        }
    }
}