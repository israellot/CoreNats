using Microsoft.Extensions.Logging;

namespace CoreNats
{
    using System;
    using System.Buffers;
    using System.Net;
    using System.Threading.Tasks;

    public interface INatsOptions
    {        
        string[] Servers { get; set; }

        /// <summary>
        /// If set, will be used instead of system dns
        /// </summary>
        Func<string, Task<IPAddress[]>> DnsResolver { get; set; }

        Func<INatsOptions,INatsServerPool> ServerPoolFactory { get; set; }

        NatsServerPoolFlags ServersOptions { get; set; }

        ArrayPool<byte> ArrayPool { get; }

        bool Verbose { get; }
        string? AuthorizationToken { get; }
        string? Username { get; }
        string? Password { get; }
        bool Echo { get; }
        TimeSpan ConnectTimeout { get; }

        ILoggerFactory? LoggerFactory { get; }
    }
}