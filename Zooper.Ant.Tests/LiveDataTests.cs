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
/// Continuation and traversal over data that changes between pages. A token is a bookmark in the order, not an offset,
/// and nothing is frozen between pages, so deletes behind the bookmark shift nothing, inserts ahead of it show up later,
/// and a changed ordering value may omit or repeat an item (specs <c>continuation-tokens</c>, <c>keyset-ordering</c>
/// and <c>paged-traversal</c>, "Traversal reads live data without a snapshot").
/// </summary>
public class LiveDataTests
{
	private sealed record Person(int Id, string Name);

	private static readonly PageScope Scope = PageScope.For("ListPeople");

	private static readonly Order<Person> ById = Order<Person>.By(p => p.Id);

	private static readonly Order<Person> ByName = Order<Person>.By(p => p.Name).ThenBy(p => p.Id);

	private readonly Pager _sut = new(new PlainTextPageTokenProtector(), Options.Create(new AntOptions()));

	// The live table. Ids 1 to 60, names n01 to n60, so id order and name order agree until a test renames someone.
	private readonly List<Person> _people = Enumerable.Range(1, 60).Select(i => new Person(i, $"n{i:D2}")).ToList();

	/// <summary>
	/// Deleting an item that was already served does not shift the next page: it still starts right after the
	/// bookmark. With an offset, every later item would move up one and one would never be shown.
	/// </summary>
	[Fact]
	public async Task AnEarlierItemDeleted_DoesNotShiftTheNextPage()
	{
		// Arrange
		var first = await Read(null, 30, ById);
		_people.RemoveAll(p => p.Id == 10);

		// Act
		var second = await Read(first.NextPageToken, 10, ById);

		// Assert
		second.ItemList.Select(p => p.Id).ShouldBe(Enumerable.Range(31, 10));
	}

	/// <summary>The page size is not bound to the token: a caller may continue with another size and gets the next items at it.</summary>
	[Fact]
	public async Task TheSizeMayChangeBetweenPages()
	{
		// Arrange
		var first = await Read(null, 5, ById);

		// Act
		var second = await Read(first.NextPageToken, 12, ById);

		// Assert
		second.Size.ShouldBe(12);
		second.ItemList.Select(p => p.Id).ShouldBe(Enumerable.Range(6, 12));
	}

	/// <summary>
	/// Continuable reads have no depth bound: walking 1100 items, far past the counted depth of 1000, is served to the
	/// end because no offset is involved.
	/// </summary>
	[Fact]
	public async Task ContinuationPastTheDepthBound_IsServed()
	{
		// Arrange
		_people.AddRange(Enumerable.Range(61, 1040).Select(i => new Person(i, $"n{i}")));
		PageToken? token = null;
		var seen = 0;

		// Act
		do
		{
			var page = await Read(token, 50, ById);
			seen += page.ItemList.Count;
			token = page.NextPageToken;
		}
		while (token is not null);

		// Assert
		seen.ShouldBe(1100);
	}

	/// <summary>
	/// During a traversal, a row inserted ahead of the bookmark is returned by a later page, and a row inserted behind it
	/// is not, though a fresh traversal does return it. This is live data without a snapshot.
	/// </summary>
	[Fact]
	public async Task ARowInsertedAheadOfTheBookmark_IsReturnedLater_AndOneBehindIsNot()
	{
		// Arrange
		var seen = new List<int>();

		// Act
		await foreach (var person in Traverse(ById, 10))
		{
			seen.Add(person.Id);

			if (person.Id == 20)
			{
				// Id 100 sorts after the bookmark, id 0 before it.
				_people.Add(new Person(100, "ahead"));
				_people.Add(new Person(0, "behind"));
			}
		}

		// Assert
		seen.ShouldContain(100);
		seen.ShouldNotContain(0);
		(await Traverse(ById, 10).ToListAsync()).Select(p => p.Id).ShouldContain(0);
	}

	/// <summary>
	/// In a name-ordered traversal, an unread item renamed to sort before the bookmark (n55 to a55, after n30 was passed)
	/// is never returned. The spec allows this; a consumer that cannot accept it orders by an immutable key.
	/// </summary>
	[Fact]
	public async Task AnUnreadItemRenamedBehindTheBookmark_MayBeOmitted()
	{
		// Arrange
		var seen = new List<string>();

		// Act
		await foreach (var person in Traverse(ByName, 10))
		{
			seen.Add(person.Name);

			if (person.Name == "n30")
				Rename(55, "a55");
		}

		// Assert: neither the old nor the new name was served.
		seen.ShouldNotContain("n55");
		seen.ShouldNotContain("a55");
	}

	/// <summary>
	/// In a name-ordered traversal, an item already returned and then renamed to sort after the bookmark (n05 to z05) is
	/// returned a second time. The spec allows this too.
	/// </summary>
	[Fact]
	public async Task AReadItemRenamedAheadOfTheBookmark_MayBeReturnedAgain()
	{
		// Arrange
		var seen = new List<int>();

		// Act
		await foreach (var person in Traverse(ByName, 10))
		{
			seen.Add(person.Id);

			if (person.Id == 30)
				Rename(5, "z05");
		}

		// Assert
		seen.Count(id => id == 5).ShouldBe(2);
	}

	/// <summary>
	/// Ordered by the immutable Id, the same renames neither omit nor repeat anything: the remedy the spec gives for the
	/// two cases above.
	/// </summary>
	[Fact]
	public async Task IdentifierOrdering_IsStableUnderRenames()
	{
		// Arrange
		var seen = new List<int>();

		// Act
		await foreach (var person in Traverse(ById, 10))
		{
			seen.Add(person.Id);

			if (person.Id == 30)
			{
				Rename(5, "z05");
				Rename(55, "a55");
			}
		}

		// Assert
		seen.ShouldBe(Enumerable.Range(1, 60));
	}

	/// <summary>
	/// A traversal whose second page read fails ends with that error; a new traversal keeps no checkpoint and starts again
	/// at the first page.
	/// </summary>
	[Fact]
	public async Task AfterAFailedTraversal_ANewOneStartsAtTheFirstPage()
	{
		// Arrange
		var attempts = 0;

		// Act: the second page read throws.
		await Should.ThrowAsync<System.TimeoutException>(async () =>
			await _sut.TraverseAsync<Person>(async (token, ct) =>
				++attempts == 2 ? throw new System.TimeoutException() : await Read(token, 10, ById)).ToListAsync());

		var again = await Traverse(ById, 10).ToListAsync();

		// Assert: no retry of the failed read, and the new traversal starts from the beginning.
		attempts.ShouldBe(2);
		again.First().Id.ShouldBe(1);
	}

	// Replaces a person in place, as an update of the row would.
	private void Rename(int id, string name) =>
		_people[_people.FindIndex(p => p.Id == id)] = new Person(id, name);

	// Each page reads a copy of the table as it is at that moment: live data, and safe to mutate the list mid-traversal.
	private IAsyncEnumerable<Person> Traverse(Order<Person> order, int size) =>
		_sut.TraverseAsync<Person>((token, ct) => _people.ToList().ToPageAsync(Paging.Continuable(token, size), order, Scope, _sut, ct));

	private async Task<ContinuablePage<Person>> Read(PageToken? token, int size, Order<Person> order) =>
		(await _people.ToList().ToPageAsync(Paging.Continuable(token, size), order, Scope, _sut)).ShouldBeOfType<ContinuablePage<Person>>();
}
