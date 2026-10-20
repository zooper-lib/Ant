using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Sources;
using Zooper.Ant.Tokens;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies the kind switch of <see cref="Pager.ReadAsync{TItem}"/> against a mocked <see cref="IPageSource{TItem}"/>:
/// which source calls each page kind makes, how the next token is issued, and every refusal path. This is the one
/// place every store's paging behaviour comes from, so a wrong call pattern here would be wrong for every store
/// (specs <c>paged-reads</c>, <c>continuation-tokens</c>, <c>page-sources</c>).
/// </summary>
public class PagerTests
{
	/// <summary>The scope every read in this class uses, unless a test needs a foreign one.</summary>
	private static readonly PageScope Scope = PageScope.For("ListAsync", "filter");

	/// <summary>The mocked store. Its items are plain integers, so a position is simply the item itself.</summary>
	private readonly IPageSource<int> _source = Substitute.For<IPageSource<int>>();

	/// <summary>The pager under test, over the real Data Protection protector so tokens are genuinely encrypted.</summary>
	private readonly Pager _sut = new(
		new DataProtectionPageTokenProtector(new EphemeralDataProtectionProvider(), Options.Create(new AntOptions())),
		Options.Create(new AntOptions()));

	/// <summary>
	/// Makes the mock behave like a keyset source ordered by the integer itself: the position of an item is the item,
	/// and parsing a position reads the integer back.
	/// </summary>
	public PagerTests()
	{
		_source.OrderSignature.Returns("Id asc");
		_source.PositionOf(Arg.Any<int>()).Returns(call => new PagePosition([call.Arg<int>()]));
		_source.ParsePosition(Arg.Any<IReadOnlyList<JsonElement>>())
			.Returns(call => new PagePosition([call.Arg<IReadOnlyList<JsonElement>>()[0].GetInt32()]));
	}

	/// <summary>
	/// A counted page asks the source for the total and for exactly one offset range, and never seeks. The counted kind
	/// is the only one that pays for a count, so it must be the only one that asks for it.
	/// </summary>
	[Fact]
	public async Task ReadAsync_Counted_CountsAndReadsOneOffsetRange()
	{
		// Arrange
		_source.CountAsync(Arg.Any<CancellationToken>()).Returns(62);
		// Page 3 at size 10 starts after 20 items.
		_source.ReadAsync(20, 10, Arg.Any<CancellationToken>()).Returns(Items(21, 10));

		// Act
		var page = (await _sut.ReadAsync(_source, Paging.Counted(3, 10), Scope)).ShouldBeOfType<CountedPage<int>>();

		// Assert
		page.Page.ShouldBe(3);
		page.Size.ShouldBe(10);
		page.TotalCount.ShouldBe(62);
		page.ItemList.ShouldBe(Items(21, 10));
		await _source.Received(1).CountAsync(Arg.Any<CancellationToken>());
		await _source.Received(1).ReadAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
		await _source.DidNotReceiveWithAnyArgs().ReadAfterAsync(default!, default, default);
	}

	/// <summary>
	/// A capped page reads one item more than the cap and reports truncation from that extra item, without counting.
	/// That keeps a "top N" screen at the cost of N + 1 rows.
	/// </summary>
	[Fact]
	public async Task ReadAsync_Capped_ReadsOneBeyondTheCapAndReportsTruncation()
	{
		// Arrange
		_source.ReadAsync(0, 6, Arg.Any<CancellationToken>()).Returns(Items(1, 6));

		// Act
		var page = (await _sut.ReadAsync(_source, Paging.Capped(5), Scope)).ShouldBeOfType<CappedPage<int>>();

		// Assert
		page.Cap.ShouldBe(5);
		page.Truncated.ShouldBeTrue();
		// The probe item is used for the flag only and never served.
		page.ItemList.ShouldBe(Items(1, 5));
		await _source.DidNotReceiveWithAnyArgs().CountAsync(default);
	}

	/// <summary>When the source returns no more than the cap, the capped page says nothing more matched.</summary>
	[Fact]
	public async Task ReadAsync_Capped_WhenNothingIsBeyond_IsNotTruncated()
	{
		// Arrange
		_source.ReadAsync(0, 6, Arg.Any<CancellationToken>()).Returns(Items(1, 5));

		// Act
		var page = (await _sut.ReadAsync(_source, Paging.Capped(5), Scope)).ShouldBeOfType<CappedPage<int>>();

		// Assert
		page.Truncated.ShouldBeFalse();
		page.ItemList.Count.ShouldBe(5);
	}

	/// <summary>
	/// The next token is issued from the last item served (the size-th), not from the extra probe item, and the
	/// following page seeks strictly after it. Issuing it from the probe would skip one item per page.
	/// </summary>
	[Fact]
	public async Task ReadAsync_Continuable_IssuesTheTokenFromTheSizeThItem_AndContinuesAfterIt()
	{
		// Arrange
		_source.ReadAsync(0, 4, Arg.Any<CancellationToken>()).Returns(Items(1, 4));
		// The second read only answers when it seeks after item 3, the last item of the first page.
		_source.ReadAfterAsync(Arg.Is<PagePosition>(p => (int)p.Values[0]! == 3), 4, Arg.Any<CancellationToken>()).Returns(Items(4, 2));

		// Act
		var first = (await _sut.ReadAsync(_source, Paging.Continuable(null, 3), Scope)).ShouldBeOfType<ContinuablePage<int>>();
		var second = (await _sut.ReadAsync(_source, Paging.Continuable(first.NextPageToken, 3), Scope)).ShouldBeOfType<ContinuablePage<int>>();

		// Assert
		first.ItemList.ShouldBe([1, 2, 3]);
		first.NextPageToken.ShouldNotBeNull();
		_source.Received(1).PositionOf(3);
		second.ItemList.ShouldBe([4, 5]);
		second.NextPageToken.ShouldBeNull();
	}

	/// <summary>
	/// When the source returns no more than the page size, the page carries no token. The missing token is the only
	/// end signal a caller gets, so it must never be issued for a last page.
	/// </summary>
	[Fact]
	public async Task ReadAsync_Continuable_OnTheLastPage_IssuesNoToken()
	{
		// Arrange
		_source.ReadAsync(0, 4, Arg.Any<CancellationToken>()).Returns(Items(1, 3));

		// Act
		var page = (await _sut.ReadAsync(_source, Paging.Continuable(null, 3), Scope)).ShouldBeOfType<ContinuablePage<int>>();

		// Assert
		page.ItemList.ShouldBe([1, 2, 3]);
		page.NextPageToken.ShouldBeNull();
		_source.DidNotReceiveWithAnyArgs().PositionOf(default);
	}

	/// <summary>An empty result set yields an empty page of every kind, with no token and a total of zero.</summary>
	[Fact]
	public async Task ReadAsync_OnAnEmptySet_ServesEmptyPagesOfEveryKind()
	{
		// Arrange
		_source.ReadAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Items(1, 0));

		// Act & Assert
		(await _sut.ReadAsync(_source, Paging.Continuable(null, 3), Scope)).ShouldBeOfType<ContinuablePage<int>>().NextPageToken.ShouldBeNull();
		(await _sut.ReadAsync(_source, Paging.Counted(1, 3), Scope)).ShouldBeOfType<CountedPage<int>>().TotalCount.ShouldBe(0);
		(await _sut.ReadAsync(_source, Paging.Capped(3), Scope)).ShouldBeOfType<CappedPage<int>>().ItemList.ShouldBeEmpty();
	}

	/// <summary>
	/// A source that cannot count (a native-cursor store) throws <see cref="NotSupportedException"/>; the pager turns
	/// that into a paging error naming the counted kind, so the host answers it like any other paging refusal and it
	/// is not mistaken for an invalid token.
	/// </summary>
	[Fact]
	public async Task ReadAsync_Counted_WhenTheSourceCannotCount_RefusesNamingTheKind()
	{
		// Arrange
		_source.CountAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new NotSupportedException());

		// Act
		var exception = await Should.ThrowAsync<PagingException>(() => _sut.ReadAsync(_source, Paging.Counted(1, 10), Scope));

		// Assert
		exception.ShouldNotBeOfType<InvalidPageTokenException>();
		exception.Message.ShouldContain("counted kind is not available");
	}

	/// <summary>
	/// A source that cannot skip throws <see cref="NotSupportedException"/> for an offset above zero; the counted kind
	/// is refused the same way as when it cannot count.
	/// </summary>
	[Fact]
	public async Task ReadAsync_Counted_WhenTheSourceCannotOffset_RefusesNamingTheKind()
	{
		// Arrange
		_source.ReadAsync(10, 10, Arg.Any<CancellationToken>()).ThrowsAsync(new NotSupportedException());

		// Act
		var exception = await Should.ThrowAsync<PagingException>(() => _sut.ReadAsync(_source, Paging.Counted(2, 10), Scope));

		// Assert
		exception.Message.ShouldContain("counted kind is not available");
	}

	/// <summary>
	/// A token issued under one scope is refused under another, before the store is asked anything. Serving it would
	/// continue a different list from a position that means nothing there.
	/// </summary>
	[Fact]
	public async Task ReadAsync_WhenTheTokenIsForeign_RefusesAndReadsNothing()
	{
		// Arrange
		_source.ReadAsync(0, 4, Arg.Any<CancellationToken>()).Returns(Items(1, 4));
		var token = ((ContinuablePage<int>)await _sut.ReadAsync(_source, Paging.Continuable(null, 3), Scope)).NextPageToken;

		// Act & Assert
		await Should.ThrowAsync<InvalidPageTokenException>(
			() => _sut.ReadAsync(_source, Paging.Continuable(token, 3), PageScope.For("ListAsync", "other filter")));
		await _source.DidNotReceiveWithAnyArgs().ReadAfterAsync(default!, default, default);
	}

	/// <summary>
	/// The source's order signature is folded into the token binding, so a token issued under one ordering is refused
	/// once the read orders differently, for every source and not only LINQ ones.
	/// </summary>
	[Fact]
	public async Task ReadAsync_WhenTheOrderingChanged_RefusesTheToken()
	{
		// Arrange
		_source.ReadAsync(0, 4, Arg.Any<CancellationToken>()).Returns(Items(1, 4));
		var token = ((ContinuablePage<int>)await _sut.ReadAsync(_source, Paging.Continuable(null, 3), Scope)).NextPageToken;
		_source.OrderSignature.Returns("Id desc");

		// Act & Assert
		await Should.ThrowAsync<InvalidPageTokenException>(() => _sut.ReadAsync(_source, Paging.Continuable(token, 3), Scope));
	}

	/// <summary>
	/// A token that passes the binding but whose position the source cannot read back (any exception from
	/// <c>ParsePosition</c>) is refused as an invalid token, not raised as an internal error, and nothing is read.
	/// </summary>
	[Fact]
	public async Task ReadAsync_WhenTheSourceCannotParseThePosition_RefusesTheToken()
	{
		// Arrange
		_source.ReadAsync(0, 4, Arg.Any<CancellationToken>()).Returns(Items(1, 4));
		var token = ((ContinuablePage<int>)await _sut.ReadAsync(_source, Paging.Continuable(null, 3), Scope)).NextPageToken;
		_source.ParsePosition(Arg.Any<IReadOnlyList<JsonElement>>()).Throws(new JsonException("wrong type"));

		// Act & Assert
		await Should.ThrowAsync<InvalidPageTokenException>(() => _sut.ReadAsync(_source, Paging.Continuable(token, 3), Scope));
		await _source.DidNotReceiveWithAnyArgs().ReadAfterAsync(default!, default, default);
	}

	/// <summary>The pager exposes the configured limits, so hosts build every paging instruction from the same bounds.</summary>
	[Fact]
	public void Limits_AreTheConfiguredOnes()
	{
		// Arrange
		var limits = new PagingLimits(25, 100, 5000);

		// Act
		var sut = new Pager(Substitute.For<IPageTokenProtector>(), Options.Create(new AntOptions { Limits = limits }));

		// Assert
		sut.Limits.ShouldBe(limits);
	}

	/// <summary>Builds the consecutive integers <paramref name="first"/> .. <paramref name="first"/> + <paramref name="count"/> - 1.</summary>
	private static IReadOnlyList<int> Items(int first, int count) => Enumerable.Range(first, count).ToList();
}
