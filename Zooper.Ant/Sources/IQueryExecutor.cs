using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Zooper.Ant.Sources;

/// <summary>
/// How a LINQ provider runs a query: to a list and to a count. This is the only thing a LINQ store adapter supplies;
/// ordering, seeking, tokens and the page kinds are all built for it.
/// </summary>
/// <remarks>
/// <para>
/// Why only two methods. <see cref="QueryablePageSource{T}"/> builds every query a page needs (the ordered range,
/// the seek after a bookmark, the count) as a plain <see cref="IQueryable{T}"/> expression through
/// <see cref="SeekExpressionBuilder"/>. What differs between providers is only how such a query is executed
/// asynchronously: EF Core has <c>ToListAsync</c>/<c>CountAsync</c> in <c>Microsoft.EntityFrameworkCore</c>, Marten
/// has its own, LINQ-to-objects runs synchronously. Keeping the seam this small means a provider gets every page kind
/// and every ordering guarantee without reimplementing the seek (design decision 5, spec <c>page-sources</c>, "LINQ-
/// queryable stores need only an executor").
/// </para>
/// <para>
/// Who sees it. Adapter packages implement it and hide it behind their own <c>ToPageAsync</c> overload, so the
/// consumer selects a store by importing its namespace and never names the executor. Implement it yourself only to
/// page a LINQ provider Zooper.Ant does not ship an adapter for.
/// </para>
/// <para>Both methods must run the query exactly as given; the query already carries the filter, the order, any
/// <c>Skip</c>/<c>Take</c> and the seek <c>Where</c>.</para>
/// </remarks>
/// <example>
/// An executor for a provider with EF Core-style async extension methods:
/// <code>
/// public sealed class EfCoreQueryExecutor : IQueryExecutor
/// {
///     public async Task&lt;IReadOnlyList&lt;T&gt;&gt; ToListAsync&lt;T&gt;(IQueryable&lt;T&gt; query, CancellationToken ct) =&gt;
///         await EntityFrameworkQueryableExtensions.ToListAsync(query, ct);
///
///     public Task&lt;int&gt; CountAsync&lt;T&gt;(IQueryable&lt;T&gt; query, CancellationToken ct) =&gt;
///         EntityFrameworkQueryableExtensions.CountAsync(query, ct);
/// }
///
/// var source = new QueryablePageSource&lt;Player&gt;(db.Players.Where(p =&gt; p.Team == team), order, new EfCoreQueryExecutor());
/// var page = await pager.ReadAsync(source, paging, scope, ct);
/// </code>
/// </example>
/// <seealso cref="QueryablePageSource{T}"/>
/// <seealso cref="InMemoryQueryExecutor"/>
public interface IQueryExecutor
{
	/// <summary>Runs the query and returns its rows.</summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="query">The complete query, already filtered, ordered and limited.</param>
	/// <param name="ct">Cancels the query.</param>
	/// <returns>The rows, in the query's order.</returns>
	Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct);

	/// <summary>Counts the query's rows.</summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="query">The filtered query, without ordering or limits.</param>
	/// <param name="ct">Cancels the query.</param>
	/// <returns>The number of rows. Only the counted page kind asks for it.</returns>
	Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct);
}

/// <summary>Runs queries synchronously over LINQ-to-objects: the executor of the in-memory source.</summary>
/// <remarks>
/// <para>Used by <see cref="QueryableExtensions.ToPageAsync{T}(IQueryable{T}, Paging, Order{T}, PageScope, Pager, CancellationToken)"/>.
/// The queries it runs are the same expression trees a database provider receives, so the in-memory source is the
/// reference every LINQ adapter is measured against (spec <c>page-sources</c>, "The in-memory source is the reference
/// implementation").</para>
/// <para>Each call enumerates the query at once and returns an already completed task. Cancellation is checked once,
/// before the query runs; an in-memory enumeration is not interrupted midway.</para>
/// </remarks>
public sealed class InMemoryQueryExecutor : IQueryExecutor
{
	/// <summary>The shared instance; the executor holds no state, so one serves every read.</summary>
	public static InMemoryQueryExecutor Instance { get; } = new();

	/// <inheritdoc />
	/// <exception cref="System.OperationCanceledException"><paramref name="ct"/> was cancelled before the query ran.</exception>
	public Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		return Task.FromResult<IReadOnlyList<T>>(query.ToList());
	}

	/// <inheritdoc />
	/// <exception cref="System.OperationCanceledException"><paramref name="ct"/> was cancelled before the query ran.</exception>
	public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		return Task.FromResult(query.Count());
	}
}
