using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zooper.Ant.Sources;
using Zooper.Ant.Tokens;

namespace Zooper.Ant.Tests.Contract;

/// <summary>
/// A test-only, unencrypted <see cref="IPageTokenProtector"/>: the token is the base64url of the JSON payload
/// <c>{ v, r, q, p }</c> (version, read name, fingerprint, position). Tests use it to look inside a token (was the
/// bookmark null?) and to forge one with a position of the wrong shape. It refuses a foreign read, a changed fingerprint,
/// an unknown version and a malformed payload like the real protector, but offers no protection against tampering, so it
/// lives only in this test project.
/// </summary>
public sealed class PlainTextPageTokenProtector : IPageTokenProtector
{
	/// <inheritdoc />
	public PageToken Protect(BoundScope scope, PagePosition position) =>
		Write(new Payload(1, scope.ReadName, scope.Fingerprint, position.Values));

	/// <inheritdoc />
	/// <remarks>Without encryption the read name travels in the payload (<c>r</c>) instead of the protection purpose.</remarks>
	public IReadOnlyList<JsonElement> Unprotect(BoundScope scope, PageToken pageToken)
	{
		PayloadReadBack? payload;

		try
		{
			payload = JsonSerializer.Deserialize<PayloadReadBack>(Base64Url.DecodeFromChars(pageToken.Value));
		}
		catch (Exception exception) when (exception is FormatException or JsonException)
		{
			throw new InvalidPageTokenException(exception);
		}

		return payload is { V: 1, P: not null } && payload.R == scope.ReadName && payload.Q == scope.Fingerprint
			? payload.P
			: throw new InvalidPageTokenException();
	}

	/// <summary>Reads the position values out of a token without checking it.</summary>
	/// <param name="pageToken">A token this protector issued.</param>
	/// <returns>The JSON of the payload.</returns>
	public static JsonElement Read(PageToken pageToken) =>
		JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(pageToken.Value));

	/// <summary>Writes a token from a payload, as a test that alters a token needs.</summary>
	/// <param name="payload">The payload object, serialised as JSON.</param>
	/// <returns>The token.</returns>
	public static PageToken Write(object payload) =>
		PageToken.Parse(Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload)));

	// Same short member names as the Data Protection payload, plus r for the read name.
	private sealed record Payload(
		[property: JsonPropertyName("v")] int V,
		[property: JsonPropertyName("r")] string R,
		[property: JsonPropertyName("q")] string Q,
		[property: JsonPropertyName("p")] IReadOnlyList<object?> P);

	private sealed record PayloadReadBack(
		[property: JsonPropertyName("v")] int? V,
		[property: JsonPropertyName("r")] string? R,
		[property: JsonPropertyName("q")] string? Q,
		[property: JsonPropertyName("p")] IReadOnlyList<JsonElement>? P);
}
