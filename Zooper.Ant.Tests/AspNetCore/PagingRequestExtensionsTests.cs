using Shouldly;
using Xunit;
using Zooper.Ant.AspNetCore;

namespace Zooper.Ant.Tests.AspNetCore;

/// <summary>
/// Verifies <see cref="PagingRequestExtensions.ToPaging"/> without a host: each kind builds its instruction, an
/// argument of another kind is refused, and the host's limits (not the built-in defaults) bound the result (spec
/// <c>http-paging-binding</c>, "Each kind accepts only its own arguments" and "Bounds come from the host's configured
/// limits"). Ported from Feng's <c>PagingRequestExtensionsTests</c>.
/// </summary>
public class PagingRequestExtensionsTests
{
	/// <summary>A structurally valid token; it is never opened here.</summary>
	private static readonly PageToken Token = PageToken.Parse("token");

	/// <summary>Limits that differ from <see cref="PagingLimits.Default"/> in every bound.</summary>
	private static readonly PagingLimits Custom = new(DefaultSize: 25, MaximumSize: 100, MaximumDepth: 5000);

	/// <summary>No kind named means continuable, carrying the size and token.</summary>
	[Fact]
	public void ToPaging_WhenNoKindIsNamed_BuildsContinuable()
	{
		var paging = new PagingRequest { Size = 10, PageToken = Token }.ToPaging(PagingLimits.Default);

		var continuable = paging.ShouldBeOfType<ContinuablePaging>();
		continuable.Size.ShouldBe(10);
		continuable.PageToken.ShouldBe(Token);
	}

	/// <summary>Naming continuable builds continuable.</summary>
	[Fact]
	public void ToPaging_WhenContinuableIsNamed_BuildsContinuable() =>
		new PagingRequest { Paging = PagingKind.Continuable }.ToPaging(PagingLimits.Default).ShouldBeOfType<ContinuablePaging>();

	/// <summary>Naming counted builds counted with the page and size.</summary>
	[Fact]
	public void ToPaging_WhenCountedIsNamed_BuildsCounted()
	{
		var paging = new PagingRequest { Paging = PagingKind.Counted, Page = 3, Size = 10 }.ToPaging(PagingLimits.Default);

		var counted = paging.ShouldBeOfType<CountedPaging>();
		counted.Page.ShouldBe(3);
		counted.Size.ShouldBe(10);
	}

	/// <summary>Naming capped builds capped with the cap.</summary>
	[Fact]
	public void ToPaging_WhenCappedIsNamed_BuildsCapped() =>
		new PagingRequest { Paging = PagingKind.Capped, Cap = 10 }.ToPaging(PagingLimits.Default)
			.ShouldBeOfType<CappedPaging>().Cap.ShouldBe(10);

	/// <summary>A default kind was never bound from a query; it is refused rather than read as continuable.</summary>
	[Fact]
	public void ToPaging_WithADefaultKind_Throws() =>
		Should.Throw<PagingException>(() => new PagingRequest { Paging = default(PagingKind) }.ToPaging(PagingLimits.Default));

	/// <summary>Every argument that belongs to another kind, with the name the refusal must carry.</summary>
	public static TheoryData<string?, int?, int?, int?, bool, string> IncompatibleCaseList => new()
	{
		{ null, 1, null, null, false, "'page' does not belong to continuable" },
		{ null, null, null, 10, false, "'cap' does not belong to continuable" },
		{ "continuable", 2, null, null, false, "'page' does not belong to continuable" },
		{ "counted", null, null, null, true, "'pageToken' does not belong to counted" },
		{ "counted", null, null, 10, false, "'cap' does not belong to counted" },
		{ "capped", 4, null, null, false, "'page' does not belong to capped" },
		{ "capped", null, 20, null, false, "'size' does not belong to capped" },
		{ "capped", null, null, null, true, "'pageToken' does not belong to capped" }
	};

	/// <summary>An argument of another kind is refused with a message naming it and the kind, never ignored.</summary>
	[Theory]
	[MemberData(nameof(IncompatibleCaseList))]
	public void ToPaging_WhenArgumentBelongsToAnotherKind_Throws(string? kind, int? page, int? size, int? cap, bool pageToken, string expected)
	{
		var request = new PagingRequest { Paging = kind is null ? null : PagingKind.Parse(kind, null), Page = page, Size = size, Cap = cap, PageToken = pageToken ? Token : null };

		Should.Throw<PagingException>(() => request.ToPaging(PagingLimits.Default)).Message.ShouldContain(expected);
	}

	/// <summary>A size above a custom maximum is clamped to that maximum, not the built-in 50.</summary>
	[Fact]
	public void ToPaging_WithCustomLimits_ClampsToTheCustomMaximum() =>
		new PagingRequest { Size = 500 }.ToPaging(Custom).ShouldBeOfType<ContinuablePaging>().Size.ShouldBe(100);

	/// <summary>An omitted size takes a custom default, not the built-in 20.</summary>
	[Fact]
	public void ToPaging_WithCustomLimits_DefaultsToTheCustomDefault() =>
		new PagingRequest { Paging = PagingKind.Counted }.ToPaging(Custom).ShouldBeOfType<CountedPaging>().Size.ShouldBe(25);
}
