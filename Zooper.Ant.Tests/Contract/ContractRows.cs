using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests.Contract;

/// <summary>
/// The row type of Zooper.Ant's internal conformance suite for the in-memory reference source (spec <c>page-sources</c>).
/// Four fields are enough to exercise every ordering rule: a unique key, a timestamp, a nullable number and a
/// duplicated text.
/// </summary>
/// <param name="Id">The unique tie-breaker.</param>
/// <param name="CreatedAt">A UTC timestamp, with ties and sub-microsecond ticks in the standard set.</param>
/// <param name="Score">A nullable number.</param>
/// <param name="Name">A non-null name with duplicates.</param>
public sealed record ContractRow(Guid Id, DateTime CreatedAt, double? Score, string Name);

/// <summary>The rows the conformance suite seeds into the source under test.</summary>
public static class ContractRows
{
	private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <summary>
	/// The standard set. Each edge case is here because one suite case depends on it:
	/// <list type="bullet">
	/// <item>three rows tied on <see cref="ContractRow.CreatedAt"/> (<c>T0</c>), so ordering by time alone is not total
	/// and the chain's later keys must decide;</item>
	/// <item>five null scores, so nulls-last ordering and a null bookmark are exercised;</item>
	/// <item>duplicate names ("ada" four times), so the full traversal by name crosses page boundaries inside a tie and
	/// only the <c>Id</c> tie-breaker keeps it exact;</item>
	/// <item>two rows in the same microsecond that differ below it (+3 and +7 ticks), so the timestamp-truncation rule
	/// matters for a store that keeps microseconds.</item>
	/// </list>
	/// The pair (<c>Score</c>, <c>CreatedAt</c>) is unique, so in memory only the full-traversal case depends on the
	/// <c>Id</c> tie-breaker. That is what lets <c>BrokenFixtureTests</c> show a seek without it fails exactly one case.
	/// </summary>
	public static IReadOnlyList<ContractRow> Standard { get; } =
	[
		Row(0x30, T0, 0.5, "ada"),
		Row(0x10, T0, null, "bob"),
		Row(0xa0, T0, 0.9, "ada"),
		Row(0x05, T0.AddHours(1), 0.5, "cid"),
		Row(0xf0, T0.AddHours(1), null, "bob"),
		Row(0x20, T0.AddHours(2), 0.1, "dan"),
		Row(0x40, T0.AddHours(2), null, "ada"),
		Row(0x60, T0.AddHours(3).AddTicks(3), 0.7, "eve"),
		Row(0x50, T0.AddHours(3).AddTicks(7), 0.7, "eve"),
		Row(0x70, T0.AddHours(3).AddTicks(10), 0.7, "fay"),
		Row(0x15, T0.AddHours(4), null, "gus"),
		Row(0x25, T0.AddHours(4), 0.3, "cid"),
		Row(0x35, T0.AddHours(5), 0.9, "bob"),
		Row(0x45, T0.AddHours(5), 0.3, "hal"),
		Row(0x55, T0.AddHours(6), null, "ada"),
		Row(0x65, T0.AddHours(6), 0.5, "ivy")
	];

	// Ids differ in the first Guid group only, so Guid comparison orders them by that number, as a database's uuid
	// comparison would.
	private static ContractRow Row(uint id, DateTime createdAt, double? score, string name) =>
		new(new Guid(id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), createdAt, score, name);
}

/// <summary>A field of <see cref="ContractRow"/> the suite orders by.</summary>
public enum ContractField
{
	/// <summary><see cref="ContractRow.Id"/>.</summary>
	Id,

	/// <summary><see cref="ContractRow.CreatedAt"/>.</summary>
	CreatedAt,

	/// <summary><see cref="ContractRow.Score"/>, the only nullable field.</summary>
	Score,

	/// <summary><see cref="ContractRow.Name"/>, compared ordinally.</summary>
	Name
}

/// <summary>One key of a <see cref="ContractOrder"/>.</summary>
/// <param name="Field">The field.</param>
/// <param name="Descending">Whether the key sorts descending.</param>
/// <param name="NullsFirst">Whether nulls sort first; only <see cref="ContractField.Score"/> has nulls.</param>
public sealed record ContractOrderKey(ContractField Field, bool Descending = false, bool NullsFirst = false);

/// <summary>
/// An order the suite asks a fixture for, as plain descriptors. The fixture maps them to an <see cref="Order{T}"/> with
/// <see cref="ToOrder"/>, and the suite computes the expected sequence from the same descriptors with <c>Sort</c>, an
/// independent oracle written without the LINQ builder. When <see cref="Reversed"/> is set, the chain is served
/// reversed through <see cref="Order{T}.Reverse"/>, so reversal itself is under test.
/// </summary>
/// <param name="Keys">The keys in chain order, <see cref="ContractField.Id"/> last.</param>
/// <param name="Reversed">Whether the chain is served reversed.</param>
public sealed record ContractOrder(IReadOnlyList<ContractOrderKey> Keys, bool Reversed = false)
{
	/// <summary>The chain reversed as a whole.</summary>
	public ContractOrder Reverse() => this with { Reversed = !Reversed };

	/// <summary>Maps the descriptors to the LINQ chain the in-memory source runs, reversed when <see cref="Reversed"/> is set.</summary>
	/// <returns>The chain over <see cref="ContractRow"/>.</returns>
	public Order<ContractRow> ToOrder()
	{
		Order<ContractRow>? chain = null;

		foreach (var key in Keys)
			chain = key.Field switch
			{
				ContractField.Id => Then(chain, r => r.Id, key),
				ContractField.CreatedAt => Then(chain, r => r.CreatedAt, key),
				ContractField.Score => Then(chain, r => r.Score, key),
				_ => Then(chain, r => r.Name, key)
			};

		return chain is null ? throw new InvalidOperationException("A contract order has at least one key.")
			: Reversed ? chain.Reverse() : chain;
	}

	/// <summary>
	/// The expected order, computed by hand: nulls placed per key, timestamps compared at the store's precision so rows
	/// the store ties fall back to the next key, names compared ordinally.
	/// </summary>
	internal IReadOnlyList<ContractRow> Sort(IEnumerable<ContractRow> rows, TimeSpan timestampPrecision)
	{
		var keys = Reversed ? Keys.Select(k => new ContractOrderKey(k.Field, !k.Descending, !k.NullsFirst)).ToList() : Keys;
		var list = rows.ToList();

		list.Sort((a, b) => keys.Select(key => Compare(key, a, b, timestampPrecision.Ticks)).FirstOrDefault(c => c != 0));

		return list;
	}

	private static Order<ContractRow> Then<TKey>(Order<ContractRow>? chain, Expression<Func<ContractRow, TKey>> key, ContractOrderKey descriptor) =>
		(chain, descriptor.Descending) switch
		{
			(null, false) => Order<ContractRow>.By(key, descriptor.NullsFirst),
			(null, true) => Order<ContractRow>.ByDescending(key, descriptor.NullsFirst),
			(_, false) => chain.ThenBy(key, descriptor.NullsFirst),
			(_, true) => chain.ThenByDescending(key, descriptor.NullsFirst)
		};

	private static int Compare(ContractOrderKey key, ContractRow a, ContractRow b, long precisionTicks)
	{
		if (key.Field == ContractField.Score && (a.Score is null || b.Score is null))
			return (a.Score is null).CompareTo(b.Score is null) * (key.NullsFirst ? -1 : 1);

		var compared = key.Field switch
		{
			ContractField.Id => a.Id.CompareTo(b.Id),
			ContractField.CreatedAt => (a.CreatedAt.Ticks / precisionTicks).CompareTo(b.CreatedAt.Ticks / precisionTicks),
			ContractField.Score => a.Score!.Value.CompareTo(b.Score!.Value),
			_ => string.CompareOrdinal(a.Name, b.Name)
		};

		return key.Descending ? -compared : compared;
	}
}
