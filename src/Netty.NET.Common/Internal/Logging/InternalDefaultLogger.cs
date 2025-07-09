using System;

namespace Netty.NET.Common.Internal.Logging
{
    public class InternalDefaultLogger : AbstractInternalLogger
    {
        public InternalDefaultLogger(string name) : base(name)
        {
        }

        public override bool isTraceEnabled()
        {
            throw new NotImplementedException();
        }

        public override void trace(string msg)
        {
            throw new NotImplementedException();
        }

        public override void trace(string format, object arg)
        {
            throw new NotImplementedException();
        }

        public override void trace(string format, object argA, object argB)
        {
            throw new NotImplementedException();
        }

        public override void trace(string format, params object[] arguments)
        {
            throw new NotImplementedException();
        }

        public override void trace(string msg, Exception t)
        {
            throw new NotImplementedException();
        }

        public override bool isDebugEnabled()
        {
            throw new NotImplementedException();
        }

        public override void debug(string msg)
        {
            throw new NotImplementedException();
        }

        public override void debug(string format, object arg)
        {
            throw new NotImplementedException();
        }

        public override void debug(string format, object argA, object argB)
        {
            throw new NotImplementedException();
        }

        public override void debug(string format, params object[] arguments)
        {
            throw new NotImplementedException();
        }

        public override void debug(string msg, Exception t)
        {
            throw new NotImplementedException();
        }

        public override bool isInfoEnabled()
        {
            throw new NotImplementedException();
        }

        public override void info(string msg)
        {
            throw new NotImplementedException();
        }

        public override void info(string format, object arg)
        {
            throw new NotImplementedException();
        }

        public override void info(string format, object argA, object argB)
        {
            throw new NotImplementedException();
        }

        public override void info(string format, params object[] arguments)
        {
            throw new NotImplementedException();
        }

        public override void info(string msg, Exception t)
        {
            throw new NotImplementedException();
        }

        public override bool isWarnEnabled()
        {
            throw new NotImplementedException();
        }

        public override void warn(string msg)
        {
            throw new NotImplementedException();
        }

        public override void warn(string format, object arg)
        {
            throw new NotImplementedException();
        }

        public override void warn(string format, params object[] arguments)
        {
            throw new NotImplementedException();
        }

        public override void warn(string format, object argA, object argB)
        {
            throw new NotImplementedException();
        }

        public override void warn(string msg, Exception t)
        {
            throw new NotImplementedException();
        }

        public override bool isErrorEnabled()
        {
            throw new NotImplementedException();
        }

        public override void error(string msg)
        {
            throw new NotImplementedException();
        }

        public override void error(string format, object arg)
        {
            throw new NotImplementedException();
        }

        public override void error(string format, object argA, object argB)
        {
            throw new NotImplementedException();
        }

        public override void error(string format, params object[] arguments)
        {
            throw new NotImplementedException();
        }

        public override void error(string msg, Exception t)
        {
            throw new NotImplementedException();
        }
    }
}