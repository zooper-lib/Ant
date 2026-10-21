using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;

namespace Zooper.Ant.Sources;

/// <summary>
/// Turns an <see cref="Order{T}"/> into LINQ expressions: the ORDER BY of a page and the keyset seek predicate that
/// continues after a bookmark. Also takes a bookmark from a row (<see cref="PositionOf{T}"/>) and reads one back from a
/// token (<see cref="ParsePosition{T}"/>).
/// </summary>
/// <remarks>
/// <para>
/// Role. This is the engine behind <see cref="QueryablePageSource{T}"/> and therefore behind every LINQ store:
/// in-memory today, EF Core and Marten through their adapters later. The in-memory source runs exactly the same
/// expressions through LINQ-to-objects, so the package's in-memory tests exercise the expressions a database provider
/// will receive (design decision 8, spec <c>page-sources</c>, "The in-memory source is the reference
/// implementation").
/// </para>
/// <para>
/// Ordering. <see cref="ApplyOrdering{T}"/> emits one <c>OrderBy</c>/<c>ThenBy</c> per key, in chain order.
/// Before every nullable key it emits a null-rank key, <c>x =&gt; x.Key == null ? 1 : 0</c> (or <c>0 : 1</c>
/// for nulls first), sorted ascending. LINQ has no "NULLS LAST" operator and databases disagree on where nulls sort by
/// default (PostgreSQL puts them last ascending, SQL Server first), so ranking them explicitly is the portable way to
/// place them. It translates in LINQ-to-objects and EF Core, and it is the idiom EF Core and Marten users are pointed
/// to. Because the rank is always ascending, nulls stay last (or first) whatever the key's own direction.
/// </para>
/// <para>
/// Seek. <see cref="BuildSeek{T}"/> emits the expanded keyset predicate. For keys <c>k_0</c>…<c>k_n</c>
/// and bookmark values <c>v_0</c>…<c>v_n</c> a row is after the bookmark when
/// <c>OR over i of ( AND over j &lt; i of k_j == v_j ) AND beyond(k_i, v_i)</c>: its first key is beyond the
/// bookmark's, or the first is equal and the second is beyond, and so on through the tie-breaker. "Beyond" honours
/// each key's direction (<c>&gt;</c> ascending, <c>&lt;</c> descending) and its nulls placement. The expanded form is
/// used instead of a row-value comparison <c>(a, b) &gt; (x, y)</c> because row values cannot express mixed directions
/// or null placement, and LINQ cannot express them at all.
/// </para>
/// <para>
/// Translatable shapes only. Comparisons use the type's operators where they exist (numbers,
/// <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="TimeSpan"/>, enums through their underlying type),
/// <see cref="string.Compare(string, string)"/> for strings, and <see cref="IComparable{T}.CompareTo"/> for the rest
/// (<see cref="Guid"/>, <see cref="bool"/>). EF Core translates all of these for the types it maps. A general
/// <see cref="Comparer{T}.Default"/> call was rejected because EF Core does not translate it (design decision 8).
/// </para>
/// <para>
/// Parameters, not literals. Each bookmark value is emitted as <c>box.Value</c>, a property read on a constant
/// <c>Box&lt;TKey&gt;</c>, instead of an <see cref="Expression.Constant(object)"/> of the value itself. LINQ providers
/// treat a member access on a closure-like constant as a query parameter, the same way they treat a captured local in
/// <c>Where(x =&gt; x.Id &gt; lastId)</c>. EF Core therefore compiles one SQL plan per order shape and reuses it for
/// every bookmark, instead of one plan per distinct bookmark. Correctness does not depend on it: a provider that
/// inlines the value anyway still gives the right rows, only with a less effective plan cache.
/// </para>
/// <para>
/// Positions. A position is the list of the chain's key values for one row, tie-breaker last. It travels inside
/// the encrypted token as JSON, so <see cref="PositionOf{T}"/> normalises timestamps before they leave, and
/// <see cref="ParsePosition{T}"/> checks the shape when they return. Both are used by
/// <see cref="QueryablePageSource{T}"/>, never called by <see cref="Pager"/> directly.
/// </para>
/// </remarks>
/// <seealso cref="Order{T}"/>
/// <seealso cref="QueryablePageSource{T}"/>
internal static class SeekExpressionBuilder
{
	/// <summary>Orders the query by the chain, ranking nulls first or last on every nullable key.</summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="query">The filtered, unordered query. Any ordering it already has is replaced by the first key.</param>
	/// <param name="order">The chain to apply.</param>
	/// <returns>The query ordered by every key of the chain, in chain order.</returns>
	/// <remarks>
	/// For <c>ByDescending(p =&gt; p.Rating).ThenBy(p =&gt; p.Id)</c> with a nullable <c>Rating</c> this produces
	/// <c>query.OrderBy(p =&gt; p.Rating == null ? 1 : 0).ThenByDescending(p =&gt; p.Rating).ThenBy(p =&gt; p.Id)</c>.
	/// The calls are built as <see cref="Queryable"/> method-call expressions, so the provider sees exactly what a
	/// hand-written <c>OrderBy</c> chain would give it.
	/// </remarks>
	public static IOrderedQueryable<T> ApplyOrdering<T>(IQueryable<T> query, Order<T> order)
	{
		var expression = query.Expression;

		// The first ordering call must be OrderBy/OrderByDescending; every later one ThenBy/ThenByDescending. The null
		// rank, when present, counts as a call too, so it can be the first.
		var first = true;

		foreach (var key in order.Keys)
		{
			if (key.IsNullable)
			{
				// Null rank: nulls get 1 and sort after the 0s (nulls last), or 0 and sort before the 1s (nulls first).
				// Sorted ascending regardless of the key's direction, so the placement does not flip with it.
				var isNull = Expression.Equal(key.Key.Body, Expression.Constant(null, key.KeyType));
				var rank = Expression.Lambda(
					Expression.Condition(isNull, Expression.Constant(key.NullsFirst ? 0 : 1), Expression.Constant(key.NullsFirst ? 1 : 0)),
					key.Key.Parameters);

				expression = CallOrdering(expression, first ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy), rank);
				first = false;
			}

			var method = (first, key.Descending) switch
			{
				(true, false) => nameof(Queryable.OrderBy),
				(true, true) => nameof(Queryable.OrderByDescending),
				(false, false) => nameof(Queryable.ThenBy),
				(false, true) => nameof(Queryable.ThenByDescending)
			};

			expression = CallOrdering(expression, method, key.Key);
			first = false;
		}

		return (IOrderedQueryable<T>)query.Provider.CreateQuery<T>(expression);
	}

	/// <summary>
	/// Builds the predicate selecting the rows strictly after <paramref name="position"/> in the order of
	/// <paramref name="order"/>.
	/// </summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="order">The chain the position was taken under.</param>
	/// <param name="position">The bookmark: one value per key, typed as the key (see <see cref="ParsePosition{T}"/>).</param>
	/// <returns>A predicate to pass to <c>Where</c>, before <see cref="ApplyOrdering{T}"/>.</returns>
	/// <remarks>
	/// <para>
	/// Worked example. Chain <c>CreatedAt asc, Score desc (double?, nulls last), Id asc</c>, bookmark
	/// <c>(T, S, I)</c> with a non-null <c>S</c>:
	/// </para>
	/// <code>
	///    x.CreatedAt &gt; T
	/// || (x.CreatedAt == T &amp;&amp; (x.Score &lt; S || x.Score == null))
	/// || (x.CreatedAt == T &amp;&amp; x.Score == S &amp;&amp; x.Id &gt; I)
	/// </code>
	/// <para>
	/// The second line shows a nulls-last key: after a non-null bookmark come the smaller scores (descending) and then
	/// every null. If the bookmark's score is null instead, nothing is beyond it on that key, so the second line
	/// disappears and the third compares <c>x.Score == null</c>: only rows whose score is also null can follow (spec
	/// <c>keyset-ordering</c>, "Null bookmark"). Under nulls first the roles swap: after a null bookmark come all
	/// non-null values (<c>x.Score != null</c>), and after a non-null one only the plain comparison applies, because
	/// the nulls were already served.
	/// </para>
	/// <para>When no key can be beyond (every bookmark value null on nulls-last keys), the predicate is
	/// <c>false</c>.</para>
	/// </remarks>
	public static Expression<Func<T, bool>> BuildSeek<T>(Order<T> order, PagePosition position)
	{
		// Each key lambda has its own parameter (p, x, player, …). They are rewritten onto one shared parameter so the
		// combined predicate is a single lambda.
		var parameter = Expression.Parameter(typeof(T), "x");

		// predicate:    the OR over the keys processed so far.
		// earlierEqual: the AND of "key j equals its bookmark" for every key j before the current one.
		Expression? predicate = null;
		Expression? earlierEqual = null;

		for (var i = 0; i < order.Keys.Count; i++)
		{
			var key = order.Keys[i];
			var body = new ParameterReplacer(key.Key.Parameters[0], parameter).Visit(key.Key.Body);
			var value = position.Values[i];

			// Term i: every earlier key equal AND this key beyond. Skipped when nothing can be beyond this key
			// (a null bookmark on a nulls-last key).
			if (Beyond(key, body, value) is { } beyond)
				predicate = Or(predicate, And(earlierEqual, beyond));

			// Extend the equality prefix for the next term. Equality with null must be "is null": a lifted == against a
			// null constant is fine in LINQ-to-objects, but spelling it out keeps the intent and the SQL (IS NULL) clear.
			earlierEqual = And(earlierEqual, value is null ? IsNull(body, key.KeyType) : Equal(body, Bookmark(key.KeyType, value)));
		}

		return Expression.Lambda<Func<T, bool>>(predicate ?? Expression.Constant(false), parameter);
	}

	/// <summary>Reads the chain's key values from an item, normalising timestamps, to make the bookmark of a page.</summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="order">The chain.</param>
	/// <param name="item">The last row served on the page.</param>
	/// <param name="precision">
	/// The precision the store keeps for date and time values. <see cref="QueryablePageSource{T}"/> defaults it to one
	/// microsecond; the in-memory entry point passes one tick.
	/// </param>
	/// <returns>One value per key, in chain order, ready to be serialised into the token.</returns>
	/// <remarks>
	/// <para>
	/// Truncation. A <see cref="DateTime"/> in memory has 100-nanosecond ticks, but PostgreSQL and most stores
	/// keep microseconds. A row read back from the store already carries the truncated value; one built in memory and
	/// then saved may not. If the bookmark kept the finer value, the stored row (equal to the truncated value) would
	/// compare as before its own bookmark and the rows sharing that microsecond would be skipped or repeated. The
	/// bookmark is therefore truncated to the store's precision, so it is equal to the stored value (spec
	/// <c>keyset-ordering</c>, "A timestamp bookmark is truncated to the store's precision").
	/// </para>
	/// <para>
	/// UTC. An unspecified-kind <see cref="DateTime"/> is marked UTC with its ticks unchanged, so it serialises
	/// with a <c>Z</c> and reads back as the same instant; values are compared by ticks, so marking moves nothing. A
	/// local-kind value is kept as is. A <see cref="DateTimeOffset"/> is converted to offset zero, which keeps the instant
	/// and gives one canonical form.
	/// </para>
	/// </remarks>
	public static PagePosition PositionOf<T>(Order<T> order, T item, TimeSpan precision) =>
		new(order.Keys.Select(key => Normalize(key.GetValue(item), precision)).ToList());

	/// <summary>Reads a position back from a token's payload, as the chain's key types.</summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="order">The chain the read uses now.</param>
	/// <param name="payload">The position values from the token, as JSON, in chain order.</param>
	/// <returns>The position, each value deserialised to its key's <see cref="OrderKey{T}.KeyType"/>.</returns>
	/// <exception cref="InvalidPageTokenException">
	/// The payload has another number of values than the chain has keys, or a value cannot be read as its key's type.
	/// </exception>
	/// <remarks>
	/// The token binding already refuses most mismatches, because <see cref="Order{T}.OrderSignature"/> changes with the
	/// chain. A position can still fail to fit when the payload was produced elsewhere or altered by whoever holds the
	/// token's keys. That is a refused token, not an internal error (spec
	/// <c>continuation-tokens</c>, "A token whose payload no longer fits the read is refused"), so every read failure
	/// becomes <see cref="InvalidPageTokenException"/> and the caller sees the same refusal as for any foreign token.
	/// An unspecified-kind <see cref="DateTime"/> is marked UTC, matching <see cref="PositionOf{T}"/>.
	/// </remarks>
	public static PagePosition ParsePosition<T>(Order<T> order, IReadOnlyList<JsonElement> payload)
	{
		// A key added to or removed from the chain: the position no longer lines up with the keys.
		if (payload.Count != order.Keys.Count)
			throw new InvalidPageTokenException();

		try
		{
			// The same pinned JSON options the token was written with, so values read back exactly as written.
			return new PagePosition(order.Keys
				.Select((key, i) => payload[i].Deserialize(key.KeyType, CanonicalJson.Options) switch
				{
					DateTime { Kind: DateTimeKind.Unspecified } dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
					var value => value
				})
				.ToList());
		}
		// The failures System.Text.Json raises for a value of the wrong type or shape (a string for an int, null for a
		// non-nullable value type, an unsupported key type).
		catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
		{
			throw new InvalidPageTokenException(exception);
		}
	}

	// See PositionOf: mark unspecified DateTimes UTC (ticks unchanged), move DateTimeOffsets to offset zero, then
	// truncate both to the store's precision. Every other value passes through unchanged.
	private static object? Normalize(object? value, TimeSpan precision) =>
		value switch
		{
			DateTime dateTime => Truncate(dateTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : dateTime, precision),
			DateTimeOffset offset => Truncate(offset.ToUniversalTime(), precision),
			_ => value
		};

	// Drops the ticks below the precision (rounds towards the past). The spec's rule is truncation; a store that rounds
	// on write instead differs only for values written with sub-microsecond ticks.
	private static DateTime Truncate(DateTime value, TimeSpan precision) =>
		value.AddTicks(-(value.Ticks % precision.Ticks));

	private static DateTimeOffset Truncate(DateTimeOffset value, TimeSpan precision) =>
		value.AddTicks(-(value.Ticks % precision.Ticks));

	// "Beyond the bookmark" on one key, or null when no row can be beyond it.
	// Nulls are placed by the null-rank key of ApplyOrdering: after every value (nulls last) or before every value
	// (nulls first). So:
	//   bookmark null,  nulls last  -> nothing is after a null on this key       -> null (the term is dropped)
	//   bookmark null,  nulls first -> every non-null value is after it          -> key != null
	//   bookmark value, nulls last  -> greater (or smaller) values, then nulls   -> compare || key == null
	//   bookmark value, nulls first -> only greater (or smaller) values          -> compare
	private static Expression? Beyond<T>(OrderKey<T> key, Expression body, object? value)
	{
		if (value is null)
			return key.NullsFirst ? Expression.Not(IsNull(body, key.KeyType)) : null;

		var compared = Compare(body, Bookmark(key.KeyType, value), key.Descending, key.IsNullable);

		return key.IsNullable && !key.NullsFirst ? Expression.OrElse(compared, IsNull(body, key.KeyType)) : compared;
	}

	// "key is beyond value" in the key's direction: key > value ascending, key < value descending.
	// First choice is the type's own operator, which is what a hand-written query would use and what every provider
	// translates. Expression.GreaterThan throws InvalidOperationException when the type defines no such operator
	// (string, Guid, bool); then the comparison goes through string.Compare(key, value) or key.CompareTo(value), compared
	// against 0.
	private static Expression Compare(Expression key, Expression value, bool descending, bool nullable)
	{
		var (left, right) = (AsComparable(key), AsComparable(value));

		try
		{
			// For Nullable<T> operands this is the lifted operator: null compares as false, and the caller adds the
			// "|| key == null" arm where nulls sort last.
			return descending ? Expression.LessThan(left, right) : Expression.GreaterThan(left, right);
		}
		catch (InvalidOperationException)
		{
			// No operator (string, Guid, bool): compare through string.Compare or IComparable<T>.CompareTo below.
		}

		// CompareTo is declared on the underlying type, so a Nullable<T> key and value are unwrapped with .Value. The
		// bookmark value is never null here (Beyond handles null bookmarks), only the row's key can be.
		var type = Nullable.GetUnderlyingType(key.Type) ?? key.Type;
		var (k, v) = Nullable.GetUnderlyingType(key.Type) is null ? (key, value) : (Expression.Property(key, "Value"), Expression.Property(value, "Value"));

		// string.Compare is static and null-safe, and providers translate it to the column's own comparison. Every other
		// type uses its CompareTo(T); a key type with neither an operator nor CompareTo(T) cannot be ordered.
		var comparison = type == typeof(string)
			? Expression.Call(typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!, k, v)
			: Expression.Call(k, type.GetMethod(nameof(IComparable<int>.CompareTo), [type])
				?? throw new NotSupportedException($"Order key type {type} has no comparison operator and no CompareTo({type})."), v);
		var beyond = descending
			? Expression.LessThan(comparison, Expression.Constant(0))
			: Expression.GreaterThan(comparison, Expression.Constant(0));

		// Null guard: a null row value must never count as beyond through this arm. In memory, .Value or CompareTo on null
		// would throw, and string.Compare(null, v) returns -1, which a descending key would read as "smaller, so beyond".
		// Where nulls do belong after the bookmark, Beyond adds them through its own "key == null" arm.
		return nullable || !key.Type.IsValueType ? Expression.AndAlso(Expression.Not(IsNull(key, key.Type)), beyond) : beyond;
	}

	// key == value, with enums compared through their underlying type like Compare does.
	private static Expression Equal(Expression key, Expression value) =>
		Expression.Equal(AsComparable(key), AsComparable(value));

	// key == null, typed as the key so it works for Nullable<T> and reference types alike (SQL: IS NULL).
	private static Expression IsNull(Expression key, Type type) =>
		Expression.Equal(key, Expression.Constant(null, type));

	// Expression.GreaterThan and Expression.Equal do not accept enum operands, so enums (and nullable enums) are
	// converted to their underlying integral type first. The order is the same, and providers store enums as those
	// integers anyway. Every other expression is returned unchanged.
	private static Expression AsComparable(Expression expression)
	{
		var underlying = Nullable.GetUnderlyingType(expression.Type);
		var type = underlying ?? expression.Type;

		if (!type.IsEnum)
			return expression;

		var integral = Enum.GetUnderlyingType(type);

		return Expression.Convert(expression, underlying is null ? integral : typeof(Nullable<>).MakeGenericType(integral));
	}

	// A bookmark value as box.Value on a constant Box<TKey>: a parameter to the provider, not a literal (see the class
	// remarks). The box is typed as the key, so the expression's type matches the key's exactly, Nullable<T> included.
	private static Expression Bookmark(Type type, object value) =>
		Expression.Property(Expression.Constant(Activator.CreateInstance(typeof(Box<>).MakeGenericType(type), value)), nameof(Box<int>.Value));

	// AND and OR that treat a missing left side as "nothing yet", so the loops in BuildSeek need no special first case.
	private static Expression And(Expression? left, Expression right) => left is null ? right : Expression.AndAlso(left, right);

	private static Expression Or(Expression? left, Expression right) => left is null ? right : Expression.OrElse(left, right);

	// Queryable.OrderBy / ThenBy / … as an expression call, with the key lambda quoted as Queryable expects.
	private static Expression CallOrdering(Expression source, string method, LambdaExpression key) =>
		Expression.Call(typeof(Queryable), method, [key.Parameters[0].Type, key.ReturnType], source, Expression.Quote(key));

	// Holds one bookmark value. Providers read a property of a constant object as a query parameter, the way they read
	// a captured variable of a closure.
	private sealed class Box<TValue>(TValue value)
	{
		public TValue Value { get; } = value;
	}

	// Replaces one lambda parameter with another throughout an expression, so key bodies declared with their own
	// parameters can be combined into one predicate over a single parameter.
	private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
	{
		protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
	}
}
