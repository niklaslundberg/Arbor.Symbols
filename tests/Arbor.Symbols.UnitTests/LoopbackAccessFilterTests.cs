using System.Net;
using Arbor.Symbols.Server;

namespace Arbor.Symbols.UnitTests;

public class LoopbackAccessFilterTests
{
    [Fact]
    public void IsAllowed_NullAddress_ReturnsTrue()
    {
        LoopbackAccessFilter.IsAllowed(null).Should().BeTrue();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.42")]
    [InlineData("::1")]
    public void IsAllowed_LoopbackAddress_ReturnsTrue(string address)
    {
        LoopbackAccessFilter.IsAllowed(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("8.8.8.8")]
    public void IsAllowed_NonLoopbackAddress_ReturnsFalse(string address)
    {
        LoopbackAccessFilter.IsAllowed(IPAddress.Parse(address)).Should().BeFalse();
    }
}
