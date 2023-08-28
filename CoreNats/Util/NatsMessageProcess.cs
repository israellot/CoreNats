namespace CoreNats
{
   using CoreNats.Messages;

    public delegate void NatsMessageProcess(ref NatsInlineMsg message);
}
