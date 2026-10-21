using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Sources;
using Zooper.Ant.Tests.Contract;

namespace Zooper.Ant.Tests;

/// <summary>
/// Pages a 95-item list end to end through <see cref="QueryableExtensions.ToPageAsync{T}(IEnumerable{T}, Paging, Order{T}, PageScope, Pager, System.Threading.CancellationToken)"/>
/// and <see cref="Pager"/>, the path a consumer of the in-memory source takes (specs <c>paged-reads</c> and
/// <c>page-sources</c>). Unlike the unit tests, nothing is mocked: ordering, seek, tokens and the kind switch run together.
/// </summary>
public class InMemoryPagingTests
{
	private sealed record Item(int Id, int Bucket);

	// 95 items, not a multiple of the page size, so the last page is partial. Bucket repeats every 7 items, so most
	// page boundaries fall inside a tie that only the Id resolves. Reversed so the input is not already in order.
	private static readonly IReadOnlyList<Item> Items = Enumerable.Range(1, 95).Select(i => new Item(i, i % 7)).Reverse().ToList();

	private static readonly Order<Item> ByBucketThenId = Order<Item>.By(i => i.Bucket).ThenBy(i => i.Id);

	// The expected sequence, computed with LINQ-to-objects independently of the builder under test.
	private static readonly IReadOnlyList<int> Expected = Items.OrderBy(i => i.Bucket).ThenBy(i => i.Id).Select(i => i.Id).ToList();

	private static readonly PageScope Scope = PageScope.For("ListItems");

	private readonly Pager _sut = new(new PlainTextPageTokenProtector(), Options.Create(new AntOptions()));

	/// <summary>A counted page answers with the requested slice of the ordered list and the total of all matches.</summary>
	[Fact]
	public async Task Counted_ServesTheRequestedPageWithTheTotal()
	{
		// Act: page 5 of size 20 is items 81 to 95, the partial last page.
		var page = (await Items.ToPageAsync(Paging.Counted(5, 20), ByBucketThenId, Scope, _sut)).ShouldBeOfType<CountedPage<Item>>();

		// Assert
		page.TotalCount.ShouldBe(95);
		page.ItemList.Select(i => i.Id).ShouldBe(Expected.Skip(80));
	}

	/// <summary>A capped page returns the first items up to the cap and reports that more matched.</summary>
	[Fact]
	public async Task Capped_ServesTheCapAndReportsTruncation()
	{
		// Act
		var page = (await Items.ToPageAsync(Paging.Capped(50), ByBucketThenId, Scope, _sut)).ShouldBeOfType<CappedPage<Item>>();

		// Assert
		page.Truncated.ShouldBeTrue();
		page.ItemList.Select(i => i.Id).ShouldBe(Expected.Take(50));
	}

	/// <summary>
	/// Following next tokens from the first page to the last returns every item exactly once and in order, and the read
	/// ends exactly when the items run out.
	/// </summary>
	[Fact]
	public async Task Continuable_FullWalk_SeesEveryItemExactlyOnceInOrder()
	{
		// Arrange
		var seen = new List<int>();
		PageToken? token = null;
		var pages = 0;

		// Act
		do
		{
			var page = (await Items.ToPageAsync(Paging.Continuable(token, 20), ByBucketThenId, Scope, _sut)).ShouldBeOfType<ContinuablePage<Item>>();
			seen.AddRange(page.ItemList.Select(i => i.Id));
			token = page.NextPageToken;
			pages++;
		}
		while (token is not null);

		// Assert: 95 items at size 20 are five pages, the last one partial.
		seen.ShouldBe(Expected);
		pages.ShouldBe(5);
	}

	/// <summary><see cref="Pager.TraverseAsync{TItem}"/> over the queryable source yields the whole list exactly once.</summary>
	[Fact]
	public async Task Traverse_OverTheQueryableSource_SeesEveryItemExactlyOnce()
	{
		// Act: size 7 equals the bucket length, so every boundary sits next to a bucket change or inside one.
		var seen = await _sut.TraverseAsync<Item>((token, ct) =>
			Items.AsQueryable().ToPageAsync(Paging.Continuable(token, 7), ByBucketThenId, Scope, _sut, ct)).ToListAsync();

		// Assert
		seen.Select(i => i.Id).ShouldBe(Expected);
	}

	/// <summary>
	/// The first counted page, the first continuable page and a capped page of the same size hold the same items, because
	/// every kind reads the same order (spec <c>paged-reads</c>, "Same order across kinds").
	/// </summary>
	[Fact]
	public async Task EveryKind_ServesTheSameFirstItems()
	{
		// Act
		var counted = await Items.ToPageAsync(Paging.Counted(1, 10), ByBucketThenId, Scope, _sut);
		var continuable = await Items.ToPageAsync(Paging.Continuable(null, 10), ByBucketThenId, Scope, _sut);
		var capped = await Items.ToPageAsync(Paging.Capped(10), ByBucketThenId, Scope, _sut);

		// Assert
		continuable.ItemList.ShouldBe(counted.ItemList);
		capped.ItemList.ShouldBe(counted.ItemList);
	}
}
