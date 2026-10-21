using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Zooper.Ant.Sources;

/// <summary>
/// The floor a store implements to be paged: one ordered, filtered result set. <see cref="Pager"/> builds every page
/// kind, issues and opens tokens from it; a source is never told the kind and never handles a token.
/// </summary>
/// <remarks>
/// <para>
/// One source, one result set. A source is created per read call, over that call's filters and order. Every
/// member refers to the same filtered set in the same total order: <see cref="CountAsync"/> counts it,
/// <see cref="ReadAsync"/> reads a range of it, <see cref="ReadAfterAsync"/> continues it. The order must be total
/// (end with a unique tie-breaker) or a continuation can skip or repeat items.
/// </para>
/// <para>
/// Which kinds call what. Counted calls <see cref="CountAsync"/> and <see cref="ReadAsync"/> with an offset;
/// capped calls <see cref="ReadAsync"/> from offset 0; continuable calls <see cref="ReadAsync"/> from offset 0 or
/// <see cref="ReadAfterAsync"/>, then <see cref="PositionOf"/> and, for a returned token, <see cref="ParsePosition"/>.
/// Limits ask for one item more than the page; the source just returns up to <c>limit</c> items.
/// </para>
/// <para>
/// Stores that cannot count or skip. A native-cursor store (Elasticsearch <c>search_after</c>, Cosmos DB, Dapr
/// state queries) throws <see cref="System.NotSupportedException"/> from <see cref="CountAsync"/> and from
/// <see cref="ReadAsync"/> when <c>offset &gt; 0</c>. <see cref="Pager"/> turns exactly that into a
/// <see cref="PagingException"/> for the counted kind; continuable and capped pages are still served.
/// </para>
/// <para>
/// You may not need it. A LINQ provider only implements <see cref="IQueryExecutor"/>;
/// <see cref="QueryablePageSource{T}"/> implements this interface from an <see cref="Order{T}"/>.
/// </para>
/// </remarks>
/// <example>
/// A store with its own cursors, which can neither count nor skip:
/// <code>
/// public sealed class ActivityLogPageSource(ActivityLog log) : IPageSource&lt;ActivityEvent&gt;
/// {
///     public string OrderSignature => "";   // the store fixes the order
///
///     public Task&lt;int&gt; CountAsync(CancellationToken ct) => throw new NotSupportedException();
///
///     public Task&lt;IReadOnlyList&lt;ActivityEvent&gt;&gt; ReadAsync(int offset, int limit, CancellationToken ct) =>
///         offset > 0 ? throw new NotSupportedException() : log.ReadAsync(null, limit, ct);
///
///     public Task&lt;IReadOnlyList&lt;ActivityEvent&gt;&gt; ReadAfterAsync(PagePosition position, int limit, CancellationToken ct) =>
///         log.ReadAsync((string)position.Values[0]!, limit, ct);
///
///     public PagePosition PositionOf(ActivityEvent item) => new([log.CursorOf(item)]);
///
///     public PagePosition ParsePosition(IReadOnlyList&lt;JsonElement&gt; payload) =>
///         payload is [{ ValueKind: JsonValueKind.String } cursor]
///             ? new PagePosition([cursor.GetString()])
///             : throw new InvalidPageTokenException();
/// }
///
/// var page = await pager.ReadAsync(new ActivityLogPageSource(log), paging, PageScope.For("ListActivity"), ct);
/// </code>
/// </example>
/// <typeparam name="TItem">The item type.</typeparam>
/// <seealso cref="Pager"/>
/// <seealso cref="PagePosition"/>
/// <seealso cref="QueryablePageSource{T}"/>
public interface IPageSource<TItem>
{
	/// <summary>
	/// A stable description of the ordering, folded into the token binding so a changed ordering refuses earlier tokens.
	/// A native-cursor source returns an empty string.
	/// </summary>
	/// <remarks>
	/// It must be equal for equal orderings across processes and restarts, and differ whenever the order changes
	/// (keys, directions, nulls placement). <see cref="Pager"/> passes it to <see cref="PageScope.Bind"/>. A source
	/// whose order is fixed by the store has nothing to sign and returns <c>""</c>.
	/// </remarks>
	string OrderSignature { get; }

	/// <summary>Counts the items of the result set.</summary>
	/// <param name="ct">Cancels the count.</param>
	/// <returns>The number of items matching the read, across every page.</returns>
	/// <exception cref="System.NotSupportedException">The store cannot count; the counted kind is then refused.</exception>
	Task<int> CountAsync(CancellationToken ct);

	/// <summary>Reads up to <paramref name="limit"/> items in order, skipping <paramref name="offset"/>.</summary>
	/// <param name="offset">The number of items to skip; 0 for the first page of any kind.</param>
	/// <param name="limit">The maximum number of items to return; the pager asks for one more than the page.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <returns>At most <paramref name="limit"/> items, in the source's order; fewer at the end of the set.</returns>
	/// <exception cref="System.NotSupportedException">
	/// <paramref name="offset"/> is above 0 and the store cannot skip; the counted kind is then refused.
	/// </exception>
	Task<IReadOnlyList<TItem>> ReadAsync(int offset, int limit, CancellationToken ct);

	/// <summary>Reads up to <paramref name="limit"/> items ordered strictly after <paramref name="position"/>.</summary>
	/// <remarks>
	/// "Strictly after" in the source's total order: the item the position was taken from is not returned again. The
	/// read runs on the data current at the time of the call; items deleted before the position do not shift the
	/// result, and items inserted after it are included.
	/// </remarks>
	/// <param name="position">A position this source issued, read back by <see cref="ParsePosition"/>.</param>
	/// <param name="limit">The maximum number of items to return.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <returns>At most <paramref name="limit"/> items after the position, in order.</returns>
	Task<IReadOnlyList<TItem>> ReadAfterAsync(PagePosition position, int limit, CancellationToken ct);

	/// <summary>Gives the position of a served item.</summary>
	/// <remarks>
	/// Called for the last item of a page that has a next page. The values go into the token, so they must be
	/// JSON-serialisable, and they must identify the item's place in the order uniquely (tie-breaker included).
	/// </remarks>
	/// <param name="item">An item this source returned.</param>
	/// <returns>The position to continue strictly after.</returns>
	PagePosition PositionOf(TItem item);

	/// <summary>Reads a position back from its serialised values.</summary>
	/// <remarks>
	/// The payload already passed the token's integrity and binding checks, but it may come from an older version of the
	/// read whose ordering had another shape. Check the count and the types, and refuse what does not fit. Any exception
	/// other than a <see cref="PagingException"/> is wrapped by <see cref="Pager"/> into an
	/// <see cref="InvalidPageTokenException"/>, so a parse error is never an internal error.
	/// </remarks>
	/// <param name="payload">The position values as JSON elements, in the order <see cref="PositionOf"/> produced them.</param>
	/// <returns>The position.</returns>
	/// <exception cref="InvalidPageTokenException">The values do not fit this source's position shape.</exception>
	PagePosition ParsePosition(IReadOnlyList<JsonElement> payload);
}
