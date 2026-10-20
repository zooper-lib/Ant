using System;

namespace Zooper.Ant;

/// <summary>
/// The instruction a caller gives a paged read: which kind of page it wants and how far into the result it is. Built
/// only through the static factories, which apply the <see cref="PagingLimits"/>.
/// </summary>
/// <remarks>
/// <para>
/// There are three kinds, one sealed record each, and the kind decides the page that comes back from
/// <see cref="Pager.ReadAsync{TItem}"/>:
/// <list type="table">
/// <listheader><term>Instruction</term><description>Arguments → result</description></listheader>
/// <item><term><see cref="ContinuablePaging"/></term><description>a token (or none) and a size →
/// <see cref="ContinuablePage{TItem}"/></description></item>
/// <item><term><see cref="CountedPaging"/></term><description>a page number and a size →
/// <see cref="CountedPage{TItem}"/></description></item>
/// <item><term><see cref="CappedPaging"/></term><description>a cap → <see cref="CappedPage{TItem}"/></description></item>
/// </list>
/// Each kind carries only its own arguments, so an instruction can never hold a token on a counted request or a page
/// number on a capped one. A host that receives an argument of another kind from a caller refuses the request rather
/// than ignoring the argument.
/// </para>
/// <para>
/// The factories are the one place that bounds paging, so every caller (a web endpoint, a message handler, a job) is
/// bounded alike before any store is asked. A missing or non-positive size or cap becomes
/// <see cref="PagingLimits.DefaultSize"/>; a larger one is clamped to <see cref="PagingLimits.MaximumSize"/> and
/// served, because a smaller page is still a correct answer. A counted page below one, or one whose <c>page × size</c>
/// exceeds <see cref="PagingLimits.MaximumDepth"/>, is refused rather than normalised: serving another page under the
/// requested number would be a wrong answer presented as a right one.
/// </para>
/// <para>
/// Pass the host's limits, <see cref="Pager.Limits"/>, to the factories. The overloads without a limits argument use
/// <see cref="PagingLimits.Default"/>, which is right only when the host keeps the defaults. When the caller names no
/// kind, use the continuable one: it is the default kind.
/// </para>
/// <para>
/// The base constructor is <c>private protected</c> and the kinds' constructors are internal, so an unbounded
/// instruction cannot be built. Instructions are immutable records and compare by value.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Paging paging = request.Kind switch
/// {
///     "counted" =&gt; Paging.Counted(request.Page, request.Size, pager.Limits),
///     "capped" =&gt; Paging.Capped(request.Cap, pager.Limits),
///     _ =&gt; Paging.Continuable(request.Token, request.Size, pager.Limits)
/// };
///
/// var page = await players.ToPageAsync(paging, order, scope, pager, ct);
/// </code>
/// </example>
/// <seealso cref="PagingLimits"/>
/// <seealso cref="Page{TItem}"/>
/// <seealso cref="Pager"/>
public abstract record Paging
{
	/// <summary>Restricts the instruction to the three kinds in this assembly.</summary>
	private protected Paging() { }

	/// <summary>Requests a continuable page under <see cref="PagingLimits.Default"/>.</summary>
	/// <param name="pageToken">The <see cref="ContinuablePage{TItem}.NextPageToken"/> of the previous page, or <see langword="null"/> for the first page.</param>
	/// <param name="size">The requested page size; <see langword="null"/> or below one becomes 20, above 50 becomes 50.</param>
	/// <returns>The bounded continuable paging instruction.</returns>
	public static ContinuablePaging Continuable(PageToken? pageToken, int? size) =>
		Continuable(pageToken, size, PagingLimits.Default);

	/// <summary>Requests a continuable page, continuing after <paramref name="pageToken"/> when one is given.</summary>
	/// <remarks>
	/// The token is not opened here; it is checked against the read when <see cref="Pager.ReadAsync{TItem}"/> runs. The
	/// size is not bound to the token, so it may differ from the size of the page that issued it. There is no depth
	/// check: continuation is a seek, not an offset.
	/// </remarks>
	/// <param name="pageToken">The <see cref="ContinuablePage{TItem}.NextPageToken"/> of the previous page, or <see langword="null"/> for the first page.</param>
	/// <param name="size">The requested page size; defaulted and clamped by <paramref name="limits"/>.</param>
	/// <param name="limits">The bounds to apply, normally <see cref="Pager.Limits"/>.</param>
	/// <returns>The bounded continuable paging instruction.</returns>
	/// <exception cref="PagingException"><paramref name="limits"/> are inconsistent.</exception>
	public static ContinuablePaging Continuable(PageToken? pageToken, int? size, PagingLimits limits) =>
		new(pageToken, Bound(size, limits));

	/// <summary>Requests a counted page by number under <see cref="PagingLimits.Default"/>.</summary>
	/// <param name="page">The one-based page number; <see langword="null"/> means the first page.</param>
	/// <param name="size">The requested page size; <see langword="null"/> or below one becomes 20, above 50 becomes 50.</param>
	/// <returns>The bounded counted paging instruction.</returns>
	/// <exception cref="PagingException">The page is below one, or <c>page × size</c> exceeds 1000.</exception>
	public static CountedPaging Counted(int? page, int? size) =>
		Counted(page, size, PagingLimits.Default);

	/// <summary>Requests a counted page by number.</summary>
	/// <remarks>
	/// The size is bounded first, then the depth is checked against the bounded size: <c>Counted(1000, 1)</c> reaches
	/// exactly row 1000 and is served, <c>Counted(1000, null)</c> reaches row 20 000 and is refused under the defaults.
	/// The product is computed as a <see cref="long"/> so a huge page number cannot overflow past the check.
	/// </remarks>
	/// <param name="page">The one-based page number; <see langword="null"/> means the first page.</param>
	/// <param name="size">The requested page size; defaulted and clamped by <paramref name="limits"/>.</param>
	/// <param name="limits">The bounds to apply, normally <see cref="Pager.Limits"/>.</param>
	/// <returns>The bounded counted paging instruction.</returns>
	/// <exception cref="PagingException">
	/// The page is below one, <c>page × size</c> exceeds <see cref="PagingLimits.MaximumDepth"/> (the message names the
	/// bound and suggests continuable paging), or <paramref name="limits"/> are inconsistent.
	/// </exception>
	public static CountedPaging Counted(int? page, int? size, PagingLimits limits)
	{
		var resolvedPage = page ?? 1;
		var resolvedSize = Bound(size, limits);

		// Refused, never normalised to page 1: serving page 1 under the number 0 would be a wrong answer.
		if (resolvedPage < 1)
			throw new PagingException("Paging page must be at least 1.");

		// long, so page × size cannot overflow int and wrap below the bound.
		if ((long)resolvedPage * resolvedSize > limits.MaximumDepth)
			throw new PagingException(
				$"Paging cannot reach beyond {limits.MaximumDepth} rows; request continuable paging to go further.");

		return new CountedPaging(resolvedPage, resolvedSize);
	}

	/// <summary>Requests one capped screen of best results under <see cref="PagingLimits.Default"/>.</summary>
	/// <param name="cap">The requested cap; <see langword="null"/> or below one becomes 20, above 50 becomes 50.</param>
	/// <returns>The bounded capped paging instruction.</returns>
	public static CappedPaging Capped(int? cap) =>
		Capped(cap, PagingLimits.Default);

	/// <summary>Requests one capped screen of best results.</summary>
	/// <remarks>The cap is bounded exactly like a size. A capped page has no offset, so there is no depth check.</remarks>
	/// <param name="cap">The requested cap; defaulted and clamped by <paramref name="limits"/>.</param>
	/// <param name="limits">The bounds to apply, normally <see cref="Pager.Limits"/>.</param>
	/// <returns>The bounded capped paging instruction.</returns>
	/// <exception cref="PagingException"><paramref name="limits"/> are inconsistent.</exception>
	public static CappedPaging Capped(int? cap, PagingLimits limits) =>
		new(Bound(cap, limits));

	/// <summary>
	/// Builds an empty page of the kind this instruction asks for, for a read whose filter is known to match nothing
	/// before any store is asked.
	/// </summary>
	/// <remarks>
	/// The empty page keeps the instruction's values: a counted page keeps its page number and size with a total of 0, a
	/// capped page its cap with <see cref="CappedPage{TItem}.Truncated"/> false, a continuable page its size with no
	/// next token. The caller therefore gets the kind it asked for, as from a real read.
	/// </remarks>
	/// <example>
	/// <code>
	/// if (filter.MinRating &gt; 100)
	///     return paging.ToEmptyPage&lt;PlayerView&gt;();
	/// </code>
	/// </example>
	/// <typeparam name="TItem">The page's item type.</typeparam>
	/// <returns>The empty page, of the matching kind.</returns>
	/// <exception cref="InvalidOperationException">Never in practice: the three kinds are the only ones that can exist.</exception>
	public Page<TItem> ToEmptyPage<TItem>() =>
		this switch
		{
			CountedPaging counted => new CountedPage<TItem>(counted.Page, counted.Size, 0, []),
			CappedPaging capped => new CappedPage<TItem>(capped.Cap, false, []),
			ContinuablePaging continuable => new ContinuablePage<TItem>(continuable.Size, null, []),
			_ => throw new InvalidOperationException("Paging kind is not known.")
		};

	/// <summary>Applies the default and the maximum to a requested size or cap.</summary>
	/// <param name="requested">The caller's size or cap, possibly missing or non-positive.</param>
	/// <param name="limits">The bounds to apply.</param>
	/// <returns>A size between 1 and <see cref="PagingLimits.MaximumSize"/>.</returns>
	/// <exception cref="PagingException"><paramref name="limits"/> are inconsistent.</exception>
	private static int Bound(int? requested, PagingLimits limits)
	{
		// Defensive and cheap: limits built by hand, not through AddAnt, are checked here too.
		limits.Validate();

		return requested switch
		{
			null or < 1 => limits.DefaultSize,
			// Clamped, not refused: a smaller page is still a correct answer.
			_ when requested > limits.MaximumSize => limits.MaximumSize,
			_ => requested.Value
		};
	}
}

/// <summary>
/// Continuable paging: a size and the token of the previous page, if any. Answered with a
/// <see cref="ContinuablePage{TItem}"/>.
/// </summary>
/// <remarks>Built by <see cref="Paging.Continuable(PageToken?, int?, PagingLimits)"/>; the default kind.</remarks>
public sealed record ContinuablePaging : Paging
{
	/// <summary>Stores the already bounded values; only the factory calls it.</summary>
	internal ContinuablePaging(PageToken? pageToken, int size) => (PageToken, Size) = (pageToken, size);

	/// <summary>
	/// The token to continue after, or <see langword="null"/> for the first page. Unopened: it is checked against the
	/// read by <see cref="Pager.ReadAsync{TItem}"/>.
	/// </summary>
	public PageToken? PageToken { get; }

	/// <summary>The bounded page size, between 1 and <see cref="PagingLimits.MaximumSize"/>.</summary>
	public int Size { get; }
}

/// <summary>
/// Counted paging: a one-based page number and a size, within the depth bound. Answered with a
/// <see cref="CountedPage{TItem}"/>.
/// </summary>
/// <remarks>Built by <see cref="Paging.Counted(int?, int?, PagingLimits)"/>, which refuses pages below one or too deep.</remarks>
public sealed record CountedPaging : Paging
{
	/// <summary>Stores the already checked values; only the factory calls it.</summary>
	internal CountedPaging(int page, int size) => (Page, Size) = (page, size);

	/// <summary>The one-based page number, at least 1, with <c>Page × Size</c> within <see cref="PagingLimits.MaximumDepth"/>.</summary>
	public int Page { get; }

	/// <summary>The bounded page size, between 1 and <see cref="PagingLimits.MaximumSize"/>.</summary>
	public int Size { get; }
}

/// <summary>
/// Capped paging: one screen of at most <see cref="Cap"/> items, with no continuation. Answered with a
/// <see cref="CappedPage{TItem}"/>.
/// </summary>
/// <remarks>Built by <see cref="Paging.Capped(int?, PagingLimits)"/>.</remarks>
public sealed record CappedPaging : Paging
{
	/// <summary>Stores the already bounded cap; only the factory calls it.</summary>
	internal CappedPaging(int cap) => Cap = cap;

	/// <summary>The bounded cap, between 1 and <see cref="PagingLimits.MaximumSize"/>.</summary>
	public int Cap { get; }
}
