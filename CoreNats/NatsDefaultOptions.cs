

namespace CoreNats
{
    using System;
    using System.Buffers;
    using System.Net;
    using Microsoft.Extensions.Logging;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
   using CoreNats.Messages;

    public class NatsDefaultOptions : INatsOptions
    {
        public NatsDefaultOptions()
        {            
            Servers = new string[] { "127.0.0.1:4222" };
            DnsResolver = Dns.GetHostAddressesAsync;
            ServerPoolFactory = (o) => new NatsServerPool(o);
            Serializer = new NatsDefaultSerializer();
            ArrayPool = ArrayPool<byte>.Create(1024*1024, 1024);
            Echo = true;
            ConnectTimeout = TimeSpan.FromSeconds(5) ;
        }

        public string[] Servers { get; set; }
        public Func<string, Task<IPAddress[]>> DnsResolver { get; set; }
        public NatsServerPoolFlags ServersOptions { get; set; }
        public ArrayPool<byte> ArrayPool { get; set; }
        public INatsSerializer Serializer { get; set; }
        public bool Verbose { get; set; }
        public string? AuthorizationToken { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool Echo { get; set; }
        public TimeSpan ConnectTimeout { get; set; } 
        public ILoggerFactory? LoggerFactory { get; set; }
        public Func<INatsOptions, INatsServerPool> ServerPoolFactory { get; set; }
    }
}