using System;
using System.Collections.Generic;

namespace Zooper.Ant;

/// <summary>
/// One page of a read plus, while more items exist, the token of the next page. The default kind, for "load more",
/// infinite scroll and jobs that read everything.
/// </summary>
/// <remarks>
/// <para>
/// A client can step forward with this kind but cannot render a total or jump to an arbitrary page, because no total
/// is reported. In return it is cheap and correct over live data: <see cref="Pager.ReadAsync{TItem}"/> reads one item
/// more than <see cref="Size"/> to learn whether a further page exists, so the cost is the page plus one item rather
/// than an evaluation of every match. Continuation is a keyset seek past the last served item, never an offset, so it
/// has no depth limit and is not shifted by rows deleted or inserted before the bookmark.
/// </para>
/// <para>
/// The absence of <see cref="NextPageToken"/> is the only end signal. A page with fewer items than <see cref="Size"/>
/// also has no token, but a full page may be the last one too; callers must not infer the end from the item count.
/// </para>
/// <para>
/// To get the next page, the caller sends <see cref="NextPageToken"/> back unchanged in
/// <see cref="Paging.Continuable(PageToken?, int?, PagingLimits)"/> with the same read and the same result-defining
/// arguments (see <see cref="PageScope"/>). The size may change between pages. <see cref="Pager.TraverseAsync{TItem}"/>
/// does this loop for code that needs every item.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// PageToken? token = null;
///
/// do
/// {
///     var page = (ContinuablePage&lt;Player&gt;)await players.ToPageAsync(Paging.Continuable(token, 20), order, scope, pager, ct);
///     Render(page.ItemList);
///     token = page.NextPageToken;
/// }
/// while (token is not null);
/// </code>
/// </example>
/// <typeparam name="TItem">The item type carried on the page.</typeparam>
/// <seealso cref="ContinuablePaging"/>
/// <seealso cref="PageToken"/>
public sealed class ContinuablePage<TItem> : Page<TItem>
{
	/// <summary>Creates a continuable page. Normally built by <see cref="Pager.ReadAsync{TItem}"/>.</summary>
	/// <param name="size">The page size actually used, after defaulting and clamping.</param>
	/// <param name="nextPageToken">The token of the next page, or <see langword="null"/> on the last page.</param>
	/// <param name="itemList">The items on this page, in the read's order; at most <paramref name="size"/> of them.</param>
	public ContinuablePage(int size, PageToken? nextPageToken, IReadOnlyList<TItem> itemList)
		: base(itemList) => (Size, NextPageToken) = (size, nextPageToken);

	/// <summary>The page size actually used, which may be smaller than the size requested because sizes are clamped.</summary>
	public int Size { get; }

	/// <summary>
	/// The token of the next page, or <see langword="null"/> when this is the last page. Opaque: callers pass it back
	/// unchanged and never interpret it.
	/// </summary>
	public PageToken? NextPageToken { get; }

	/// <inheritdoc />
	/// <remarks>Keeps <see cref="Size"/> and <see cref="NextPageToken"/>.</remarks>
	public override Page<TOut> Map<TOut>(Func<TItem, TOut> map) => new ContinuablePage<TOut>(Size, NextPageToken, MapItemList(map));
}
