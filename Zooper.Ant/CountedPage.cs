using System;
using System.Collections.Generic;

namespace Zooper.Ant;

/// <summary>
/// One page of a read, addressed by page number and carrying the exact number of items that matched. The only kind
/// that lets a client render "page 3 of 62" and jump to an arbitrary page.
/// </summary>
/// <remarks>
/// <para>
/// It is the costliest kind. <see cref="Pager.ReadAsync{TItem}"/> asks the source for a count of every match and then
/// for one offset range, <c>(Page - 1) × Size</c> rows skipped and <see cref="Size"/> rows read, so both the count and
/// the skip grow with the result set. The caller chooses this kind with that cost known.
/// </para>
/// <para>
/// Because of the offset, its depth is bounded: <see cref="Paging.Counted(int?, int?, PagingLimits)"/> refuses a page
/// whose <c>page × size</c> exceeds <see cref="PagingLimits.MaximumDepth"/>. Over live data an offset page can also
/// shift when rows before it are inserted or deleted; a client that needs stable continuation uses the continuable kind.
/// </para>
/// <para>
/// A page number past the last page is not an error: the page has an empty <see cref="Page{TItem}.ItemList"/> and still
/// reports the <see cref="TotalCount"/>. A source that cannot count or skip (a native-cursor store) cannot serve this
/// kind; the <see cref="Pager"/> refuses it with a <see cref="PagingException"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var page = (CountedPage&lt;Player&gt;)await players.ToPageAsync(Paging.Counted(page: 3, size: 20), order, scope, pager, ct);
/// var pageCount = (page.TotalCount + page.Size - 1) / page.Size;
/// Console.WriteLine($"Page {page.Page} of {pageCount}");
/// </code>
/// </example>
/// <typeparam name="TItem">The item type carried on the page.</typeparam>
/// <seealso cref="CountedPaging"/>
public sealed class CountedPage<TItem> : Page<TItem>
{
	/// <summary>Creates a counted page. Normally built by <see cref="Pager.ReadAsync{TItem}"/>.</summary>
	/// <param name="page">The one-based page number this page answers.</param>
	/// <param name="size">The page size actually used, after defaulting and clamping.</param>
	/// <param name="totalCount">The number of items matching the read, across every page.</param>
	/// <param name="itemList">The items on this page, in the read's order; empty past the last page.</param>
	public CountedPage(int page, int size, int totalCount, IReadOnlyList<TItem> itemList)
		: base(itemList) => (Page, Size, TotalCount) = (page, size, totalCount);

	/// <summary>The one-based page number this page answers, as requested.</summary>
	public int Page { get; }

	/// <summary>The page size actually used, which may be smaller than the size requested because sizes are clamped.</summary>
	public int Size { get; }

	/// <summary>
	/// The number of items matching the read, across every page, counted in the same request as the page. Over live
	/// data it may differ from the count of an earlier page.
	/// </summary>
	public int TotalCount { get; }

	/// <inheritdoc />
	/// <remarks>Keeps <see cref="Page"/>, <see cref="Size"/> and <see cref="TotalCount"/>.</remarks>
	public override Page<TOut> Map<TOut>(Func<TItem, TOut> map) => new CountedPage<TOut>(Page, Size, TotalCount, MapItemList(map));
}
