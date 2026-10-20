using System;
using System.Collections.Generic;

namespace Zooper.Ant;

/// <summary>
/// A single bounded screen of a read's best results, with no continuation: at most <see cref="Cap"/> items and whether
/// more matched. For a "top 5" widget or a preview list.
/// </summary>
/// <remarks>
/// <para>
/// A client can render one screen and say "and more" when <see cref="Truncated"/> is <see langword="true"/>; it has
/// no way to reach the rest, by design. That makes it the cheapest kind: <see cref="Pager.ReadAsync{TItem}"/> reads
/// <c>Cap + 1</c> items from the start of the order and nothing else, using the extra item only to set
/// <see cref="Truncated"/>. No count, no offset, no token.
/// </para>
/// <para>
/// The items are the first <see cref="Cap"/> items of the same order every other kind uses, so a capped page equals
/// the first counted page and the first continuable page of the same size.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var top = (CappedPage&lt;Player&gt;)await players.ToPageAsync(Paging.Capped(cap: 5), order, scope, pager, ct);
/// Render(top.ItemList, showMoreLink: top.Truncated);
/// </code>
/// </example>
/// <typeparam name="TItem">The item type carried on the screen.</typeparam>
/// <seealso cref="CappedPaging"/>
public sealed class CappedPage<TItem> : Page<TItem>
{
	/// <summary>Creates a capped page. Normally built by <see cref="Pager.ReadAsync{TItem}"/>.</summary>
	/// <param name="cap">The maximum number of items this read returns, after defaulting and clamping.</param>
	/// <param name="truncated">Whether further items matched beyond <paramref name="cap"/>.</param>
	/// <param name="itemList">The items, in the read's order, never more than <paramref name="cap"/> of them.</param>
	public CappedPage(int cap, bool truncated, IReadOnlyList<TItem> itemList)
		: base(itemList) => (Cap, Truncated) = (cap, truncated);

	/// <summary>The maximum number of items this read returns, which may be smaller than the cap requested because caps are clamped.</summary>
	public int Cap { get; }

	/// <summary>Whether the result was cut off at <see cref="Cap"/> with further items matching.</summary>
	public bool Truncated { get; }

	/// <inheritdoc />
	/// <remarks>Keeps <see cref="Cap"/> and <see cref="Truncated"/>.</remarks>
	public override Page<TOut> Map<TOut>(Func<TItem, TOut> map) => new CappedPage<TOut>(Cap, Truncated, MapItemList(map));
}
