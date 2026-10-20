namespace Zooper.Ant;

/// <summary>
/// The bounds a host imposes on every paging instruction: the size used when the caller gives none, the largest size
/// served, and how deep a counted page may reach.
/// </summary>
/// <remarks>
/// <para>
/// The limits are applied in exactly one place: the <see cref="Paging"/> factories. Every caller, a web endpoint or
/// code inside the service, builds its instruction through them, so all callers are bounded alike before any store is
/// asked. How each bound is applied:
/// <list type="bullet">
/// <item><description><see cref="DefaultSize"/> replaces a missing, zero or negative size or cap.</description></item>
/// <item><description><see cref="MaximumSize"/> clamps a larger size or cap, which is then served without error,
/// because a smaller page is still a correct answer.</description></item>
/// <item><description><see cref="MaximumDepth"/> refuses a counted page whose <c>page × size</c> exceeds it with a
/// <see cref="PagingException"/>; serving another page under the requested number would be a wrong answer presented
/// as a right one. Continuable and capped pages have no offset and no depth bound.</description></item>
/// </list>
/// </para>
/// <para>
/// A host configures its limits once in <see cref="Zooper.Ant.DependencyInjection.AntOptions.Limits"/> through
/// <c>services.AddAnt(...)</c>, which validates them when the host starts. Code then reads them back from
/// <see cref="Pager.Limits"/> and passes them to the factories. The factory overloads without a limits argument use
/// <see cref="Default"/>.
/// </para>
/// <para>
/// It is an immutable positional record, so two limits with the same values are equal and <c>with</c> derives a
/// variant.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddAnt(options =&gt; options.Limits = new PagingLimits(DefaultSize: 25, MaximumSize: 100, MaximumDepth: 5000));
///
/// // Later, in a handler with an injected Pager:
/// var paging = Paging.Counted(request.Page, request.Size, pager.Limits);
/// </code>
/// </example>
/// <param name="DefaultSize">The size or cap applied when the caller supplies none or a non-positive one. At least 1.</param>
/// <param name="MaximumSize">The largest size or cap served; a larger request is clamped to it. At least <paramref name="DefaultSize"/>.</param>
/// <param name="MaximumDepth">The deepest row a counted page may reach, as <c>page × size</c>. At least <paramref name="MaximumSize"/>.</param>
/// <seealso cref="Paging"/>
/// <seealso cref="Zooper.Ant.DependencyInjection.AntOptions"/>
public sealed record PagingLimits(int DefaultSize = 20, int MaximumSize = 50, int MaximumDepth = 1000)
{
	/// <summary>The default bounds: default size 20, maximum size 50, maximum depth 1000.</summary>
	public static PagingLimits Default { get; } = new();

	/// <summary>Whether the bounds are consistent; see <see cref="Validate"/> for the rules.</summary>
	public bool IsValid => Problem is null;

	/// <summary>Throws when the bounds are inconsistent.</summary>
	/// <remarks>
	/// The rules are <c>1 ≤ DefaultSize ≤ MaximumSize ≤ MaximumDepth</c>. A maximum depth below the maximum size would
	/// refuse the first counted page of the largest size. The <see cref="Paging"/> factories call this on every
	/// instruction, which is cheap, so limits built by hand cannot slip through; <c>AddAnt</c> checks the same rules at
	/// startup.
	/// </remarks>
	/// <exception cref="PagingException">
	/// The default size is below one or above the maximum size, or the maximum depth is below the maximum size. The
	/// message names the bound and its value.
	/// </exception>
	public void Validate()
	{
		if (Problem is { } problem)
			throw new PagingException(problem);
	}

	/// <summary>
	/// The first broken rule as a message naming the bound, or <see langword="null"/> when the limits are consistent.
	/// Shared by <see cref="Validate"/> and the startup validation in <c>AddAnt</c>, so both report the same text.
	/// </summary>
	internal string? Problem =>
		DefaultSize < 1 ? $"PagingLimits.DefaultSize must be at least 1, but is {DefaultSize}."
		: DefaultSize > MaximumSize ? $"PagingLimits.DefaultSize ({DefaultSize}) must not exceed PagingLimits.MaximumSize ({MaximumSize})."
		: MaximumDepth < MaximumSize ? $"PagingLimits.MaximumDepth ({MaximumDepth}) must not be below PagingLimits.MaximumSize ({MaximumSize})."
		: null;
}
