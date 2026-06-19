namespace CoreNats
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    internal class NatsServerPool:INatsServerPool
    {
        public IList<DnsEndPoint> Servers
        {
            get
            {
                if (_options.ServersOptions.HasFlag(NatsServerPoolFlags.AllowDiscovery))
                    return new List<DnsEndPoint>(_servers.Concat(_discoveredServers));
                else
                    return new List<DnsEndPoint>(_servers);
            }
        }

        List<DnsEndPoint> _servers = new List<DnsEndPoint>();
        List<DnsEndPoint> _discoveredServers = new List<DnsEndPoint>();
        Random _random = new Random();
        DnsEndPoint? _lastSelectedDnsEndPoint = null;
        Queue<IPEndPoint> _retryIPEndPointQueue = new Queue<IPEndPoint>();
        INatsOptions _options;
        ILogger<NatsServerPool>? _logger;

        object _sync = new object();

        public NatsServerPool(INatsOptions options)
        {

            _logger=options.LoggerFactory?.CreateLogger<NatsServerPool>();

            var servers = options.Servers;

            if(servers == null) throw new ArgumentNullException(nameof(servers));
            if(servers.Count()==0) throw new ArgumentException("server list cannot be empty",nameof(servers));

            _options = options;

            foreach (var server in servers)
                AddServer(_servers,server);
        }


        public async ValueTask<IPEndPoint> SelectServer(bool isRetry=false)
        {
            DnsEndPoint SelectDnsEndpoint(bool isRetry)
            {
                DnsEndPoint selectedServer;
                lock (_sync)
                {
                    var combinedServers = _servers;
                    if (_options.ServersOptions.HasFlag(NatsServerPoolFlags.AllowDiscovery))
                        combinedServers = _servers.Concat(_discoveredServers).ToList();

                    selectedServer = combinedServers[0];

                    if (combinedServers.Count == 1)
                    {
                        _lastSelectedDnsEndPoint = selectedServer;
                        return selectedServer!;
                    }


                    if (!isRetry)
                        _lastSelectedDnsEndPoint = null;

                    if (_options.ServersOptions.HasFlag(NatsServerPoolFlags.Randomize))
                    {
                        //if possible, randomize but also avoid returning the same selection as previous on retry
                        selectedServer = combinedServers
                            .Where(s => s != _lastSelectedDnsEndPoint)
                            .OrderBy(s => _random.Next())
                            .First();
                    }
                    else if (_lastSelectedDnsEndPoint != null)
                    {
                        //return server list in order
                        if (combinedServers.IndexOf(_lastSelectedDnsEndPoint) + 1 != combinedServers.Count)
                            selectedServer = combinedServers[combinedServers.IndexOf(_lastSelectedDnsEndPoint) + 1];
                    }

                    _lastSelectedDnsEndPoint = selectedServer;
                    return selectedServer!;
                }
            }

            if (isRetry && _retryIPEndPointQueue.Count > 0)
                return _retryIPEndPointQueue.Dequeue();
            else
                _retryIPEndPointQueue.Clear();

            var dnsEndpoint = SelectDnsEndpoint(isRetry);

            if(IPAddress.TryParse(dnsEndpoint.Host, out var ipAddress))
            {
                return new IPEndPoint(ipAddress, dnsEndpoint.Port);
            }

            _logger?.LogTrace("Resolving dns hostname {Host}", dnsEndpoint.Host);
            var resolved = await _options.DnsResolver(dnsEndpoint.Host);

            if(resolved == null)
            {
                //dns failed
                throw new InvalidOperationException("dns resolve returned 0 entries");
            }

            _logger?.LogTrace("Dns hostname {Host} resolve to {Ips}", dnsEndpoint.Host, string.Join(",", resolved.Select(ip => ip.ToString())));

            if (resolved.Length > 1)
            {                
                for (var i = 1; i < resolved.Length; i++)
                    _retryIPEndPointQueue.Enqueue(new IPEndPoint(resolved[i], dnsEndpoint.Port));
            }

            return new IPEndPoint(resolved[0], dnsEndpoint.Port);
        }

        public void SetDiscoveredServers(IEnumerable<string> servers)
        {
            if (servers == null || servers.Count()==0) return;

            if (!_options.ServersOptions.HasFlag(NatsServerPoolFlags.AllowDiscovery))
                return;

            lock (_sync)
            {
                _discoveredServers = new List<DnsEndPoint>();

                foreach (var server in servers.OrderBy(s=>Guid.NewGuid())) //randomize discovered
                    AddServer(_discoveredServers, server);
            }
            
        }

        private DnsEndPoint ParseAndNormalizeServerEntry(string server)
        {
            if (string.IsNullOrWhiteSpace(server))
                throw new FormatException($"invalid server string {server}");

            var originalServer = server;
            if (originalServer.EndsWith(":", StringComparison.Ordinal))
                throw new FormatException($"invalid server string {originalServer}");

            if (!server.Contains("://"))
            {
                server = $"nats://{server}";
            }

            if (!Uri.TryCreate(server, UriKind.Absolute, out var uri))
            {
                if (!Uri.TryCreate($"{server}:4222", UriKind.Absolute, out uri))
                    throw new FormatException($"invalid server string {originalServer}");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host))
                throw new FormatException($"invalid server string {originalServer}");

            var host = uri.Host.Trim('[', ']');
            var port = uri.IsDefaultPort ? 4222 : uri.Port;
            if (port <= 0)
                throw new FormatException($"invalid server string {originalServer}");

            var checkHostNameResult = Uri.CheckHostName(host);
            if(checkHostNameResult==UriHostNameType.Unknown)
                throw new FormatException($"invalid server string {originalServer}");

            return new DnsEndPoint(host, port);
            
        }

        private void AddServer(List<DnsEndPoint> list, string server)
        {
            lock (_sync)
            {
                var parsed = ParseAndNormalizeServerEntry(server);

                if (!list.Contains(parsed))
                {
                    list.Add(parsed);
                }
            }
        }               
        

    }
}