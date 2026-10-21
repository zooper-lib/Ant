using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests.Contract;

/// <summary>
/// Connects <see cref="PageSourceContractTests{TFixture}"/> to the source under test: seeds rows and builds the source.
/// Zooper.Ant has two fixtures, the in-memory reference source and a deliberately broken variant; the abstraction exists
/// so the same cases can prove both a correct source passes and an incorrect one fails.
/// </summary>
public interface IPageSourceFixture
{
	/// <summary>
	/// Seeds exactly <paramref name="rows"/> (replacing what an earlier call seeded) and returns a source over them
	/// ordered by <paramref name="order"/>, mapped to the store's own chain form.
	/// </summary>
	/// <param name="rows">The rows the read must contain, and no others.</param>
	/// <param name="order">The order to map; reverse the chain when <see cref="ContractOrder.Reversed"/> is set.</param>
	/// <param name="ct">Cancels seeding.</param>
	/// <returns>The source under test.</returns>
	Task<IPageSource<ContractRow>> CreateAsync(IReadOnlyList<ContractRow> rows, ContractOrder order, CancellationToken ct);

	/// <summary>
	/// The precision the store keeps for <see cref="ContractRow.CreatedAt"/>. The suite compares timestamps at it when it
	/// computes the expected order. A database that keeps microseconds ties the standard set's sub-microsecond pair and
	/// orders it by <c>Id</c>; memory keeps every tick and orders it by time. Without this the expected order would be
	/// right for one store and wrong for the other. Defaults to one microsecond, PostgreSQL's precision.
	/// </summary>
	TimeSpan TimestampPrecision => TimeSpan.FromMicroseconds(1);
}
