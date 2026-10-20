using Shouldly;
using Xunit;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies <see cref="PagingLimits"/> (spec <c>paged-reads</c>, "Bounds are configuration validated at startup"): the
/// defaults are 20, 50 and 1000, and an inconsistent configuration is refused with a message naming the bound, so a
/// misconfigured host fails at startup instead of serving odd pages.
/// </summary>
public class PagingLimitsTests
{
	/// <summary>The default limits are size 20, maximum 50, depth 1000, and they are consistent.</summary>
	[Fact]
	public void Default_IsTwentyFiftyThousand_AndValid()
	{
		// Act
		var sut = PagingLimits.Default;

		// Assert
		sut.ShouldBe(new PagingLimits(20, 50, 1000));
		sut.IsValid.ShouldBeTrue();
		Should.NotThrow(sut.Validate);
	}

	/// <summary>
	/// Each inconsistent combination is refused and the message names the offending bound. The cases: a default size of
	/// zero or below, a default above the maximum, and a depth below the maximum size.
	/// </summary>
	[Theory]
	[InlineData(0, 50, 1000, "DefaultSize")]
	[InlineData(-1, 50, 1000, "DefaultSize")]
	[InlineData(60, 50, 1000, "DefaultSize")]
	[InlineData(20, 50, 49, "MaximumDepth")]
	public void Validate_WhenBoundsAreInconsistent_ThrowsNamingTheBound(int defaultSize, int maximumSize, int maximumDepth, string bound)
	{
		// Arrange
		var sut = new PagingLimits(defaultSize, maximumSize, maximumDepth);

		// Act & Assert
		sut.IsValid.ShouldBeFalse();
		Should.Throw<PagingException>(sut.Validate).Message.ShouldContain(bound);
	}

	/// <summary>The edges are allowed: a default equal to the maximum, and a depth equal to the maximum size.</summary>
	[Fact]
	public void Validate_WhenDefaultEqualsMaximumAndDepthEqualsMaximum_Succeeds()
	{
		// Act & Assert
		Should.NotThrow(new PagingLimits(50, 50, 50).Validate);
	}
}
