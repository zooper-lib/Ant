using Zooper.Ant.Tokens;

namespace Zooper.Ant.DependencyInjection;

/// <summary>Host configuration of Zooper.Ant, set through <see cref="ServiceCollectionExtensions.AddAnt"/>.</summary>
/// <remarks>
/// <para>
/// Bound with the options pattern, so it can be configured in code (<c>AddAnt(options => ...)</c>) or with any other
/// <c>Configure&lt;AntOptions&gt;</c> source. The values are validated when the host starts (<c>ValidateOnStart</c>):
/// inconsistent <see cref="Limits"/> or a blank <see cref="PurposeRoot"/> fail startup with a message naming the
/// value, instead of failing the first request.
/// </para>
/// <para>
/// <see cref="Pager"/> exposes <see cref="Limits"/> as <see cref="Pager.Limits"/>; the
/// <see cref="DataProtectionPageTokenProtector"/> reads <see cref="PurposeRoot"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddAnt(options =>
/// {
///     options.Limits = new PagingLimits(DefaultSize: 25, MaximumSize: 100, MaximumDepth: 5000);
///     options.PurposeRoot = "MyService.PageToken";
/// });
/// </code>
/// </example>
public sealed class AntOptions
{
	/// <summary>The bounds every paging instruction built from <see cref="Pager.Limits"/> obeys.</summary>
	/// <remarks>
	/// Defaults to <see cref="PagingLimits.Default"/> (size 20, maximum 50, counted depth 1000). Must satisfy
	/// <see cref="PagingLimits.IsValid"/>, or the host fails at startup.
	/// </remarks>
	public PagingLimits Limits { get; set; } = PagingLimits.Default;

	/// <summary>
	/// The root Data Protection purpose of page tokens; each read adds its name below it. Changing it invalidates every
	/// token in flight.
	/// </summary>
	/// <remarks>
	/// Defaults to <c>"Zooper.Ant.PageToken"</c>. Set it per application when several applications share one Data
	/// Protection key ring and must not accept each other's tokens. Must not be blank.
	/// </remarks>
	public string PurposeRoot { get; set; } = "Zooper.Ant.PageToken";
}
