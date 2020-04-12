using FluentAssertions;
using NUnit.Framework;

namespace Template.Tests
{
    [TestFixture]
    class PowerOf2Tests
    {
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        [TestCase(1024, true)]
        public void Should_power_of_2(int x, bool expected)
        {
            var result = MathUtils.PowerOf2(x);
            result.Should().Be(expected);
        }
    }
}
