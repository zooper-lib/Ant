using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Zooper.Ant.Sources;
using Zooper.Ant.Tokens;

namespace Zooper.Ant;

/// <summary>
/// The one set of JSON options for scope fingerprints and token payloads. Pinned explicitly rather than taken from the
/// defaults: changing any of them changes fingerprints and payloads, and so invalidates every token in flight.
/// </summary>
/// <remarks>
/// <para>Used by:</para>
/// <list type="bullet">
/// <item><description><see cref="PageScope.Bind"/>, to serialise <c>{"a":[values],"o":"signature"}</c> before hashing it into the fingerprint.</description></item>
/// <item><description><see cref="DataProtectionPageTokenProtector"/>, to write and read the payload <c>{"v","q","p"}</c>.</description></item>
/// <item><description>The LINQ source (<see cref="QueryablePageSource{T}"/>), to read position values back as the order's key types.</description></item>
/// </list>
/// <para>
/// Every setting that affects the bytes produced is spelled out, even where it equals today's default, so a future
/// change to <see cref="System.Text.Json"/> defaults cannot silently change fingerprints between package versions
/// (design, "Risks"). Changing a value here is a breaking change for tokens in flight and must be noted in the
/// CHANGELOG.
/// </para>
/// </remarks>
internal static class CanonicalJson
{
	/// <summary>The pinned options. Do not mutate; the instance is shared.</summary>
	public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General)
	{
		// Property names exactly as declared ("a", "o", "v", "q", "p").
		PropertyNamingPolicy = null,
		// No whitespace: the bytes are hashed, so formatting would be part of the fingerprint.
		WriteIndented = false,
		// Numbers as JSON numbers only; reading "1" for an int is refused, which keeps a payload's shape strict.
		NumberHandling = JsonNumberHandling.Strict,
		// Null values and properties are always written, never skipped, so the shape of the JSON never varies.
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		// The default encoder escapes the same characters on every runtime.
		Encoder = JavaScriptEncoder.Default,
		// Reflection-based metadata, needed for the anonymous type in PageScope.Bind and for runtime key types.
		TypeInfoResolver = new DefaultJsonTypeInfoResolver()
	};
}
