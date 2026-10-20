using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Zooper.Ant.Sources;

/// <summary>
/// The order of a LINQ read, declared once as a chain of typed keys ending in a unique tie-breaker. Both the ORDER BY
/// and the continuation seek are generated from it, so they cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Role.</b> An order is the single statement of how a read is sorted. <see cref="QueryablePageSource{T}"/> hands
/// it to <see cref="SeekExpressionBuilder"/>, which derives two things from the same keys: the ORDER BY applied to
/// every page (<see cref="SeekExpressionBuilder.ApplyOrdering{T}"/>) and the "rows strictly after the last served row"
/// predicate used to continue (<see cref="SeekExpressionBuilder.BuildSeek{T}"/>). Because neither is written by hand,
/// the page order and the continuation can never drift apart (spec <c>keyset-ordering</c>, "An order is declared once
/// as a chain ending in a unique tie-breaker").
/// </para>
/// <para>
/// <b>The tie-breaker.</b> The last key must be unique across the result set, typically the identifier. Without it,
/// rows that share every other key value have no fixed order, and a page boundary falling among them would skip or
/// repeat rows. The order cannot verify uniqueness; declaring it is the caller's responsibility.
/// </para>
/// <para>
/// <b>Keys.</b> Each key is a typed lambda over the item, a member access (<c>p =&gt; p.Rating</c>) or a computed
/// expression the LINQ provider can translate. Its name and value type are inferred from the lambda, so renaming a
/// property is a compile-time change and no string names exist. Directions may be mixed freely; each key seeks in its
/// own direction.
/// </para>
/// <para>
/// <b>Nulls.</b> A nullable key places nulls last in both directions unless declared with <c>nullsFirst: true</c>. A key
/// counts as nullable when its type is <see cref="Nullable{T}"/> (<c>int?</c>, <c>DateTime?</c>) or when it is a
/// direct access to a reference-type property or field annotated as nullable (<c>string?</c> under nullable reference
/// types). A computed reference-type key, or one without the annotation, is treated as never null; see
/// <see cref="OrderKey{T}.IsNullable"/>.
/// </para>
/// <para>
/// <b>Identity in tokens.</b> <see cref="OrderSignature"/> describes the chain. <see cref="Pager"/> folds it into the
/// token binding through <see cref="PageScope.Bind"/>, so a token issued under one order is refused under another
/// (spec <c>continuation-tokens</c>, "Changed ordering").
/// </para>
/// <para>An order is immutable: <see cref="ThenBy{TKey}"/> and the other builders return a new chain. A chain can
/// therefore be stored in a static field and reused by every request.</para>
/// </remarks>
/// <example>
/// Best rating first, unrated players last, ties broken by name and then by the unique identifier:
/// <code>
/// var byRating = Order&lt;Player&gt;.ByDescending(p =&gt; p.Rating)   // int?: nulls last by default
///     .ThenBy(p =&gt; p.Name)
///     .ThenBy(p =&gt; p.Id);                                     // unique tie-breaker, always last
///
/// // The same chain read backwards: unrated first, then lowest rating, names Z to A, highest Id first.
/// var worstFirst = byRating.Reverse();
///
/// // Nulls first instead of last for one key.
/// var unratedFirst = Order&lt;Player&gt;.By(p =&gt; p.Rating, nullsFirst: true).ThenBy(p =&gt; p.Id);
/// </code>
/// </example>
/// <typeparam name="T">The item type the keys read from.</typeparam>
/// <seealso cref="OrderKey{T}"/>
/// <seealso cref="QueryablePageSource{T}"/>
/// <seealso cref="QueryableExtensions"/>
public sealed class Order<T>
{
	// Private: a chain always starts from By or ByDescending, so it is never empty.
	private Order(IReadOnlyList<OrderKey<T>> keys) => Keys = keys;

	/// <summary>The keys in chain order; the last one is the tie-breaker.</summary>
	/// <remarks>Read by <see cref="SeekExpressionBuilder"/> to build the ordering and the seek, and by store adapters that
	/// translate the chain themselves.</remarks>
	public IReadOnlyList<OrderKey<T>> Keys { get; }

	/// <summary>
	/// A stable description of the chain: each key's expression, value type, direction and nulls placement, joined with
	/// commas. It changes whenever the order changes, which refuses tokens issued under the earlier order.
	/// </summary>
	/// <remarks>
	/// <para>Example for <c>Order&lt;Player&gt;.ByDescending(p =&gt; p.Rating).ThenBy(p =&gt; p.Id)</c>:
	/// <c>_.Rating:System.Nullable`1[[System.Int32, …]] desc nulls-last, _.Id:System.Int32 asc</c>.</para>
	/// <para>It is built from expression text, so it is the same in every process that runs the same code, and it does
	/// not depend on the lambda's parameter name (see <see cref="OrderKey{T}.Signature"/>). It is exposed through
	/// <see cref="QueryablePageSource{T}.OrderSignature"/>, which <see cref="Pager"/> binds into every token.</para>
	/// </remarks>
	public string OrderSignature => string.Join(", ", Keys.Select(key => key.Signature));

	/// <summary>Starts a chain with an ascending key.</summary>
	/// <typeparam name="TKey">The key's value type, inferred from the lambda.</typeparam>
	/// <param name="key">The key, as a lambda the LINQ provider can translate, such as <c>p =&gt; p.CreatedAt</c>.</param>
	/// <param name="nullsFirst">Place nulls before every value instead of after. Only matters for a nullable key.</param>
	/// <returns>A new chain holding this one key.</returns>
	public static Order<T> By<TKey>(Expression<Func<T, TKey>> key, bool nullsFirst = false) =>
		new([new OrderKey<T>(key, false, nullsFirst)]);

	/// <summary>Starts a chain with a descending key.</summary>
	/// <typeparam name="TKey">The key's value type, inferred from the lambda.</typeparam>
	/// <param name="key">The key, as a lambda the LINQ provider can translate, such as <c>p =&gt; p.Rating</c>.</param>
	/// <param name="nullsFirst">Place nulls before every value instead of after. Only matters for a nullable key.</param>
	/// <returns>A new chain holding this one key.</returns>
	public static Order<T> ByDescending<TKey>(Expression<Func<T, TKey>> key, bool nullsFirst = false) =>
		new([new OrderKey<T>(key, true, nullsFirst)]);

	/// <summary>Adds an ascending key after the existing ones.</summary>
	/// <typeparam name="TKey">The key's value type, inferred from the lambda.</typeparam>
	/// <param name="key">The key, as a lambda the LINQ provider can translate.</param>
	/// <param name="nullsFirst">Place nulls before every value instead of after. Only matters for a nullable key.</param>
	/// <returns>A new chain; this one is unchanged.</returns>
	/// <remarks>The key is compared only between rows whose earlier keys are all equal. End the chain with a unique
	/// key.</remarks>
	public Order<T> ThenBy<TKey>(Expression<Func<T, TKey>> key, bool nullsFirst = false) =>
		new([.. Keys, new OrderKey<T>(key, false, nullsFirst)]);

	/// <summary>Adds a descending key after the existing ones.</summary>
	/// <typeparam name="TKey">The key's value type, inferred from the lambda.</typeparam>
	/// <param name="key">The key, as a lambda the LINQ provider can translate.</param>
	/// <param name="nullsFirst">Place nulls before every value instead of after. Only matters for a nullable key.</param>
	/// <returns>A new chain; this one is unchanged.</returns>
	public Order<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> key, bool nullsFirst = false) =>
		new([.. Keys, new OrderKey<T>(key, true, nullsFirst)]);

	/// <summary>The same chain reversed as a whole: every direction and every nulls placement flipped.</summary>
	/// <returns>A new chain that yields the rows of this one in exactly the opposite order.</returns>
	/// <remarks>
	/// Flipping only the directions would not be a true reversal: with nulls last, an ascending key puts nulls at the
	/// end, so the reversed order must put them at the start. Flipping both keeps the seek consistent with the
	/// reversed order (spec <c>keyset-ordering</c>, "An ordering can be reversed without a second declaration"). The
	/// reversed chain has another <see cref="OrderSignature"/>, so tokens do not carry over between the two.
	/// </remarks>
	public Order<T> Reverse() =>
		new(Keys.Select(key => key.Reverse()).ToList());
}

/// <summary>One key of an <see cref="Order{T}"/>: the lambda, its direction, its nulls placement and whether it can be
/// null.</summary>
/// <remarks>
/// <para>Created only through <see cref="Order{T}"/>'s builders. Store adapters that translate a chain themselves read
/// <see cref="Key"/>, <see cref="Descending"/>, <see cref="NullsFirst"/> and <see cref="IsNullable"/>;
/// <see cref="SeekExpressionBuilder"/> does the same for every LINQ provider.</para>
/// <para><see cref="GetValue"/> reads the key from an item in memory, which is how
/// <see cref="SeekExpressionBuilder.PositionOf{T}"/> takes the bookmark of the last served row. The lambda is compiled
/// once, on first use, and the compiled delegate is reused for every later call.</para>
/// </remarks>
/// <typeparam name="T">The item type the key reads from.</typeparam>
public sealed class OrderKey<T>
{
	// One context for the whole process: NullabilityInfoContext caches what it has read, and the annotations it reads
	// never change at run time.
	private static readonly NullabilityInfoContext NullabilityContext = new();

	// Compiled on first GetValue rather than in the constructor, so declaring an order costs nothing until a position
	// is actually taken. Lazy<T>'s default mode is thread-safe, so concurrent requests share one compiled delegate.
	private readonly Lazy<Func<T, object?>> _getter;

	/// <summary>Creates a key, inferring whether it can be null from the lambda.</summary>
	/// <param name="key">The key lambda over <typeparamref name="T"/>.</param>
	/// <param name="descending">Whether the key sorts descending.</param>
	/// <param name="nullsFirst">Whether nulls sort before every value.</param>
	internal OrderKey(LambdaExpression key, bool descending, bool nullsFirst)
		: this(key, descending, nullsFirst, IsNullableKey(key)) { }

	// Used by Reverse to carry IsNullable over without inspecting the lambda again.
	private OrderKey(LambdaExpression key, bool descending, bool nullsFirst, bool isNullable)
	{
		(Key, Descending, NullsFirst, IsNullable) = (key, descending, nullsFirst, isNullable);

		// key.Body boxed to object, so one delegate type serves keys of every value type.
		_getter = new Lazy<Func<T, object?>>(() =>
			Expression.Lambda<Func<T, object?>>(Expression.Convert(key.Body, typeof(object)), key.Parameters).Compile());
	}

	/// <summary>The key as a lambda over the item, exactly as declared.</summary>
	/// <remarks>Kept as an expression rather than a delegate so a LINQ provider can translate it into the query.</remarks>
	public LambdaExpression Key { get; }

	/// <summary>The key's value type, such as <c>int?</c> or <c>string</c>.</summary>
	/// <remarks>Used to read a position back from a token (<see cref="SeekExpressionBuilder.ParsePosition{T}"/>) and to
	/// type the bookmark values in the seek predicate.</remarks>
	public Type KeyType => Key.ReturnType;

	/// <summary>Whether the key sorts descending. For a descending key, "after the bookmark" means a smaller value.</summary>
	public bool Descending { get; }

	/// <summary>Whether nulls sort before every value; otherwise they sort after every value.</summary>
	/// <remarks>Independent of <see cref="Descending"/>: nulls stay last (or first) in both directions. Has no effect
	/// when <see cref="IsNullable"/> is false.</remarks>
	public bool NullsFirst { get; }

	/// <summary>Whether the key can be null, so its nulls placement applies.</summary>
	/// <remarks>
	/// True when the key's type is <see cref="Nullable{T}"/>, or when the key is a direct access to a reference-type
	/// property or field annotated as nullable (<c>string?</c>). Any other reference-type key, including a computed
	/// one, is treated as never null. That keeps the null-rank ordering key, and the extra ORDER BY term it costs, off
	/// keys that cannot be null.
	/// </remarks>
	public bool IsNullable { get; }

	/// <summary>This key's part of <see cref="Order{T}.OrderSignature"/>: <c>expression:Type direction [nulls-placement]</c>.</summary>
	/// <remarks>The lambda's parameter name is rewritten to <c>_</c> (<c>p.Rating</c> and <c>player.Rating</c> both give
	/// <c>_.Rating</c>). Without that, renaming a lambda parameter would change the signature and refuse every token in
	/// flight although the order is the same. The nulls placement appears only for nullable keys, where it changes the
	/// order.</remarks>
	internal string Signature =>
		$"{Key.Body.ToString().Replace(Key.Parameters[0].Name + ".", "_.")}:{KeyType.FullName} {(Descending ? "desc" : "asc")}{(IsNullable ? NullsFirst ? " nulls-first" : " nulls-last" : "")}";

	/// <summary>Reads the key of an item in memory.</summary>
	/// <param name="item">The item, typically the last row of a page.</param>
	/// <returns>The key value, boxed; <see langword="null"/> when the key is null for this item.</returns>
	/// <remarks>The lambda is compiled on the first call and reused afterwards.</remarks>
	public object? GetValue(T item) => _getter.Value(item);

	/// <summary>The same key with its direction and nulls placement both flipped.</summary>
	/// <returns>A new key over the same lambda.</returns>
	internal OrderKey<T> Reverse() => new(Key, !Descending, !NullsFirst, IsNullable);

	// Nullable<T> is always nullable. A reference type is nullable only when the key is a direct member access and that
	// member carries a nullable annotation: NullabilityInfoContext reads the compiler's [Nullable] metadata. A method
	// call or other computed expression has no member to inspect, so it counts as non-null.
	private static bool IsNullableKey(LambdaExpression key) =>
		Nullable.GetUnderlyingType(key.ReturnType) is not null
		|| (!key.ReturnType.IsValueType && key.Body is MemberExpression { Member: var member } && member switch
		{
			PropertyInfo property => NullabilityContext.Create(property).ReadState == NullabilityState.Nullable,
			FieldInfo field => NullabilityContext.Create(field).ReadState == NullabilityState.Nullable,
			_ => false
		});
}
