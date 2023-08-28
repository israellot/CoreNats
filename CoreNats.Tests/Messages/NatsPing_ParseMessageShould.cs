﻿namespace CoreNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using CoreNats;
   using CoreNats.Messages;
    using Xunit;

    public class NatsPing_ParseMessageShould
    {
        private ReadOnlyMemory<byte> _message = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("PING\r\n"));

        [Fact]
        public void ReturnNatsPing()
        {
            var ping = new NatsMessageParser().ParsePing();
            Assert.IsType<NatsPing>(ping);
        }
    }
}