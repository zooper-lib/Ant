using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Zooper.Ant.Sources;
using Zooper.Ant.Tokens;

namespace Zooper.Ant;

/// <summary>
/// What a continuation token is bound to: the read that issues it and the values that define its result set. A token
/// issued under one scope is refused under any other, so a read never continues with other filters than it started with.
/// </summary>
/// <remarks>
/// <para>
/// Why a scope. A token carries a position in an ordering, such as "after rating 91, id 9". Presented to another
/// read, or to the same read with another filter, that position would be applied to a different result set and serve a
/// wrong page that looks right. Binding the token to the read and its arguments turns that into an
/// <see cref="InvalidPageTokenException"/> instead (spec <c>continuation-tokens</c>).
/// </para>
/// <para>
/// What to put in it. Every result-defining argument of the read: filters, identifier sets, search terms,
/// locations. Not the page size: it may change between pages. Not the ordering: <see cref="Pager"/> folds the source's
/// <see cref="IPageSource{TItem}.OrderSignature"/> in through <see cref="Bind"/>. Pass scalars and records: values bind
/// alike exactly when they serialise to the same JSON, so a value with unstable serialisation (a dictionary in insertion
/// order) refuses tokens unexpectedly.
/// </para>
/// <para>
/// How it binds. The paging system, not the read, computes the binding (spec <c>continuation-tokens</c>, "The
/// service, not the read, binds a token to its arguments"). <see cref="Bind"/> serialises the values and the order
/// signature with the pinned <see cref="CanonicalJson"/> options and hashes the result into a
/// <see cref="BoundScope.Fingerprint"/>. The <see cref="ReadName"/> is not hashed: the
/// <see cref="DataProtectionPageTokenProtector"/> uses it as part of the Data Protection purpose, so a token of another
/// read cannot even be decrypted.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Inside PlayerQueries.ListAsync(string? team, int? minRating, Paging paging, ...)
/// var scope = PageScope.For(nameof(ListAsync), team, minRating);
/// </code>
/// </example>
/// <seealso cref="BoundScope"/>
/// <seealso cref="IPageTokenProtector"/>
public sealed class PageScope
{
	// Kept as given; serialised only when a continuable page is read (Bind).
	private readonly object?[] _values;

	private PageScope(string readName, object?[] values) => (ReadName, _values) = (readName, values);

	/// <summary>The read that issues and accepts the token, as given to <see cref="For"/>.</summary>
	public string ReadName { get; }

	/// <summary>Creates the scope of a read.</summary>
	/// <param name="readName">
	/// A name unique to the read within the host, such as <c>nameof(ListAsync)</c>. Two reads with the same name and
	/// equal values would accept each other's tokens.
	/// </param>
	/// <param name="values">
	/// The read's result-defining arguments, in a fixed order. <see langword="null"/> values are allowed and bind as
	/// JSON <c>null</c>. Passing a single <see langword="null"/> array (<c>PageScope.For("Read", null)</c>) is taken as
	/// one <see langword="null"/> value.
	/// </param>
	/// <returns>The scope.</returns>
	/// <exception cref="ArgumentException"><paramref name="readName"/> is <see langword="null"/>, empty or whitespace.</exception>
	public static PageScope For(string readName, params object?[]? values)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(readName);

		// C# binds For("Read", null) to a null array rather than to an array holding null; treat it as the latter,
		// which is what the caller meant.
		return new PageScope(readName, values ?? [null]);
	}

	/// <summary>Binds the scope to the ordering a source applies.</summary>
	/// <remarks>
	/// <para>
	/// The fingerprint is <c>Base64Url(SHA256(UTF8(json)))</c> where <c>json</c> is <c>{"a":[values],"o":"signature"}</c>
	/// serialised with <see cref="CanonicalJson.Options"/>. JSON serialisation is culture-invariant, so a token issued
	/// under one thread culture opens under another. Hashing keeps the token's size independent of the size of the
	/// arguments, and keeps the argument values out of the token.
	/// </para>
	/// <para>It is computed on every continuable read (one small serialisation and one hash) and not cached.</para>
	/// </remarks>
	/// <param name="orderSignature">The source's <see cref="IPageSource{TItem}.OrderSignature"/>; empty for a native-cursor source.</param>
	/// <returns>The read name and the fingerprint of the values and the ordering.</returns>
	public BoundScope Bind(string orderSignature)
	{
		// "a" and "o" are short on purpose and pinned: renaming them changes every fingerprint and refuses every token
		// in flight.
		var json = JsonSerializer.SerializeToUtf8Bytes(new { a = _values, o = orderSignature }, CanonicalJson.Options);

		return new BoundScope(ReadName, Base64Url.EncodeToString(SHA256.HashData(json)));
	}
}

/// <summary>
/// A <see cref="PageScope"/> bound to an ordering by <see cref="PageScope.Bind"/>: what an
/// <see cref="IPageTokenProtector"/> writes into a token and checks a returned token against.
/// </summary>
/// <remarks>
/// A protector must refuse a token whose <see cref="ReadName"/> or <see cref="Fingerprint"/> differs from the scope it
/// is opened under. The default <see cref="DataProtectionPageTokenProtector"/> uses the read name as part of the Data
/// Protection purpose and stores the fingerprint in the encrypted payload.
/// </remarks>
/// <param name="ReadName">The read that issues and accepts the token.</param>
/// <param name="Fingerprint">
/// The base64url SHA-256 of the read's result-defining values and its ordering signature.
/// </param>
public sealed record BoundScope(string ReadName, string Fingerprint);
