using Shouldly;
using Xunit;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies the <see cref="Paging"/> factories (spec <c>paged-reads</c>): they default and clamp sizes, refuse a counted
/// page below one or beyond the depth bound, and never bound continuable paging by depth. The factories are the one
/// place bounds are applied, so every caller is bounded alike before any store is asked.
/// </summary>
public class PagingTests
{
	/// <summary>
	/// A missing, zero or negative size becomes the default of 20, so a caller that omits the size still gets a sensible
	/// page.
	/// </summary>
	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(-3)]
	public void Continuable_WhenSizeIsMissingOrNonPositive_DefaultsToTwenty(int? size)
	{
		// Act
		var sut = Paging.Continuable(null, size);

		// Assert
		sut.Size.ShouldBe(20);
	}

	/// <summary>A size above the maximum is clamped to 50 and served, because a smaller page is still a correct answer.</summary>
	[Fact]
	public void Continuable_WhenSizeExceedsMaximum_ClampsToFifty()
	{
		// Act
		var sut = Paging.Continuable(null, 500);

		// Assert
		sut.Size.ShouldBe(50);
	}

	/// <summary>A token is carried through untouched and no depth check applies, because continuation uses no offset.</summary>
	[Fact]
	public void Continuable_WhenTokenIsGiven_KeepsItWithoutDepthCheck()
	{
		// Arrange
		var token = PageToken.Parse("any-token_value");

		// Act
		var sut = Paging.Continuable(token, 50);

		// Assert
		sut.PageToken.ShouldBe(token);
		sut.Size.ShouldBe(50);
	}

	/// <summary>A counted request without a page number asks for the first page at the default size.</summary>
	[Fact]
	public void Counted_WhenPageIsMissing_DefaultsToFirstPage()
	{
		// Act
		var sut = Paging.Counted(null, null);

		// Assert
		sut.Page.ShouldBe(1);
		sut.Size.ShouldBe(20);
	}

	/// <summary>
	/// Page zero and negative pages are refused rather than served as page 1, which would be a wrong answer presented as
	/// a right one.
	/// </summary>
	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void Counted_WhenPageIsBelowOne_Throws(int page)
	{
		// Act & Assert
		Should.Throw<PagingException>(() => Paging.Counted(page, 20));
	}

	/// <summary>Page 50 at size 20 reaches row 1000, exactly the default depth bound, and is served.</summary>
	[Fact]
	public void Counted_WhenDepthIsAtBound_Serves()
	{
		// Act
		var sut = Paging.Counted(50, 20);

		// Assert
		sut.Page.ShouldBe(50);
	}

	/// <summary>
	/// Page 51 at size 20 reaches row 1020, beyond the bound, and is refused with a message naming the bound so the
	/// caller knows to switch to continuable paging.
	/// </summary>
	[Fact]
	public void Counted_WhenDepthExceedsBound_ThrowsNamingTheBound()
	{
		// Act
		var exception = Should.Throw<PagingException>(() => Paging.Counted(51, 20));

		// Assert
		exception.Message.ShouldContain("1000");
	}

	/// <summary>Depth is page times size: page 1000 at size 1 reaches exactly row 1000 and is served.</summary>
	[Fact]
	public void Counted_WhenPageThousandAtSizeOne_ReachesExactlyTheBoundAndServes()
	{
		// Act
		var sut = Paging.Counted(1000, 1);

		// Assert
		sut.Page.ShouldBe(1000);
		sut.Size.ShouldBe(1);
	}

	/// <summary>The same page number at the default size reaches row 20000 and is refused.</summary>
	[Fact]
	public void Counted_WhenPageThousandAtDefaultSize_Throws()
	{
		// Act & Assert
		Should.Throw<PagingException>(() => Paging.Counted(1000, null));
	}

	/// <summary>
	/// A cap is bounded exactly like a size: missing or zero becomes the default, a value in range is kept, and a value
	/// above the maximum is clamped.
	/// </summary>
	[Theory]
	[InlineData(null, 20)]
	[InlineData(0, 20)]
	[InlineData(10, 10)]
	[InlineData(500, 50)]
	public void Capped_BoundsCapLikeASize(int? cap, int expected)
	{
		// Act
		var sut = Paging.Capped(cap);

		// Assert
		sut.Cap.ShouldBe(expected);
	}

	/// <summary>
	/// A host's own limits replace the defaults in every factory: default, clamp and depth all follow them
	/// (spec <c>paged-reads</c>, "Valid custom bounds").
	/// </summary>
	[Fact]
	public void Factories_WithCustomLimits_ApplyThem()
	{
		// Arrange
		var limits = new PagingLimits(25, 100, 5000);

		// Act & Assert
		Paging.Continuable(null, null, limits).Size.ShouldBe(25);
		Paging.Continuable(null, 500, limits).Size.ShouldBe(100);
		Paging.Capped(500, limits).Cap.ShouldBe(100);
		Paging.Counted(50, 100, limits).Page.ShouldBe(50);
		Should.Throw<PagingException>(() => Paging.Counted(51, 100, limits)).Message.ShouldContain("5000");
	}

	/// <summary>The factories check the limits they are given, so inconsistent limits never produce a page.</summary>
	[Fact]
	public void Factories_WithInvalidLimits_Throw()
	{
		// Act & Assert
		Should.Throw<PagingException>(() => Paging.Continuable(null, 10, new PagingLimits(60, 50, 1000)));
	}

	/// <summary>
	/// An empty page has the kind the caller asked for and keeps its page-level values, so a read that knows it matches
	/// nothing can answer without asking the store.
	/// </summary>
	[Fact]
	public void ToEmptyPage_KeepsTheRequestedKind()
	{
		// Act & Assert
		Paging.Counted(2, 10).ToEmptyPage<int>().ShouldBeOfType<CountedPage<int>>().Page.ShouldBe(2);
		Paging.Capped(5).ToEmptyPage<int>().ShouldBeOfType<CappedPage<int>>().Truncated.ShouldBeFalse();
		Paging.Continuable(null, 7).ToEmptyPage<int>().ShouldBeOfType<ContinuablePage<int>>().NextPageToken.ShouldBeNull();
	}
}
