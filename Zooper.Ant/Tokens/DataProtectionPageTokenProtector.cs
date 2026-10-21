using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tokens;

/// <summary>
/// The default <see cref="IPageTokenProtector"/>: ASP.NET Core Data Protection encrypts and integrity-protects the
/// payload <c>{ v, q, p }</c> under the purpose <c>(PurposeRoot, readName)</c>, and the result is written as base64url.
/// </summary>
/// <remarks>
/// <para>The payload. Serialised with <see cref="CanonicalJson.Options"/> before encryption:</para>
/// <list type="table">
/// <listheader><term>Field</term><description>Meaning</description></listheader>
/// <item><term><c>v</c></term><description>Payload version, always 1. Reserved for the direction flag of a future backwards-paging change; any other value is refused.</description></item>
/// <item><term><c>q</c></term><description>The <see cref="BoundScope.Fingerprint"/> the token was issued under; compared on open.</description></item>
/// <item><term><c>p</c></term><description>The <see cref="PagePosition.Values"/> of the last served item; returned as JSON elements on open.</description></item>
/// </list>
/// <para>
/// The purpose chain. Each read gets its own protector, <c>CreateProtector(PurposeRoot, readName)</c>. Data
/// Protection derives a separate key per purpose, so a token of another read fails decryption before its fingerprint
/// is even read. <see cref="AntOptions.PurposeRoot"/> defaults to <c>"Zooper.Ant.PageToken"</c>; changing it
/// invalidates every token in flight.
/// </para>
/// <para>
/// One refusal. Every failure (a base64 format error, a cryptographic failure, malformed JSON, a missing field,
/// another version, another fingerprint) throws <see cref="InvalidPageTokenException"/> with the same fixed message,
/// so a caller cannot probe which check failed. The original exception, when there is one, is kept as the inner
/// exception for logs.
/// </para>
/// <para>
/// Keys. The protector holds no secret; the keys live in the host's Data Protection key ring. Tokens have no
/// lifetime (Data Protection's time-limited protector is deliberately not used), so a token stays valid until its key
/// is removed from the ring. A host with several instances must share one key ring (<c>PersistKeysToFileSystem</c>, a
/// database, Redis), otherwise a token issued by one instance is refused by another. <c>AddAnt</c> does not call
/// <c>AddDataProtection</c>; the host owns that configuration.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddDataProtection()
///     .PersistKeysToFileSystem(new DirectoryInfo("/shared/keys"));   // shared by every instance
/// services.AddAnt(options => options.PurposeRoot = "MyService.PageToken");
/// </code>
/// </example>
/// <param name="dataProtectionProvider">The host's Data Protection provider, registered by <c>AddDataProtection()</c>.</param>
/// <param name="options">Supplies <see cref="AntOptions.PurposeRoot"/>.</param>
/// <seealso cref="IPageTokenProtector"/>
/// <seealso cref="PageScope"/>
public sealed class DataProtectionPageTokenProtector(IDataProtectionProvider dataProtectionProvider, IOptions<AntOptions> options)
	: IPageTokenProtector
{
	// v is reserved for the direction flag of backwards paging; only 1 exists.
	private const int Version = 1;

	/// <inheritdoc />
	/// <remarks>
	/// Serialises <c>{ v: 1, q: fingerprint, p: position }</c>, encrypts it with the read's protector and encodes the
	/// bytes as base64url, so the token is safe in a URL query string without escaping.
	/// </remarks>
	public PageToken Protect(BoundScope scope, PagePosition position)
	{
		var payload = JsonSerializer.SerializeToUtf8Bytes(new TokenPayload(Version, scope.Fingerprint, position.Values), CanonicalJson.Options);

		return PageToken.Parse(Base64Url.EncodeToString(CreateProtector(scope.ReadName).Protect(payload)));
	}

	/// <inheritdoc />
	/// <remarks>
	/// Decodes and decrypts with the protector of <paramref name="scope"/>'s read, then checks the version, the presence
	/// of the position and the fingerprint. Any failure is the same <see cref="InvalidPageTokenException"/>.
	/// </remarks>
	public IReadOnlyList<JsonElement> Unprotect(BoundScope scope, PageToken pageToken)
	{
		TokenPayloadReadBack? payload;

		try
		{
			var protectedBytes = Base64Url.DecodeFromChars(pageToken.Value);
			payload = JsonSerializer.Deserialize<TokenPayloadReadBack>(CreateProtector(scope.ReadName).Unprotect(protectedBytes), CanonicalJson.Options);
		}
		// FormatException: not base64url. CryptographicException: altered, another read's purpose, or a key no longer
		// in the ring. JsonException: decrypted bytes that are not the payload.
		catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
		{
			throw new InvalidPageTokenException(exception);
		}

		// A well-formed payload of another version, without a position, or issued for other arguments or another
		// ordering (the fingerprint covers both) is refused like a cryptographic failure.
		return payload is { V: Version, P: not null } && payload.Q == scope.Fingerprint
			? payload.P
			: throw new InvalidPageTokenException();
	}

	/// <summary>The protector for one read: a separate key purpose under the configured root.</summary>
	/// <param name="readName">The read's name from its <see cref="BoundScope"/>.</param>
	/// <returns>The Data Protection protector for <c>(PurposeRoot, readName)</c>.</returns>
	private IDataProtector CreateProtector(string readName) =>
		dataProtectionProvider.CreateProtector(options.Value.PurposeRoot, readName);

	// Short member names keep the token small; the payload is never read outside this class.
	private sealed record TokenPayload(
		[property: JsonPropertyName("v")] int V,
		[property: JsonPropertyName("q")] string Q,
		[property: JsonPropertyName("p")] IReadOnlyList<object?> P);

	// The read-back shape is nullable everywhere so a payload with a missing field deserialises and is refused by the
	// check in Unprotect, rather than failing in an unexpected way.
	private sealed record TokenPayloadReadBack(
		[property: JsonPropertyName("v")] int? V,
		[property: JsonPropertyName("q")] string? Q,
		[property: JsonPropertyName("p")] IReadOnlyList<JsonElement>? P);
}
