using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Xunit;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests.Contract;

/// <summary>
/// Zooper.Ant's internal conformance suite for an <see cref="IPageSource{TItem}"/> (spec <c>page-sources</c>, "The
/// in-memory source is the reference implementation", with <c>keyset-ordering</c> and <c>continuation-tokens</c>). It
/// runs every kind, mixed directions, a null bookmark, reversal, token refusals, the depth bound, timestamp truncation
/// and a full traversal against the in-memory source, which runs the same LINQ order-and-seek builder a database
/// provider receives, so a builder defect fails here before any database is involved.
/// </summary>
/// <remarks>
/// Nothing of this ships: adapter authors test their own adapters. The suite is abstract over its fixture only so
/// <c>BrokenFixtureTests</c> can run the same cases against a deliberately wrong source. Each expected sequence comes
/// from <see cref="ContractOrder"/>'s hand-written sort, not from the builder under test.
/// </remarks>
/// <typeparam name="TFixture">Seeds rows and builds the source under test.</typeparam>
/// <param name="fixture">The fixture xUnit shares across the cases.</param>
public abstract class PageSourceContractTests<TFixture>(TFixture fixture) : IClassFixture<TFixture>
	where TFixture : class, IPageSourceFixture
{
	// The orders the cases read under. Each ends with Id, the unique tie-breaker.
	private static readonly ContractOrder ByCreatedAt = Order((ContractField.CreatedAt, false, false), (ContractField.Id, false, false));

	private static readonly ContractOrder ByScoreDescendingThenCreatedAt =
		Order((ContractField.Score, true, false), (ContractField.CreatedAt, false, false), (ContractField.Id, false, false));

	private static readonly ContractOrder ByScoreThenCreatedAtDescending =
		Order((ContractField.Score, false, false), (ContractField.CreatedAt, true, false), (ContractField.Id, false, false));

	private static readonly ContractOrder ByCreatedAtThenScore =
		Order((ContractField.CreatedAt, false, false), (ContractField.Score, false, false), (ContractField.Id, false, false));

	private static readonly ContractOrder ByName = Order((ContractField.Name, false, false), (ContractField.Id, false, false));

	// One read with one argument; the refusal cases change the name or the argument.
	private static readonly PageScope Scope = PageScope.For("ContractRead", "argument");

	/// <summary>
	/// The pager the suite reads through. It uses <see cref="PlainTextPageTokenProtector"/> so cases can look inside a
	/// token and forge one; encryption is covered by <c>DataProtectionPageTokenProtectorTests</c>.
	/// </summary>
	protected Pager Pager { get; } = new(new PlainTextPageTokenProtector(), Options.Create(new AntOptions()));

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	// The expected order compares timestamps at the store's precision; see IPageSourceFixture.TimestampPrecision.
	private TimeSpan Precision => ((IPageSourceFixture)fixture).TimestampPrecision;

	/// <summary>
	/// Counted, continuable and capped pages of one read hold the same rows in the same order (spec <c>paged-reads</c>,
	/// "Same order across kinds"). Without this a client switching kinds would see a different list.
	/// </summary>
	[Fact]
	public async Task EveryKind_ServesTheSameRowsInTheSameOrder()
	{
		// Arrange
		var rows = ContractRows.Standard;
		var expected = ByCreatedAt.Sort(rows, Precision);
		var source = await fixture.CreateAsync(rows, ByCreatedAt, Ct);

		// Act
		var counted = Assert.IsType<CountedPage<ContractRow>>(await Pager.ReadAsync(source, Paging.Counted(2, 5), Scope, Ct));
		var first = Assert.IsType<CountedPage<ContractRow>>(await Pager.ReadAsync(source, Paging.Counted(1, 5), Scope, Ct));
		var continuable = Assert.IsType<ContinuablePage<ContractRow>>(await Pager.ReadAsync(source, Paging.Continuable(null, 5), Scope, Ct));
		var capped = Assert.IsType<CappedPage<ContractRow>>(await Pager.ReadAsync(source, Paging.Capped(5), Scope, Ct));

		// Assert
		Assert.Equal(rows.Count, counted.TotalCount);
		AssertSequence(expected.Skip(5).Take(5), counted.ItemList, "counted page 2");
		AssertSequence(expected.Take(5), first.ItemList, "counted page 1");
		AssertSequence(expected.Take(5), continuable.ItemList, "first continuable page");
		AssertSequence(expected.Take(5), capped.ItemList, "capped page");
		Assert.NotNull(continuable.NextPageToken);
		Assert.True(capped.Truncated);
	}

	/// <summary>
	/// A chain mixing a descending key with ascending ones (score descending, then time, then id) is followed page by
	/// page without a gap or a repeat: "beyond" means lower on the descending key and higher on the others.
	/// </summary>
	[Fact]
	public async Task MixedDirections_TraverseInChainOrder()
	{
		// Arrange
		var source = await fixture.CreateAsync(ContractRows.Standard, ByScoreDescendingThenCreatedAt, Ct);

		// Act
		var (rows, _) = await Walk(source, 3);

		// Assert
		AssertSequence(ByScoreDescendingThenCreatedAt.Sort(ContractRows.Standard, Precision), rows, "mixed directions");
	}

	/// <summary>
	/// When a page ends on a row whose nulls-last score is null, the next page seeks only among the remaining null rows
	/// (spec <c>keyset-ordering</c>, "Null bookmark"). A naive "score greater than null" would match nothing and end the
	/// read early.
	/// </summary>
	[Fact]
	public async Task NullBookmark_SeeksOnlyAmongNulls()
	{
		// Arrange
		var source = await fixture.CreateAsync(ContractRows.Standard, ByScoreThenCreatedAtDescending, Ct);

		// Act
		var (rows, tokens) = await Walk(source, 2);

		// Assert
		AssertSequence(ByScoreThenCreatedAtDescending.Sort(ContractRows.Standard, Precision), rows, "null bookmark");
		// Proves the case really crossed a null bookmark: at least one token holds a null first value.
		Assert.Contains(tokens, token => PlainTextPageTokenProtector.Read(token).GetProperty("p")[0].ValueKind == JsonValueKind.Null);
	}

	/// <summary>
	/// A chain served through <see cref="Order{T}.Reverse"/> comes out exactly backwards, and continuation seeks under
	/// the reversed order too (spec <c>keyset-ordering</c>, "Reversed chain").
	/// </summary>
	[Fact]
	public async Task Reversal_TraversesInReversedOrder()
	{
		// Arrange
		var reversed = ByCreatedAtThenScore.Reverse();
		var source = await fixture.CreateAsync(ContractRows.Standard, reversed, Ct);

		// Act
		var (rows, _) = await Walk(source, 4);

		// Assert
		AssertSequence(ByCreatedAtThenScore.Sort(ContractRows.Standard, Precision).Reverse(), rows, "reversal");
	}

	/// <summary>
	/// A token issued by one read is refused by another, even over the same rows and order (spec
	/// <c>continuation-tokens</c>, "Foreign read"), so a token can never continue a list it did not come from.
	/// </summary>
	[Fact]
	public async Task ForeignRead_IsRefused()
	{
		// Arrange
		var source = await fixture.CreateAsync(ContractRows.Standard, ByCreatedAt, Ct);
		var token = await FirstToken(source, Scope);

		// Act & Assert
		await Assert.ThrowsAsync<InvalidPageTokenException>(
			() => Pager.ReadAsync(source, Paging.Continuable(token, 5), PageScope.For("OtherRead", "argument"), Ct));
	}

	/// <summary>
	/// A token presented with another argument value for the same read is refused (spec <c>continuation-tokens</c>,
	/// "Changed criteria"), instead of silently continuing a different result set.
	/// </summary>
	[Fact]
	public async Task ChangedArguments_AreRefused()
	{
		// Arrange
		var source = await fixture.CreateAsync(ContractRows.Standard, ByCreatedAt, Ct);
		var token = await FirstToken(source, Scope);

		// Act & Assert
		await Assert.ThrowsAsync<InvalidPageTokenException>(
			() => Pager.ReadAsync(source, Paging.Continuable(token, 5), PageScope.For("ContractRead", "other argument"), Ct));
	}

	/// <summary>
	/// A token that passes the binding but whose position has too few values, or values of the wrong type, is refused
	/// as an invalid token rather than served or crashing (spec <c>continuation-tokens</c>, "A token whose payload no
	/// longer fits the read is refused").
	/// </summary>
	[Fact]
	public async Task TamperedToken_IsRefused()
	{
		// Arrange
		var source = await fixture.CreateAsync(ContractRows.Standard, ByCreatedAt, Ct);
		var payload = PlainTextPageTokenProtector.Read(await FirstToken(source, Scope));
		// Keep the genuine read name and fingerprint so only the position is wrong.
		var (r, q) = (payload.GetProperty("r").GetString(), payload.GetProperty("q").GetString());

		var shortPosition = PlainTextPageTokenProtector.Write(new { v = 1, r, q, p = new object?[] { "2026-01-01T00:00:00Z" } });
		var wrongType = PlainTextPageTokenProtector.Write(new { v = 1, r, q, p = new object?[] { "not a timestamp", "not an id" } });

		// Act & Assert
		await Assert.ThrowsAsync<InvalidPageTokenException>(() => Pager.ReadAsync(source, Paging.Continuable(shortPosition, 5), Scope, Ct));
		await Assert.ThrowsAsync<InvalidPageTokenException>(() => Pager.ReadAsync(source, Paging.Continuable(wrongType, 5), Scope, Ct));
	}

	/// <summary>
	/// A counted page one past the depth bound is refused with a message naming the bound, before any source is asked
	/// (spec <c>paged-reads</c>, "Depth beyond the bound").
	/// </summary>
	[Fact]
	public void DepthBeyondBound_IsRefused()
	{
		// Arrange
		var limits = Pager.Limits;
		var page = limits.MaximumDepth / limits.DefaultSize + 1;

		// Act
		var exception = Assert.Throws<PagingException>(() => Paging.Counted(page, limits.DefaultSize, limits));

		// Assert
		Assert.Contains(limits.MaximumDepth.ToString(), exception.Message);
	}

	/// <summary>
	/// Rows in one microsecond that differ below it are each served exactly once across page boundaries: a store that
	/// keeps microseconds ties them, so the bookmark must be truncated to match the stored value.
	/// </summary>
	[Fact]
	public async Task TimestampTruncation_ServesEachRowOnce()
	{
		// Arrange
		var source = await fixture.CreateAsync(ContractRows.Standard, ByCreatedAtThenScore, Ct);

		// Act: size 1 puts a page boundary between every pair of rows, including the sub-microsecond pair.
		var (rows, _) = await Walk(source, 1);

		// Assert
		AssertSequence(ByCreatedAtThenScore.Sort(ContractRows.Standard, Precision), rows, "timestamp truncation");
	}

	/// <summary>
	/// Following every token through <see cref="Pager.TraverseAsync{TItem}"/> sees every row exactly once and in chain
	/// order. Ordering by duplicated names with size 2 puts page boundaries inside ties, so this is the case that fails,
	/// naming the row, when the seek disagrees with the order (spec <c>page-sources</c>, "Seek and order disagree").
	/// </summary>
	[Fact]
	public async Task FullTraversal_SeesEveryRowExactlyOnce()
	{
		// Arrange
		var source = await fixture.CreateAsync(ContractRows.Standard, ByName, Ct);

		// Act
		var rows = await Pager.TraverseAsync<ContractRow>(
			(token, ct) => Pager.ReadAsync(source, Paging.Continuable(token, 2), Scope, ct), Ct).ToListAsync(Ct);

		// Assert
		AssertSequence(ByName.Sort(ContractRows.Standard, Precision), rows, "full traversal");
	}

	// Reads the first continuable page and returns its next token, the token the refusal cases misuse.
	private async Task<PageToken> FirstToken(IPageSource<ContractRow> source, PageScope scope)
	{
		var page = Assert.IsType<ContinuablePage<ContractRow>>(await Pager.ReadAsync(source, Paging.Continuable(null, 5), scope, Ct));

		return page.NextPageToken ?? throw new InvalidOperationException("The first page issued no token.");
	}

	// Follows next tokens to the end by hand (not through TraverseAsync) so the issued tokens can be inspected.
	private async Task<(List<ContractRow> Rows, List<PageToken> Tokens)> Walk(IPageSource<ContractRow> source, int size)
	{
		var (rows, tokens) = (new List<ContractRow>(), new List<PageToken>());
		PageToken? token = null;

		do
		{
			// A source that keeps issuing tokens would loop forever; fail instead.
			if (tokens.Count > ContractRows.Standard.Count)
				Assert.Fail("The read did not end: a next token was issued past the last row.");

			var page = Assert.IsType<ContinuablePage<ContractRow>>(await Pager.ReadAsync(source, Paging.Continuable(token, size), Scope, Ct));
			rows.AddRange(page.ItemList);
			token = page.NextPageToken;

			if (token is { } next)
				tokens.Add(next);
		}
		while (token is not null);

		return (rows, tokens);
	}

	// Fails with a message naming the first row skipped, repeated, foreign or out of place, so a failure says which
	// row the source got wrong instead of dumping two lists.
	private static void AssertSequence(IEnumerable<ContractRow> expected, IReadOnlyList<ContractRow> actual, string @case)
	{
		var expectedList = expected.ToList();

		AssertExactlyOnce(expectedList, actual, @case);

		for (var i = 0; i < expectedList.Count; i++)
			if (expectedList[i].Id != actual[i].Id)
				Assert.Fail($"{@case}: position {i} holds {Describe(actual[i])}, expected {Describe(expectedList[i])}.");
	}

	// Every expected row exactly once, and nothing else.
	private static void AssertExactlyOnce(IReadOnlyList<ContractRow> expected, IReadOnlyList<ContractRow> actual, string @case)
	{
		foreach (var row in expected)
		{
			var seen = actual.Count(a => a.Id == row.Id);

			if (seen != 1)
				Assert.Fail($"{@case}: {Describe(row)} was {(seen == 0 ? "skipped" : $"repeated {seen} times")}.");
		}

		if (actual.FirstOrDefault(a => expected.All(e => e.Id != a.Id)) is { } stranger)
			Assert.Fail($"{@case}: {Describe(stranger)} was served but is not in the read.");
	}

	private static string Describe(ContractRow row) =>
		$"row {row.Id} ({row.Name}, {row.CreatedAt:O}, score {row.Score?.ToString() ?? "null"})";

	// Builds an order from (field, descending, nullsFirst) tuples.
	private static ContractOrder Order(params (ContractField Field, bool Descending, bool NullsFirst)[] keys) =>
		new(keys.Select(k => new ContractOrderKey(k.Field, k.Descending, k.NullsFirst)).ToList());
}
