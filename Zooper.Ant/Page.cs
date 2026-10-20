using System;
using System.Collections.Generic;
using System.Linq;

namespace Zooper.Ant;

/// <summary>
/// One page of a paged read: the items in the read's order, plus the page-level values of its kind. This is the result
/// type of every read in Zooper.Ant, whichever store served it.
/// </summary>
/// <remarks>
/// <para>
/// A page is always one of three sealed kinds, chosen by the caller's <see cref="Paging"/> instruction and built by
/// <see cref="Pager.ReadAsync{TItem}"/>:
/// <list type="bullet">
/// <item><description><see cref="ContinuablePage{TItem}"/> for a <see cref="ContinuablePaging"/>: the size used and a
/// next token while more items exist.</description></item>
/// <item><description><see cref="CountedPage{TItem}"/> for a <see cref="CountedPaging"/>: the page number, the size
/// used and the total count.</description></item>
/// <item><description><see cref="CappedPage{TItem}"/> for a <see cref="CappedPaging"/>: the cap and whether more
/// items matched.</description></item>
/// </list>
/// No kind is a bare wrapper around a list: each carries exactly what a client needs to render that kind of screen.
/// </para>
/// <para>
/// Code between the store and the edge usually does not need to know the kind. It maps the items with
/// <see cref="Map{TOut}"/>, which returns a page of the same kind with the same page-level values, and passes the page
/// on. Only code that renders or follows a page (a host, or <see cref="Pager.TraverseAsync{TItem}"/>) inspects the kind
/// with a type pattern.
/// </para>
/// <para>
/// The constructor is <c>private protected</c>, so the three kinds in this assembly are the only ones that can exist.
/// A page is immutable: <see cref="ItemList"/> is set once and <see cref="Map{TOut}"/> builds a new page.
/// </para>
/// <para>
/// Serialised with <c>System.Text.Json</c> as its runtime type, a page writes only its own fields and no type
/// discriminator, for example <c>{"size":3,"nextPageToken":"…","itemList":[…]}</c> for a continuable page under web
/// naming. A host that declares the return type as <see cref="Page{TItem}"/> should serialise the runtime type.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Page&lt;Player&gt; page = await players.ToPageAsync(paging, order, scope, pager, ct);
///
/// // Map without naming the kind; the result keeps it.
/// Page&lt;PlayerView&gt; view = page.Map(PlayerView.From);
///
/// // Inspect the kind only where it matters.
/// if (view is ContinuablePage&lt;PlayerView&gt; { NextPageToken: { } next })
///     Console.WriteLine($"More after this page: {next}");
/// </code>
/// </example>
/// <typeparam name="TItem">The item type carried on the page.</typeparam>
/// <seealso cref="Paging"/>
/// <seealso cref="Pager"/>
public abstract class Page<TItem>
{
	/// <summary>Stores the items; only the three kinds in this assembly call it.</summary>
	/// <param name="itemList">The items on this page, in the read's order.</param>
	private protected Page(IReadOnlyList<TItem> itemList) => ItemList = itemList;

	/// <summary>The items on this page, in the read's order. Empty when nothing matched or the page is past the end.</summary>
	public IReadOnlyList<TItem> ItemList { get; }

	/// <summary>Maps every item, keeping the page's kind and its page-level values.</summary>
	/// <remarks>
	/// The map runs once per item, in order, eagerly. A <see cref="ContinuablePage{TItem}"/> keeps its size and next
	/// token, a <see cref="CountedPage{TItem}"/> its page number, size and total, a <see cref="CappedPage{TItem}"/> its
	/// cap and truncation flag. A token stays valid after mapping because it describes the position of the read's own
	/// items, not of the mapped ones.
	/// </remarks>
	/// <typeparam name="TOut">The mapped item type.</typeparam>
	/// <param name="map">The mapping applied to each item, in order.</param>
	/// <returns>A new page of the same kind over the mapped items.</returns>
	public abstract Page<TOut> Map<TOut>(Func<TItem, TOut> map);

	/// <summary>Maps the items into a new list; shared by the three <see cref="Map{TOut}"/> overrides.</summary>
	/// <typeparam name="TOut">The mapped item type.</typeparam>
	/// <param name="map">The mapping applied to each item, in order.</param>
	/// <returns>The mapped items, materialised so the new page does not re-run the map on every enumeration.</returns>
	private protected IReadOnlyList<TOut> MapItemList<TOut>(Func<TItem, TOut> map) =>
		ItemList.Select(map).ToList();
}
