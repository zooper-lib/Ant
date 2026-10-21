using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Zooper.Ant.Sources;

/// <summary>
/// Pages items held in memory with one call at the end of a LINQ query, through the same order-and-seek builder a
/// database provider receives.
/// </summary>
/// <remarks>
/// <para>
/// Role. This is the in-memory entry point and the shape every store adapter follows: a query-first terminal
/// method, like <c>ToListAsync</c>, so paging never breaks a <c>Where</c> chain (design decision 3). Each call builds a
/// <see cref="QueryablePageSource{T}"/> over <see cref="InMemoryQueryExecutor"/> and hands it to
/// <see cref="Pager.ReadAsync{TItem}"/>, which serves the kind the <see cref="Paging"/> asks for.
/// </para>
/// <para>
/// Adapters. Store adapter packages add their own <c>ToPageAsync</c> overloads, bound to their executors and
/// declared in their own namespaces. The consumer picks the store by the namespace it imports; the call site looks the
/// same.
/// </para>
/// <para>
/// Timestamps. Memory keeps every tick, so the source is created with a precision of one tick: there the stored
/// value is the in-memory value. Truncating to microseconds, as a database source does, would make a row with
/// sub-microsecond ticks sort after its own bookmark and be served twice.
/// </para>
/// <para>Use it for code-owned catalogues, cached lists and tests. Data is read when the call runs: a later page sees
/// the items as they are then.</para>
/// </remarks>
/// <example>
/// <code>
/// var page = await players
///     .Where(p =&gt; team == null || p.Team == team)
///     .ToPageAsync(
///         Paging.Continuable(token, size, pager.Limits),
///         Order&lt;Player&gt;.ByDescending(p =&gt; p.Rating).ThenBy(p =&gt; p.Id),
///         PageScope.For("ListPlayers", team),
///         pager,
///         ct);
/// </code>
/// </example>
/// <seealso cref="QueryablePageSource{T}"/>
/// <seealso cref="Pager"/>
public static class QueryableExtensions
{
	/// <summary>Orders the query by <paramref name="order"/> and serves the page <paramref name="paging"/> asks for.</summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="query">The filtered in-memory query, unordered; any ordering on it is replaced by the chain.</param>
	/// <param name="paging">The caller's paging instruction: continuable, counted or capped.</param>
	/// <param name="order">The chain, ending in a unique tie-breaker.</param>
	/// <param name="scope">The read's name and its result-defining arguments; a token is accepted only under the same scope.</param>
	/// <param name="pager">The injected <see cref="Pager"/>, which owns the tokens and the page kinds.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <returns>
	/// A <see cref="ContinuablePage{TItem}"/>, <see cref="CountedPage{TItem}"/> or <see cref="CappedPage{TItem}"/>,
	/// matching <paramref name="paging"/>.
	/// </returns>
	/// <exception cref="InvalidPageTokenException">The token was issued by another read, for other arguments or another
	/// order, or was altered.</exception>
	/// <exception cref="PagingException">Any other paging refusal.</exception>
	public static Task<Page<T>> ToPageAsync<T>(
		this IQueryable<T> query,
		Paging paging,
		Order<T> order,
		PageScope scope,
		Pager pager,
		CancellationToken ct = default) =>
		// In memory keeps every tick, so no truncation: the stored precision is the in-memory precision.
		pager.ReadAsync(new QueryablePageSource<T>(query, order, InMemoryQueryExecutor.Instance, TimeSpan.FromTicks(1)), paging, scope, ct);

	/// <summary>Pages any sequence held in memory, such as a list or an array.</summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="items">The filtered items, in any order.</param>
	/// <param name="paging">The caller's paging instruction: continuable, counted or capped.</param>
	/// <param name="order">The chain, ending in a unique tie-breaker.</param>
	/// <param name="scope">The read's name and its result-defining arguments.</param>
	/// <param name="pager">The injected <see cref="Pager"/>.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <returns>The page of the requested kind.</returns>
	/// <exception cref="InvalidPageTokenException">The token was issued by another read, for other arguments or another
	/// order, or was altered.</exception>
	/// <exception cref="PagingException">Any other paging refusal.</exception>
	/// <remarks>Wraps <paramref name="items"/> with <see cref="Queryable.AsQueryable{TElement}(IEnumerable{TElement})"/>,
	/// so it runs the same expressions as the <see cref="IQueryable{T}"/> overload. The sequence is enumerated on every
	/// call.</remarks>
	public static Task<Page<T>> ToPageAsync<T>(
		this IEnumerable<T> items,
		Paging paging,
		Order<T> order,
		PageScope scope,
		Pager pager,
		CancellationToken ct = default) =>
		items.AsQueryable().ToPageAsync(paging, order, scope, pager, ct);
}
