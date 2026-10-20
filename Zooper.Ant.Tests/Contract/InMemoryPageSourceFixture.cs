using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests.Contract;

/// <summary>
/// Builds the in-memory reference source for the conformance suite: a <see cref="QueryablePageSource{T}"/> over the rows
/// with the same LINQ order-and-seek builder a database provider receives, run by <see cref="InMemoryQueryExecutor"/>.
/// </summary>
public class InMemoryPageSourceFixture : IPageSourceFixture
{
	// Memory keeps every tick, so the expected order compares full timestamps and the source must not truncate them.
	public TimeSpan TimestampPrecision => TimeSpan.FromTicks(1);

	/// <summary>Returns a fresh in-memory source over exactly <paramref name="rows"/>, ordered by <paramref name="order"/>.</summary>
	public virtual Task<IPageSource<ContractRow>> CreateAsync(IReadOnlyList<ContractRow> rows, ContractOrder order, CancellationToken ct) =>
		Task.FromResult<IPageSource<ContractRow>>(
			new QueryablePageSource<ContractRow>(rows.AsQueryable(), order.ToOrder(), InMemoryQueryExecutor.Instance, TimeSpan.FromTicks(1)));
}

/// <summary>
/// Runs every case of <see cref="PageSourceContractTests{TFixture}"/> against the in-memory reference source (spec
/// <c>page-sources</c>, "The in-memory source is the reference implementation"). If this class fails, the shared
/// order-and-seek builder is wrong, and every LINQ adapter built on it would be wrong too.
/// </summary>
public sealed class InMemoryPageSourceContractTests(InMemoryPageSourceFixture fixture)
	: PageSourceContractTests<InMemoryPageSourceFixture>(fixture);
