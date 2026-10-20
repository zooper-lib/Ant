using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using Shouldly;
using Xunit;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies the LINQ order-and-seek builder that every LINQ store receives (spec <c>keyset-ordering</c>): the ORDER BY
/// it generates, the seek predicate that selects the rows after a bookmark, and how positions are written and read
/// back. The order and the seek come from one declaration; if they ever disagree, continuation skips or repeats rows,
/// so the main check runs the seek from every row of every order and compares it with a hand-written oracle.
/// </summary>
public class SeekExpressionBuilderTests
{
	/// <summary>An enum key, compared through its underlying integer.</summary>
	public enum Colour { Red, Green, Blue }

	/// <summary>A row with one key of each kind the builder handles: int, DateTime, nullable double, string, nullable string, Guid, enum.</summary>
	public sealed record Row(int Id, DateTime CreatedAt, double? Score, string Name, string? Nickname, Guid Key, Colour Colour);

	/// <summary>The base timestamp of the rows.</summary>
	private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <summary>
	/// Eight rows built to put ties and nulls on every key, so no order is decided by its first key alone:
	/// <list type="bullet">
	/// <item><c>CreatedAt</c> ties: rows 1, 2, 5 at T0; rows 3, 4, 8 at T0 + 1h; rows 6, 7 at T0 + 2h.</item>
	/// <item><c>Score</c> nulls: rows 2, 5, 7; ties at 0.5 (rows 1, 3) and 0.9 (rows 4, 8).</item>
	/// <item><c>Name</c> duplicates: "ada" (rows 1, 3, 5) and "bob" (rows 2, 6).</item>
	/// <item><c>Nickname</c> nulls: rows 1, 4, 7; duplicate "b" (rows 2, 6).</item>
	/// <item><c>Key</c> Guids differ in their first group only and are not in Id order.</item>
	/// <item><c>Colour</c> ties: three rows each of Red and Blue, two of Green.</item>
	/// </list>
	/// </summary>
	private static readonly IReadOnlyList<Row> Rows =
	[
		new(1, T0, 0.5, "ada", null, new Guid("00000003-0000-0000-0000-000000000000"), Colour.Blue),
		new(2, T0, null, "bob", "b", new Guid("00000001-0000-0000-0000-000000000000"), Colour.Red),
		new(3, T0.AddHours(1), 0.5, "ada", "a", new Guid("00000002-0000-0000-0000-000000000000"), Colour.Green),
		new(4, T0.AddHours(1), 0.9, "cid", null, new Guid("0000000a-0000-0000-0000-000000000000"), Colour.Blue),
		new(5, T0, null, "ada", "z", new Guid("ffffffff-0000-0000-0000-000000000000"), Colour.Red),
		new(6, T0.AddHours(2), 0.1, "bob", "b", new Guid("00000005-0000-0000-0000-000000000000"), Colour.Green),
		new(7, T0.AddHours(2), null, "dan", null, new Guid("00000004-0000-0000-0000-000000000000"), Colour.Red),
		new(8, T0.AddHours(1), 0.9, "eve", "e", new Guid("00000006-0000-0000-0000-000000000000"), Colour.Blue)
	];

	/// <summary>The names of <see cref="OrderCases"/>, as theory data.</summary>
	public static TheoryData<string> Orders => [.. OrderCases.Keys];

	/// <summary>
	/// Each order under test with its oracle: the same order written with plain LINQ-to-objects, which is trusted.
	/// A null-rank key (<c>Score is null</c>) in the oracle places nulls; strings compare ordinally, which agrees with
	/// the builder because every name is lowercase ASCII.
	/// </summary>
	private static readonly Dictionary<string, (Order<Row> Order, Func<IEnumerable<Row>, IEnumerable<Row>> Oracle)> OrderCases = new()
	{
		// Nullable key ascending, nulls after every value (the default).
		["score asc nulls last"] = (Order<Row>.By(r => r.Score).ThenBy(r => r.Id),
			rows => rows.OrderBy(r => r.Score is null).ThenBy(r => r.Score).ThenBy(r => r.Id)),
		// Nullable key ascending, nulls before every value.
		["score asc nulls first"] = (Order<Row>.By(r => r.Score, nullsFirst: true).ThenBy(r => r.Id),
			rows => rows.OrderBy(r => r.Score is not null).ThenBy(r => r.Score).ThenBy(r => r.Id)),
		// Nullable key descending: nulls still last, so "beyond" means lower values, then nulls.
		["score desc nulls last"] = (Order<Row>.ByDescending(r => r.Score).ThenBy(r => r.Id),
			rows => rows.OrderBy(r => r.Score is null).ThenByDescending(r => r.Score).ThenBy(r => r.Id)),
		// Nullable key descending with nulls first.
		["score desc nulls first"] = (Order<Row>.ByDescending(r => r.Score, nullsFirst: true).ThenBy(r => r.Id),
			rows => rows.OrderBy(r => r.Score is not null).ThenByDescending(r => r.Score).ThenBy(r => r.Id)),
		// Three keys, mixed directions, a nullable key in the middle: the spec's three-key seek.
		["created asc, score desc, id asc"] = (Order<Row>.By(r => r.CreatedAt).ThenByDescending(r => r.Score).ThenBy(r => r.Id),
			rows => rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.Score is null).ThenByDescending(r => r.Score).ThenBy(r => r.Id)),
		// A string key, compared through string.Compare because strings have no operator.
		["name desc, id asc"] = (Order<Row>.ByDescending(r => r.Name).ThenBy(r => r.Id),
			rows => rows.OrderByDescending(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id)),
		// A nullable reference key, detected from the nullable annotation, with a descending tie-breaker.
		["nickname asc nulls last"] = (Order<Row>.By(r => r.Nickname).ThenByDescending(r => r.Id),
			rows => rows.OrderBy(r => r.Nickname is null).ThenBy(r => r.Nickname, StringComparer.Ordinal).ThenByDescending(r => r.Id)),
		// A Guid as the only key, compared through CompareTo; the Guid is unique, so it is its own tie-breaker.
		["guid asc"] = (Order<Row>.By(r => r.Key),
			rows => rows.OrderBy(r => r.Key)),
		// An enum key, compared through its underlying integer.
		["colour desc, id desc"] = (Order<Row>.ByDescending(r => r.Colour).ThenByDescending(r => r.Id),
			rows => rows.OrderByDescending(r => r.Colour).ThenByDescending(r => r.Id))
	};

	/// <summary>
	/// The generated ORDER BY sorts the rows exactly like the oracle, for every order in <see cref="OrderCases"/>:
	/// nulls first and last, ascending and descending, string, Guid and enum keys.
	/// </summary>
	[Theory]
	[MemberData(nameof(Orders))]
	public void ApplyOrdering_MatchesTheOracle(string name)
	{
		// Arrange
		var (order, oracle) = OrderCases[name];

		// Act
		var ordered = SeekExpressionBuilder.ApplyOrdering(Rows.AsQueryable(), order).ToList();

		// Assert
		ordered.Select(r => r.Id).ShouldBe(oracle(Rows).Select(r => r.Id));
	}

	/// <summary>
	/// For every order in <see cref="OrderCases"/> and every row used as the bookmark, the seek selects exactly the rows
	/// that follow it in the oracle order. This is the property continuation depends on: no row skipped, no row repeated,
	/// whatever the row is and wherever the page boundary falls.
	/// </summary>
	[Theory]
	[MemberData(nameof(Orders))]
	public void BuildSeek_FromEveryRow_SelectsExactlyTheRowsAfterIt(string name)
	{
		// Arrange
		var (order, oracle) = OrderCases[name];
		var expected = oracle(Rows).ToList();

		for (var i = 0; i < expected.Count; i++)
		{
			// One-tick precision: these rows live in memory, where nothing is truncated.
			var position = SeekExpressionBuilder.PositionOf(order, expected[i], TimeSpan.FromTicks(1));

			// Act
			var seek = SeekExpressionBuilder.BuildSeek(order, position);
			var after = SeekExpressionBuilder.ApplyOrdering(Rows.AsQueryable().Where(seek), order).Select(r => r.Id).ToList();

			// Assert
			after.ShouldBe(expected.Skip(i + 1).Select(r => r.Id), $"after row {expected[i].Id}");
		}
	}

	/// <summary>
	/// The spec's three-key example: after (T0 + 1h, 0.9, id 4) under (created asc, score desc, id asc) come the rows with
	/// a later timestamp (6, 7), the same timestamp and a lower score (3), and the same timestamp and score with a higher
	/// id (8).
	/// </summary>
	[Fact]
	public void BuildSeek_ThreeKeyChain_IsTheExpandedForm()
	{
		// Arrange
		var order = Order<Row>.By(r => r.CreatedAt).ThenByDescending(r => r.Score).ThenBy(r => r.Id);

		// Act
		var seek = SeekExpressionBuilder.BuildSeek(order, new PagePosition([T0.AddHours(1), (double?)0.9, 4])).Compile();

		// Assert
		// created > T, or created = T and score < S (nulls last), or created = T and score = S and id > I
		Rows.Where(seek).Select(r => r.Id).Order().ShouldBe([3, 6, 7, 8]);
	}

	/// <summary>
	/// After a null bookmark on a nulls-last key, only the remaining null rows follow, ordered by the next key. No
	/// non-null row may follow a null one, or it would be served twice.
	/// </summary>
	[Fact]
	public void BuildSeek_NullBookmarkUnderNullsLast_SeeksOnlyAmongNulls()
	{
		// Arrange
		var order = Order<Row>.By(r => r.Score).ThenBy(r => r.Id);

		// Act
		var seek = SeekExpressionBuilder.BuildSeek(order, new PagePosition([null, 2])).Compile();

		// Assert
		// Rows 2, 5 and 7 have no score; after row 2 come 5 and 7.
		Rows.Where(seek).Select(r => r.Id).ShouldBe([5, 7]);
	}

	/// <summary>
	/// Bookmark values appear in the expression as a property of a boxed constant, never as literal constants. A
	/// database provider then treats them as parameters and reuses one query plan for every bookmark.
	/// </summary>
	[Fact]
	public void BuildSeek_EmitsBookmarkValuesAsBoxedProperties_NotConstants()
	{
		// Arrange
		var order = Order<Row>.By(r => r.CreatedAt).ThenByDescending(r => r.Score).ThenBy(r => r.Name).ThenBy(r => r.Key).ThenBy(r => r.Id);
		var seek = SeekExpressionBuilder.BuildSeek(order, new PagePosition([T0, (double?)0.5, "ada", Rows[0].Key, 7]));
		var constants = new ConstantCollector();

		// Act
		constants.Visit(seek);

		// Assert
		constants.Values.ShouldNotBeEmpty();
		// Allowed: null checks, the boxes themselves, and the 0 a CompareTo result is compared with.
		constants.Values.ShouldAllBe(value => value == null || value.GetType().Name.StartsWith("Box") || Equals(value, 0));
	}

	/// <summary>
	/// A timestamp in the position is truncated to the store's precision (17 ticks become 10, one microsecond). A
	/// store that keeps microseconds holds the truncated value, so an untruncated bookmark would sort past its own row.
	/// </summary>
	[Fact]
	public void PositionOf_TruncatesDateTimeToMicroseconds()
	{
		// Arrange
		var order = Order<Row>.By(r => r.CreatedAt).ThenBy(r => r.Id);

		// Act
		var position = SeekExpressionBuilder.PositionOf(order, Rows[0] with { CreatedAt = T0.AddTicks(17) }, TimeSpan.FromMicroseconds(1));

		// Assert
		position.Values[0].ShouldBe(T0.AddTicks(10));
	}

	/// <summary>
	/// An unspecified-kind <see cref="DateTime"/> is marked UTC without changing its ticks, so it round-trips through
	/// the token as the same instant instead of being shifted by the server's time zone.
	/// </summary>
	[Fact]
	public void PositionOf_MarksAnUnspecifiedDateTimeAsUtcWithoutShiftingIt()
	{
		// Arrange
		var order = Order<Row>.By(r => r.CreatedAt).ThenBy(r => r.Id);
		var unspecified = DateTime.SpecifyKind(T0.AddTicks(5), DateTimeKind.Unspecified);

		// Act
		var value = (DateTime)SeekExpressionBuilder.PositionOf(order, Rows[0] with { CreatedAt = unspecified }, TimeSpan.FromMicroseconds(1)).Values[0]!;

		// Assert
		value.Kind.ShouldBe(DateTimeKind.Utc);
		// 5 ticks is below a microsecond, so truncation brings it back to T0.
		value.Ticks.ShouldBe(T0.Ticks);
	}

	/// <summary>A <see cref="DateTimeOffset"/> is converted to UTC (same instant, zero offset) and truncated to microseconds.</summary>
	[Fact]
	public void PositionOf_NormalisesADateTimeOffsetToUtcAndTruncates()
	{
		// Arrange
		var order = Order<DateTimeOffset>.By(d => d);
		// 02:00 at +02:00 is midnight UTC, plus 9 ticks below a microsecond.
		var value = new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.FromHours(2)).AddTicks(9);

		// Act
		var position = SeekExpressionBuilder.PositionOf(order, value, TimeSpan.FromMicroseconds(1));

		// Assert
		position.Values[0].ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
		((DateTimeOffset)position.Values[0]!).Offset.ShouldBe(TimeSpan.Zero);
	}

	/// <summary>
	/// A position read back from its JSON has each value in the key's own type (DateTime, null, Guid, int), with the
	/// timestamp marked UTC, ready for the seek.
	/// </summary>
	[Fact]
	public void ParsePosition_ReadsTheKeyTypesBack()
	{
		// Arrange
		var order = Order<Row>.By(r => r.CreatedAt).ThenBy(r => r.Score).ThenBy(r => r.Key).ThenBy(r => r.Id);

		// Act
		var position = SeekExpressionBuilder.ParsePosition(order, Payload(T0, null, Rows[0].Key, 3));

		// Assert
		position.Values.ShouldBe([T0, null, Rows[0].Key, 3]);
		((DateTime)position.Values[0]!).Kind.ShouldBe(DateTimeKind.Utc);
	}

	/// <summary>
	/// A position with fewer values than the order has keys (the order gained a key after the token was issued) is
	/// refused as an invalid token rather than seeking with a missing value.
	/// </summary>
	[Fact]
	public void ParsePosition_WhenThePayloadIsShort_Refuses()
	{
		// Arrange
		var order = Order<Row>.By(r => r.CreatedAt).ThenBy(r => r.Id);

		// Act & Assert
		Should.Throw<InvalidPageTokenException>(() => SeekExpressionBuilder.ParsePosition(order, Payload(T0)));
	}

	/// <summary>
	/// A value that cannot be read as its key's type (text where an int or a timestamp is expected) is refused as an
	/// invalid token, not raised as an internal error.
	/// </summary>
	[Fact]
	public void ParsePosition_WhenAValueHasTheWrongType_Refuses()
	{
		// Arrange
		var order = Order<Row>.By(r => r.CreatedAt).ThenBy(r => r.Id);

		// Act & Assert
		Should.Throw<InvalidPageTokenException>(() => SeekExpressionBuilder.ParsePosition(order, Payload(T0, "not an int")));
		Should.Throw<InvalidPageTokenException>(() => SeekExpressionBuilder.ParsePosition(order, Payload("yesterday", 1)));
	}

	/// <summary>Serialises the values to JSON elements, the form a position takes inside a token.</summary>
	private static IReadOnlyList<JsonElement> Payload(params object?[] values) =>
		JsonSerializer.SerializeToElement(values).EnumerateArray().ToList();

	/// <summary>Collects the value of every constant node in an expression tree.</summary>
	private sealed class ConstantCollector : ExpressionVisitor
	{
		public List<object?> Values { get; } = [];

		protected override Expression VisitConstant(ConstantExpression node)
		{
			Values.Add(node.Value);
			return node;
		}
	}
}
