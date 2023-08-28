namespace CoreNats
{
    using System;
   using CoreNats.Messages;

    public class NatsDeserializeException : Exception
    {
        public NatsMsg Msg { get; }
        
        public NatsDeserializeException(NatsMsg msg, Exception innerException)
            : base(innerException.Message, innerException)
        {
            Msg = msg;
        }
    }
}