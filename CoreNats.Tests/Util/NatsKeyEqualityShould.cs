namespace CoreNats.Tests.Util
{
    using System.Collections.Generic;
    using Xunit;

    public class NatsKeyEqualityShould
    {
        // Build two NatsKey instances from DIFFERENT backing arrays that hold the same bytes.
        private static (NatsKey k1, NatsKey k2) TwoEqualKeys()
        {
            var bytes1 = new byte[] { 102, 111, 111 }; // "foo"
            var bytes2 = new byte[] { 102, 111, 111 }; // "foo" — different array, same content
            return (new NatsKey(bytes1), new NatsKey(bytes2));
        }

        [Fact]
        public void GetHashCode_Equal_ForSameContent()
        {
            var (k1, k2) = TwoEqualKeys();
            Assert.Equal(k1.GetHashCode(), k2.GetHashCode());
        }

        [Fact]
        public void TypedEquals_True_ForSameContent()
        {
            var (k1, k2) = TwoEqualKeys();
            Assert.True(k1.Equals(k2));
        }

        [Fact]
        public void ObjectEquals_True_ForSameContent()
        {
            var (k1, k2) = TwoEqualKeys();
            // Regression test: ValueType.Equals(object) used reference equality on the
            // backing Memory field and returned false even when bytes were equal.
            Assert.True(((object)k1).Equals((object)k2));
        }

        [Fact]
        public void ObjectEquals_False_ForDifferentContent()
        {
            var k1 = new NatsKey(new byte[] { 1, 2, 3 });
            var k2 = new NatsKey(new byte[] { 4, 5, 6 });
            Assert.False(((object)k1).Equals((object)k2));
        }

        [Fact]
        public void HashSet_TreatsEqualContentAsSameKey()
        {
            var (k1, k2) = TwoEqualKeys();
            var set = new HashSet<NatsKey> { k1 };
            // Adding a key with the same content must return false (already present).
            Assert.False(set.Add(k2));
            Assert.Single(set);
        }

        [Fact]
        public void Dictionary_TreatsEqualContentAsSameKey()
        {
            var (k1, k2) = TwoEqualKeys();
            var dict = new Dictionary<NatsKey, int> { [k1] = 42 };
            Assert.Equal(42, dict[k2]);
        }

        [Fact]
        public void EqualityOperator_True_ForSameContent()
        {
            var (k1, k2) = TwoEqualKeys();
            Assert.True(k1 == k2);
            Assert.False(k1 != k2);
        }
    }

    public class NatsPayloadEqualityShould
    {
        // Build two NatsPayload instances from DIFFERENT backing arrays that hold the same bytes.
        private static (NatsPayload p1, NatsPayload p2) TwoEqualPayloads()
        {
            var bytes1 = new byte[] { 98, 97, 114 }; // "bar"
            var bytes2 = new byte[] { 98, 97, 114 }; // "bar" — different array, same content
            return (new NatsPayload(bytes1), new NatsPayload(bytes2));
        }

        [Fact]
        public void GetHashCode_Equal_ForSameContent()
        {
            var (p1, p2) = TwoEqualPayloads();
            Assert.Equal(p1.GetHashCode(), p2.GetHashCode());
        }

        [Fact]
        public void TypedEquals_True_ForSameContent()
        {
            var (p1, p2) = TwoEqualPayloads();
            Assert.True(p1.Equals(p2));
        }

        [Fact]
        public void ObjectEquals_True_ForSameContent()
        {
            var (p1, p2) = TwoEqualPayloads();
            Assert.True(((object)p1).Equals((object)p2));
        }

        [Fact]
        public void ObjectEquals_False_ForDifferentContent()
        {
            var p1 = new NatsPayload(new byte[] { 1, 2, 3 });
            var p2 = new NatsPayload(new byte[] { 4, 5, 6 });
            Assert.False(((object)p1).Equals((object)p2));
        }

        [Fact]
        public void HashSet_TreatsEqualContentAsSameKey()
        {
            var (p1, p2) = TwoEqualPayloads();
            var set = new HashSet<NatsPayload> { p1 };
            Assert.False(set.Add(p2));
            Assert.Single(set);
        }

        [Fact]
        public void Dictionary_TreatsEqualContentAsSameKey()
        {
            var (p1, p2) = TwoEqualPayloads();
            var dict = new Dictionary<NatsPayload, int> { [p1] = 99 };
            Assert.Equal(99, dict[p2]);
        }

        [Fact]
        public void EqualityOperator_True_ForSameContent()
        {
            var (p1, p2) = TwoEqualPayloads();
            Assert.True(p1 == p2);
            Assert.False(p1 != p2);
        }
    }
}
