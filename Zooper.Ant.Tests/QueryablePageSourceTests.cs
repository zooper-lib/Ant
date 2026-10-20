using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using Xunit;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies that <see cref="QueryablePageSource{T}"/> builds the ordered, filtered and sought query itself and hands
/// only its execution to the <see cref="IQueryExecutor"/> (spec <c>page-sources</c>, "LINQ-queryable stores need only
/// an executor"). An adapter for a new LINQ provider supplies nothing but that executor, so every call must go
/// through it and every call must cover the same filtered result set.
/// </summary>
public class QueryablePageSourceTests
{
	/// <summary>A row with a unique id and a group to filter on. Public, because NSubstitute proxies generic calls over it.</summary>
	public sealed record Row(int Id, string Group);

	/// <summary>The read under test: five rows in no particular order, filtered to group "a" (ids 1, 3, 4, 5).</summary>
	private static readonly IQueryable<Row> Query =
		new[] { new Row(5, "a"), new Row(2, "b"), new Row(4, "a"), new Row(1, "a"), new Row(3, "a") }.AsQueryable().Where(r => r.Group == "a");

	/// <summary>
	/// A mocked executor that runs whatever query it is given in memory. Asserting on its results shows which query the
	/// source built; asserting on its calls shows the source executed nothing itself.
	/// </summary>
	private readonly IQueryExecutor _executor = Substitute.For<IQueryExecutor>();

	/// <summary>The source under test, ordered by id over <see cref="Query"/>.</summary>
	private readonly QueryablePageSource<Row> _sut;

	/// <summary>Wires the mocked executor to run queries in memory and builds the source over it.</summary>
	public QueryablePageSourceTests()
	{
		_executor.ToListAsync(Arg.Any<IQueryable<Row>>(), Arg.Any<CancellationToken>())
			.Returns(call => (IReadOnlyList<Row>)call.Arg<IQueryable<Row>>().ToList());
		_executor.CountAsync(Arg.Any<IQueryable<Row>>(), Arg.Any<CancellationToken>())
			.Returns(call => call.Arg<IQueryable<Row>>().Count());
		_sut = new QueryablePageSource<Row>(Query, Order<Row>.By(r => r.Id), _executor);
	}

	/// <summary>
	/// An offset read hands the executor one query that is filtered, ordered by the chain, skipped and limited: offset
	/// 1, limit 2 over ids 1, 3, 4, 5 gives 3 and 4.
	/// </summary>
	[Fact]
	public async Task ReadAsync_DelegatesTheOrderedFilteredRangeToTheExecutor()
	{
		// Act
		var items = await _sut.ReadAsync(1, 2, CancellationToken.None);

		// Assert
		items.Select(r => r.Id).ShouldBe([3, 4]);
		await _executor.Received(1).ToListAsync(Arg.Any<IQueryable<Row>>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// A read after a position hands the executor the filtered query with the seek applied: after id 3 come 4 and 5,
	/// and the group "b" row is still excluded.
	/// </summary>
	[Fact]
	public async Task ReadAfterAsync_DelegatesTheSeekToTheExecutor()
	{
		// Act
		var items = await _sut.ReadAfterAsync(new PagePosition([3]), 5, CancellationToken.None);

		// Assert
		items.Select(r => r.Id).ShouldBe([4, 5]);
		await _executor.Received(1).ToListAsync(Arg.Any<IQueryable<Row>>(), Arg.Any<CancellationToken>());
	}

	/// <summary>A count hands the executor the filtered query itself, unordered, so the total covers the same rows as the pages.</summary>
	[Fact]
	public async Task CountAsync_DelegatesTheFilteredQueryToTheExecutor()
	{
		// Act & Assert
		(await _sut.CountAsync(CancellationToken.None)).ShouldBe(4);
		await _executor.Received(1).CountAsync(Query, Arg.Any<CancellationToken>());
	}

	/// <summary>The source reports its order's signature, which the pager folds into the token binding.</summary>
	[Fact]
	public void OrderSignature_IsTheOrdersSignature()
	{
		// Act & Assert
		_sut.OrderSignature.ShouldBe(Order<Row>.By(r => r.Id).OrderSignature);
	}

	/// <summary>
	/// Without an explicit precision, a timestamp position is truncated to one microsecond, the precision of PostgreSQL
	/// and most stores: 7 ticks above midnight become midnight.
	/// </summary>
	[Fact]
	public void PositionOf_TruncatesTimestampsToTheGivenPrecision()
	{
		// Arrange
		var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var source = new QueryablePageSource<DateTime>(Array.Empty<DateTime>().AsQueryable(), Order<DateTime>.By(d => d), _executor);

		// Act & Assert
		source.PositionOf(at.AddTicks(7)).Values[0].ShouldBe(at);
	}
}
