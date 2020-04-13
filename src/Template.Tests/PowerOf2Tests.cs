using System;
using FluentAssertions;
using Xunit;

namespace Template.Tests
{
    public class PowerOf2Tests
    {
        [Theory]
        [InlineData(3, false)]
        [InlineData(4, true)]
        [InlineData(1024, true)]
        public void Should_power_of_2(int x, bool expected)
        {
            var result = MathUtils.PowerOf2(x);
            result.Should().Be(expected);
        }
    }

    public class ExpTests
    {
        [Fact]
        public void Should_exp()
        {
            var result = MathUtils.Exp(5.2);
            result.Should().Be(Math.Exp(5.2));
        }
    }
}
