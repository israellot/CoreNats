namespace CoreNats.Tests
{
    using System;
    using Xunit;

    public class NatsDefaultOptionsShould
    {
        [Fact]
        public void Constructor_SetsExpectedDefaults()
        {
            var options = new NatsDefaultOptions();

            Assert.Equal(new[] { "127.0.0.1:4222" }, options.Servers);
            Assert.True(options.Echo);
            Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
            Assert.False(options.Verbose);
            Assert.Null(options.AuthorizationToken);
            Assert.Null(options.Username);
            Assert.Null(options.Password);
        }

        [Fact]
        public void Constructor_ProvidesDefaultServices()
        {
            var options = new NatsDefaultOptions();

            Assert.NotNull(options.DnsResolver);
            Assert.NotNull(options.ServerPoolFactory);
            Assert.NotNull(options.Serializer);
            Assert.NotNull(options.ArrayPool);
            Assert.IsType<NatsDefaultSerializer>(options.Serializer);
        }

        [Fact]
        public void Constructor_ProvidesWorkingServerPoolFactory()
        {
            var options = new NatsDefaultOptions();

            var pool = options.ServerPoolFactory(options);

            Assert.IsType<NatsServerPool>(pool);
            Assert.Single(pool.Servers);
            Assert.Equal("127.0.0.1", pool.Servers[0].Host);
            Assert.Equal(4222, pool.Servers[0].Port);
        }
    }
}
