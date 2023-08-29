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

    internal class NatsUnsubscriber: INatsUnsubscriber
    {
        public CancellationToken Token => _cts.Token;

        CancellationTokenSource _cts = new CancellationTokenSource();

        public NatsUnsubscriber()
        {

        }

        public void Unsubscribe() 
        {
            try
            {
                _cts.Cancel();
            }
            catch { }
        }

    }
}
