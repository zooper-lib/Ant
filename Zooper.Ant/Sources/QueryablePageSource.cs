using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Zooper.Ant.Sources;

/// <summary>
/// The page source of any LINQ provider: orders and seeks a query by an <see cref="Order{T}"/> and runs it through an
/// <see cref="IQueryExecutor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Role.</b> <see cref="Pager"/> serves every page kind from an <see cref="IPageSource{TItem}"/>. This class is that
/// source for everything LINQ can express. It turns each call of the floor contract into one query over the same
/// filtered result set:
/// </para>
/// <list type="table">
/// <listheader><term>Call</term><description>Query run through the executor</description></listheader>
/// <item><term><see cref="CountAsync"/></term><description><c>query</c> counted (counted kind only)</description></item>
/// <item><term><see cref="ReadAsync"/></term><description><c>query</c> ordered by the chain, <c>.Skip(offset).Take(limit)</c></description></item>
/// <item><term><see cref="ReadAfterAsync"/></term><description><c>query.Where(seek)</c> ordered by the chain, <c>.Take(limit)</c></description></item>
/// </list>
/// <para>
/// The ordering and the seek both come from <see cref="SeekExpressionBuilder"/>, so they cannot disagree. Positions are
/// the chain's key values of a row (<see cref="PositionOf"/>), read back typed from a token (<see cref="ParsePosition"/>).
/// </para>
/// <para>
/// <b>One instance per read.</b> A source wraps one filtered query and one order. Create it per call; it holds no
/// connection or state of its own beyond the query.
/// </para>
/// <para>
/// <b>Timestamp precision.</b> A bookmark on a date or time key is truncated to the precision the store keeps, so it
/// equals the value the store holds (see <see cref="SeekExpressionBuilder.PositionOf{T}"/>). The default of one
/// microsecond matches PostgreSQL and most stores. Pass the store's own precision when it differs. The in-memory entry
/// point passes one tick: memory keeps every tick, and truncating there would put a row before its own bookmark, so
/// continuation would serve it twice.
/// </para>
/// </remarks>
/// <example>
/// Paging a LINQ provider directly, with an executor of your own:
/// <code>
/// var source = new QueryablePageSource&lt;Player&gt;(
///     db.Players.Where(p =&gt; p.Team == team),                        // filtered, unordered
///     Order&lt;Player&gt;.ByDescending(p =&gt; p.Rating).ThenBy(p =&gt; p.Id),
///     new EfCoreQueryExecutor());                                      // see IQueryExecutor
///
/// var page = await pager.ReadAsync(source, paging, PageScope.For("ListPlayers", team), ct);
/// </code>
/// </example>
/// <typeparam name="T">The item type.</typeparam>
/// <seealso cref="IQueryExecutor"/>
/// <seealso cref="QueryableExtensions"/>
/// <seealso cref="Pager"/>
public sealed class QueryablePageSource<T> : IPageSource<T>
{
	private readonly IQueryable<T> _query;
	private readonly Order<T> _order;
	private readonly IQueryExecutor _executor;
	private readonly TimeSpan _timestampPrecision;

	/// <summary>Creates the source of one filtered read.</summary>
	/// <param name="query">The filtered, unordered query. Any ordering on it is replaced by <paramref name="order"/>.</param>
	/// <param name="order">The chain, ending in a unique tie-breaker.</param>
	/// <param name="executor">Runs the queries on the provider.</param>
	/// <param name="timestampPrecision">
	/// The precision the store keeps for date and time values; bookmarks are truncated to it. Defaults to one microsecond.
	/// Pass <c>TimeSpan.FromTicks(1)</c> for a store that keeps every tick, such as memory.
	/// </param>
	public QueryablePageSource(IQueryable<T> query, Order<T> order, IQueryExecutor executor, TimeSpan? timestampPrecision = null)
	{
		(_query, _order, _executor) = (query, order, executor);
		_timestampPrecision = timestampPrecision ?? TimeSpan.FromMicroseconds(1);
	}

	/// <inheritdoc />
	/// <remarks>The order's <see cref="Order{T}.OrderSignature"/>: a token issued under another order is refused.</remarks>
	public string OrderSignature => _order.OrderSignature;

	/// <inheritdoc />
	/// <remarks>Counts the filtered query; ordering is irrelevant to a count, so none is applied.</remarks>
	public Task<int> CountAsync(CancellationToken ct) =>
		_executor.CountAsync(_query, ct);

	/// <inheritdoc />
	/// <remarks><see cref="Pager"/> calls this with offset 0 for the first continuable page and for capped pages, and
	/// with <c>(page - 1) × size</c> for counted pages.</remarks>
	public Task<IReadOnlyList<T>> ReadAsync(int offset, int limit, CancellationToken ct) =>
		_executor.ToListAsync(SeekExpressionBuilder.ApplyOrdering(_query, _order).Skip(offset).Take(limit), ct);

	/// <inheritdoc />
	/// <remarks>The seek filter is applied before ordering, so the provider sees <c>WHERE … ORDER BY … LIMIT</c>, the
	/// shape an index on the chain's keys serves directly.</remarks>
	public Task<IReadOnlyList<T>> ReadAfterAsync(PagePosition position, int limit, CancellationToken ct) =>
		_executor.ToListAsync(
			SeekExpressionBuilder.ApplyOrdering(_query.Where(SeekExpressionBuilder.BuildSeek(_order, position)), _order).Take(limit),
			ct);

	/// <inheritdoc />
	/// <remarks>The chain's key values of <paramref name="item"/>, tie-breaker last, with timestamps truncated to the
	/// store's precision.</remarks>
	public PagePosition PositionOf(T item) =>
		SeekExpressionBuilder.PositionOf(_order, item, _timestampPrecision);

	/// <inheritdoc />
	/// <remarks>Each value is read as its key's type; a payload of the wrong length or type is refused with
	/// <see cref="InvalidPageTokenException"/>.</remarks>
	public PagePosition ParsePosition(IReadOnlyList<JsonElement> payload) =>
		SeekExpressionBuilder.ParsePosition(_order, payload);
}
