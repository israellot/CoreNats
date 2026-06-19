using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CoreNats
{
    public interface INatsUnsubscriber
    {
        void Unsubscribe();
    }

    internal class NatsUnsubscriber : INatsUnsubscriber, IDisposable
    {
        public CancellationToken Token => _cts.Token;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _disposed;

        public NatsUnsubscriber()
        {
        }

        public void Unsubscribe()
        {
            // Cancel first so any in-flight InternalSubscribe registration fires
            // and the subscription loop observes cancellation before we dispose.
            // CancellationTokenSource.Dispose() after Cancel() is safe per .NET docs:
            // all registered callbacks have already been invoked or scheduled at the
            // point Cancel() returns, so there is no live registration that could
            // receive an ObjectDisposedException from the disposed CTS.
            try
            {
                _cts.Cancel();
            }
            catch { }

            Dispose();
        }

        public void Dispose()
        {
            // Guard against double-dispose (e.g. Unsubscribe() followed by explicit Dispose()).
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _cts.Dispose();
            }
        }
    }
}
