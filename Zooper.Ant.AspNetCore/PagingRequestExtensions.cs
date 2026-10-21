namespace Zooper.Ant.AspNetCore;

/// <summary>Turns a bound <see cref="PagingRequest"/> into the <see cref="Paging"/> instruction of a read.</summary>
public static class PagingRequestExtensions
{
	/// <summary>Builds the paging instruction of <paramref name="request"/>, bounded by the host's limits.</summary>
	/// <remarks>
	/// <para>
	/// Each kind accepts only its own arguments: continuable takes <c>pageToken</c> and <c>size</c>, counted takes
	/// <c>page</c> and <c>size</c>, capped takes <c>cap</c>. An argument of another kind is refused rather than ignored,
	/// because ignoring it would serve a wrong answer that looks like a right one. The refusal names the first foreign
	/// argument it finds, in the order <c>page</c>, <c>size</c>, <c>cap</c>, <c>pageToken</c>.
	/// </para>
	/// <para>
	/// Nothing else is checked here. The <see cref="Paging"/> factories default and clamp the size or cap and refuse a
	/// counted page below one or deeper than <see cref="PagingLimits.MaximumDepth"/>, all under
	/// <paramref name="limits"/>. There is deliberately no overload without limits: it would quietly apply
	/// <see cref="PagingLimits.Default"/> to a host that configured others.
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// var page = await pager.ReadAsync(source, paging.ToPaging(pager.Limits), PageScope.For("ListActivity"), ct);
	/// </code>
	/// </example>
	/// <param name="request">The bound query arguments.</param>
	/// <param name="limits">The host's limits, <see cref="Pager.Limits"/>.</param>
	/// <returns>A <see cref="ContinuablePaging"/>, <see cref="CountedPaging"/> or <see cref="CappedPaging"/>.</returns>
	/// <exception cref="PagingException">
	/// An argument belongs to another kind (<c>Paging argument 'page' does not belong to capped paging.</c>), the kind
	/// is not one of the three, or a <see cref="Paging"/> factory refused the values. <see cref="PagingExceptionHandler"/>
	/// turns it into a 400.
	/// </exception>
	public static Paging ToPaging(this PagingRequest request, PagingLimits limits) =>
		// Each arm first refuses the arguments its kind does not take; Refuse returns null when none is present, so ??
		// falls through to the factory. PagingKind is a struct, not an enum, so the arms compare with == rather than a
		// constant pattern.
		request.Paging switch
		{
			null => Continuable(request, limits),
			var kind when kind == PagingKind.Continuable => Continuable(request, limits),
			var kind when kind == PagingKind.Counted => Refuse(request, "counted", pageToken: true, cap: true)
				?? Paging.Counted(request.Page, request.Size, limits),
			var kind when kind == PagingKind.Capped => Refuse(request, "capped", page: true, size: true, pageToken: true)
				?? Paging.Capped(request.Cap, limits),
			// Binding only produces the three kinds; only a hand-built default(PagingKind) gets here.
			_ => throw new PagingException("Paging kind is not known.")
		};

	/// <summary>The continuable arm, shared by an absent and a named kind.</summary>
	/// <param name="request">The bound query arguments.</param>
	/// <param name="limits">The host's limits.</param>
	/// <returns>The continuable instruction.</returns>
	/// <exception cref="PagingException">A <c>page</c> or <c>cap</c> is present.</exception>
	private static Paging Continuable(PagingRequest request, PagingLimits limits) =>
		Refuse(request, "continuable", page: true, cap: true)
			?? Paging.Continuable(request.PageToken, request.Size, limits);

	/// <summary>Throws naming the first refused argument that is present; returns <see langword="null"/> when none is.</summary>
	/// <remarks>Returns <see cref="Paging"/>? only so the call composes with <c>??</c> in <see cref="ToPaging"/>.</remarks>
	/// <param name="request">The bound query arguments.</param>
	/// <param name="kind">The kind's wire name, for the message.</param>
	/// <param name="page">Whether <c>page</c> is foreign to the kind.</param>
	/// <param name="size">Whether <c>size</c> is foreign to the kind.</param>
	/// <param name="cap">Whether <c>cap</c> is foreign to the kind.</param>
	/// <param name="pageToken">Whether <c>pageToken</c> is foreign to the kind.</param>
	/// <returns>Always <see langword="null"/>; it throws instead of returning anything else.</returns>
	/// <exception cref="PagingException">A foreign argument is present.</exception>
	private static Paging? Refuse(
		PagingRequest request,
		string kind,
		bool page = false,
		bool size = false,
		bool cap = false,
		bool pageToken = false)
	{
		var intruder =
			page && request.Page is not null ? "page"
			: size && request.Size is not null ? "size"
			: cap && request.Cap is not null ? "cap"
			: pageToken && request.PageToken is not null ? "pageToken"
			: null;

		return intruder is null
			? null
			: throw new PagingException($"Paging argument '{intruder}' does not belong to {kind} paging.");
	}
}
