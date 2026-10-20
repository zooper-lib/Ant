using System.Collections.Generic;
using System.Text.Json;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tokens;

/// <summary>
/// Protects a page position as an opaque <see cref="PageToken"/>, and opens it again only for the same read and the
/// same result-defining arguments. A token carries no lifetime.
/// </summary>
/// <remarks>
/// <para>
/// <b>Role.</b> <see cref="Pager"/> calls <see cref="Protect"/> when it issues the next token of a continuable page and
/// <see cref="Unprotect"/> when a caller returns one. The position is the source's own (<see cref="PagePosition"/>);
/// the protector serialises it, never interprets it, and hands the values back as JSON for the source's
/// <see cref="IPageSource{TItem}.ParsePosition"/> to read.
/// </para>
/// <para>
/// <b>What an implementation must guarantee</b> (spec <c>continuation-tokens</c>):
/// </para>
/// <list type="bullet">
/// <item><description>The payload is encrypted and integrity-protected: a caller can neither read nor alter it.</description></item>
/// <item><description>A token is refused when opened under another <see cref="BoundScope.ReadName"/> or another <see cref="BoundScope.Fingerprint"/>.</description></item>
/// <item><description>Every refusal throws <see cref="InvalidPageTokenException"/>, never another exception type.</description></item>
/// <item><description>No lifetime: a token is never refused because of its age, and stays readable across restarts and between instances that share the keys.</description></item>
/// </list>
/// <para>
/// <b>Default and replacement.</b> <c>services.AddAnt()</c> registers <see cref="DataProtectionPageTokenProtector"/>
/// with <c>TryAddSingleton</c>, so an implementation registered before <c>AddAnt</c> wins, for example an HMAC protector
/// or one backed by a key-management service. Every read then issues and accepts tokens through it with no change to the
/// reads.
/// </para>
/// </remarks>
/// <example>
/// Replacing the protector:
/// <code>
/// services.AddSingleton&lt;IPageTokenProtector, MyHmacPageTokenProtector&gt;();
/// services.AddAnt();   // keeps MyHmacPageTokenProtector
/// </code>
/// </example>
/// <seealso cref="DataProtectionPageTokenProtector"/>
/// <seealso cref="ServiceCollectionExtensions.AddAnt"/>
public interface IPageTokenProtector
{
	/// <summary>Protects a position as a token.</summary>
	/// <param name="scope">The issuing read and its fingerprint, to be checked again by <see cref="Unprotect"/>.</param>
	/// <param name="position">
	/// The position of the last served item, as given by <see cref="IPageSource{TItem}.PositionOf"/>. Its values must be
	/// JSON-serialisable.
	/// </param>
	/// <returns>The opaque token, base64url text as <see cref="PageToken.Parse"/> accepts.</returns>
	PageToken Protect(BoundScope scope, PagePosition position);

	/// <summary>Opens a token issued under the same <paramref name="scope"/>.</summary>
	/// <param name="scope">The read opening the token and the fingerprint of the current request.</param>
	/// <param name="pageToken">The token the caller returned.</param>
	/// <returns>
	/// The position values in the order they were protected, as JSON elements; the source converts them back to its own
	/// types in <see cref="IPageSource{TItem}.ParsePosition"/>.
	/// </returns>
	/// <exception cref="InvalidPageTokenException">
	/// The token was altered, issued by another read or for other arguments, or cannot be read with the available keys.
	/// </exception>
	IReadOnlyList<JsonElement> Unprotect(BoundScope scope, PageToken pageToken);
}
