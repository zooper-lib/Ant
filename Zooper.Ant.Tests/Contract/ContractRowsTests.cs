using System;
using System.Linq;
using Shouldly;
using Xunit;

namespace Zooper.Ant.Tests.Contract;

/// <summary>
/// Guards <see cref="ContractRows.Standard"/>: each conformance case only proves something if the data holds the edge case
/// it depends on. Editing the set so a case passes without its edge case breaks one of these tests instead of silently
/// weakening the suite.
/// </summary>
public class ContractRowsTests
{
	private readonly System.Collections.Generic.IReadOnlyList<ContractRow> _sut = ContractRows.Standard;

	/// <summary>Every Id is unique, which is what makes it a valid tie-breaker at the end of every order.</summary>
	[Fact]
	public void Standard_HasUniqueIds() =>
		// Act & Assert
		_sut.Select(r => r.Id).Distinct().Count().ShouldBe(_sut.Count);

	/// <summary>At least three rows share one timestamp, so an order by time alone is not total and later keys must decide.</summary>
	[Fact]
	public void Standard_HasThreeRowsTiedOnCreatedAt() =>
		// Act & Assert
		_sut.GroupBy(r => r.CreatedAt).Max(g => g.Count()).ShouldBeGreaterThanOrEqualTo(3);

	/// <summary>Several scores are null, so nulls-last ordering and a null bookmark get exercised.</summary>
	[Fact]
	public void Standard_HasNullScores() =>
		// Act & Assert
		_sut.Count(r => r.Score is null).ShouldBeGreaterThanOrEqualTo(2);

	/// <summary>A name occurs at least three times, so the full traversal by name crosses a page boundary inside a tie.</summary>
	[Fact]
	public void Standard_HasDuplicateNames() =>
		// Act & Assert
		_sut.GroupBy(r => r.Name).Max(g => g.Count()).ShouldBeGreaterThanOrEqualTo(3);

	/// <summary>
	/// Two rows fall in the same microsecond but differ below it, so the timestamp case shows what a store keeping
	/// microseconds does with them.
	/// </summary>
	[Fact]
	public void Standard_HasRowsInOneMicrosecondDifferingBelowIt() =>
		// Act & Assert: ten ticks are one microsecond.
		_sut.GroupBy(r => r.CreatedAt.Ticks / 10).Any(g => g.Select(r => r.CreatedAt).Distinct().Count() > 1).ShouldBeTrue();

	/// <summary>
	/// No two rows share both score and timestamp, so in memory only the full traversal by name needs the Id. This is
	/// what lets <see cref="BrokenFixtureTests"/> expect exactly one failing case.
	/// </summary>
	[Fact]
	public void Standard_HasAUniqueScoreAndCreatedAtPair_SoOnlyTheFullTraversalNeedsTheTieBreaker() =>
		// Act & Assert
		_sut.Select(r => (r.Score, r.CreatedAt)).Distinct().Count().ShouldBe(_sut.Count);

	/// <summary>All timestamps are UTC, so a token's timestamp round trip cannot shift them by a time zone.</summary>
	[Fact]
	public void Standard_IsUtc() =>
		// Act & Assert
		_sut.ShouldAllBe(r => r.CreatedAt.Kind == DateTimeKind.Utc);
}
