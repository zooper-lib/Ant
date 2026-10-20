using Zooper.Ant;

namespace Zooper.Ant.Example;

/// <summary>
/// The paging arguments of a list endpoint as they arrive in the query string: <c>?kind=</c> plus the arguments of that
/// kind. <see cref="ToPaging"/> turns them into the <see cref="Paging"/> instruction a query passes to
/// <see cref="Pager"/>.
/// </summary>
/// <remarks>
/// <para>
/// Bound with <c>[AsParameters]</c> in <see cref="Api"/>. <see cref="PageToken"/> binds directly from the query
/// string because it implements <see cref="IParsable{TSelf}"/>: a token that is blank or not base64url is answered with
/// a 400 by ASP.NET Core before the endpoint runs. Whether the token is genuine is checked later, by the
/// <see cref="Pager"/>, when it opens the token.
/// </para>
/// <para>
/// The planned Zooper.Ant.AspNetCore package will ship this binding. Until then an application writes these few lines
/// itself; this record shows the rules they must follow.
/// </para>
/// </remarks>
/// <param name="Kind"><c>continuable</c> (the default when absent), <c>counted</c> or <c>capped</c>.</param>
/// <param name="Token">The continuation token of the previous continuable page; absent for the first page.</param>
/// <param name="Page">The one-based page number of a counted page.</param>
/// <param name="Size">The page size of a continuable or counted page; defaulted and clamped by the limits.</param>
/// <param name="Cap">The cap of a capped page; defaulted and clamped like a size.</param>
public sealed record PagingQuery(string? Kind, PageToken? Token, int? Page, int? Size, int? Cap)
{
	/// <summary>Builds the paging instruction, bounded by the host's limits.</summary>
	/// <remarks>
	/// <para>
	/// Each kind accepts only its own arguments: continuable takes <c>token</c> and <c>size</c>, counted takes
	/// <c>page</c> and <c>size</c>, capped takes <c>cap</c>. An argument of another kind (a <c>page</c> on a capped
	/// request, a <c>token</c> on a counted one) is refused rather than ignored, because silently ignoring it would
	/// serve a wrong answer that looks like a right one.
	/// </para>
	/// <para>
	/// The <see cref="Paging"/> factories do the rest: they default and clamp the size, and refuse a counted page below
	/// one or deeper than <see cref="PagingLimits.MaximumDepth"/>.
	/// </para>
	/// </remarks>
	/// <param name="limits">The host's limits, from <see cref="Pager.Limits"/>.</param>
	/// <returns>A <see cref="ContinuablePaging"/>, <see cref="CountedPaging"/> or <see cref="CappedPaging"/>.</returns>
	/// <exception cref="PagingException">
	/// The kind is unknown, an argument belongs to another kind, or a <see cref="Paging"/> factory refused the values.
	/// <see cref="PagingExceptionHandler"/> turns it into a 400.
	/// </exception>
	public Paging ToPaging(PagingLimits limits) =>
		(Kind ?? "continuable") switch
		{
			// Each arm matches only when no argument of another kind is present; otherwise it falls through to the refusal.
			"continuable" when Page is null && Cap is null => Paging.Continuable(Token, Size, limits),
			"counted" when Token is null && Cap is null => Paging.Counted(Page, Size, limits),
			"capped" when Token is null && Page is null && Size is null => Paging.Capped(Cap, limits),

			// A known kind whose arm did not match was given a foreign argument: name what the kind accepts.
			"continuable" or "counted" or "capped" => throw new PagingException(
				$"The {Kind ?? "continuable"} kind takes {(Kind ?? "continuable") switch { "continuable" => "token and size", "counted" => "page and size", _ => "cap" }} only."),
			_ => throw new PagingException($"Paging kind '{Kind}' is not one of continuable, counted, capped.")
		};
}
