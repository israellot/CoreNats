namespace CoreNats
{
   using CoreNats.Messages;

    public delegate void NatsMessageInlineProcess(ref NatsInlineMsg message);

    public delegate void NatsMessageProcess(NatsMsg message);
}
