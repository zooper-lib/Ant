using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zooper.Ant;

/// <summary>
/// The opaque continuation token of a continuable page. A caller receives it as
/// <see cref="ContinuablePage{TItem}.NextPageToken"/> and sends it back unchanged to get the next page.
/// </summary>
/// <remarks>
/// <para>
/// The text is the base64url form of an encrypted, integrity-protected payload: the position of the last served item
/// in the read's order, and a fingerprint of the read's result-defining arguments and order. It is produced and opened
/// by an <see cref="Zooper.Ant.Tokens.IPageTokenProtector"/> (by default the Data Protection one) on behalf of the
/// <see cref="Pager"/>. Callers must treat it as opaque: its contents may change between versions, and it has no
/// lifetime.
/// </para>
/// <para>
/// This struct validates structure only: the text is non-blank and uses the base64url alphabet
/// (<c>A–Z a–z 0–9 - _</c>) without <c>=</c> padding, so it is safe in a URL. That check runs at the boundary, before
/// any read. Whether the token was issued by this read, for these arguments (see <see cref="PageScope"/>), is checked
/// later when <see cref="Pager.ReadAsync{TItem}"/> opens it, and fails with <see cref="InvalidPageTokenException"/>.
/// A structurally valid token is not necessarily a valid one.
/// </para>
/// <para>
/// Absence is expressed by <see langword="null"/> on <c>PageToken?</c>, never by an empty token, which cannot be
/// parsed. <c>default(PageToken)</c> has a <see langword="null"/> <see cref="Value"/> and is not a usable token.
/// </para>
/// <para>
/// For hosts it binds and serialises as a plain string: <see cref="IParsable{TSelf}"/> lets ASP.NET Core minimal APIs
/// bind <c>PageToken?</c> from a query string, <see cref="PageTokenJsonConverter"/> writes and reads the raw string, and
/// <see cref="PageTokenTypeConverter"/> serves MVC model binding and other <see cref="TypeConverter"/> users. It is a
/// plain struct rather than a value-object library type so consumers inherit no extra dependency. Zooper.Ant.AspNetCore's
/// <c>PagingRequest</c> binds it as the <c>pageToken</c> query parameter.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Minimal API: a malformed token is a 400 before the handler runs.
/// app.MapGet("/players", (PageToken? pageToken, int? size, PlayerQueries queries, Pager pager, CancellationToken ct) =&gt;
///     queries.ListAsync(Paging.Continuable(pageToken, size, pager.Limits), ct));
///
/// // From any string, for example a message field:
/// if (PageToken.TryParse(message.Continuation, out var token))
///     paging = Paging.Continuable(token, 50);
/// </code>
/// </example>
/// <seealso cref="ContinuablePaging"/>
/// <seealso cref="Zooper.Ant.Tokens.IPageTokenProtector"/>
[JsonConverter(typeof(PageTokenJsonConverter))]
[TypeConverter(typeof(PageTokenTypeConverter))]
public readonly record struct PageToken : IParsable<PageToken>
{
	/// <summary>Wraps already validated text; only <see cref="Parse(string)"/> and <see cref="TryParse(string?, out PageToken)"/> call it.</summary>
	private PageToken(string value) => Value = value;

	/// <summary>The token text, base64url without padding. <see langword="null"/> only on <c>default(PageToken)</c>.</summary>
	public string Value { get; }

	/// <summary>Parses a token, refusing blank or non-base64url text.</summary>
	/// <param name="value">The token text as the caller sent it.</param>
	/// <returns>The token. Its content is not checked here.</returns>
	/// <exception cref="PagingException">
	/// The text is <see langword="null"/>, empty, whitespace, or contains a character outside the base64url alphabet
	/// (including <c>+</c>, <c>/</c> and <c>=</c> padding). The message says which.
	/// </exception>
	public static PageToken Parse(string value) =>
		Problem(value) is { } problem ? throw new PagingException(problem) : new PageToken(value);

	/// <summary>Tries to parse a token without throwing.</summary>
	/// <param name="value">The token text, possibly <see langword="null"/>.</param>
	/// <param name="token">The token when the text is valid; <c>default</c> otherwise.</param>
	/// <returns>Whether the text is a structurally valid token.</returns>
	public static bool TryParse([NotNullWhen(true)] string? value, out PageToken token)
	{
		token = Problem(value) is null ? new PageToken(value!) : default;

		// default(PageToken) has a null Value, so a null Value means the text was refused.
		return token.Value is not null;
	}

	/// <inheritdoc cref="Parse(string)"/>
	/// <remarks>Explicit <see cref="IParsable{TSelf}"/> member for host binding; the format provider is ignored.</remarks>
	static PageToken IParsable<PageToken>.Parse(string s, IFormatProvider? provider) => Parse(s);

	/// <inheritdoc cref="TryParse(string?, out PageToken)"/>
	/// <remarks>Explicit <see cref="IParsable{TSelf}"/> member for host binding; the format provider is ignored.</remarks>
	static bool IParsable<PageToken>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PageToken result) =>
		TryParse(s, out result);

	/// <summary>Returns the token text, so a token can be written into a URL or message as is.</summary>
	/// <returns><see cref="Value"/>.</returns>
	public override string ToString() => Value;

	/// <summary>Checks the structural rules shared by <see cref="Parse(string)"/> and <see cref="TryParse(string?, out PageToken)"/>.</summary>
	/// <param name="value">The candidate text.</param>
	/// <returns>The refusal message, or <see langword="null"/> when the text is valid.</returns>
	private static string? Problem(string? value) =>
		value switch
		{
			null => "PageToken cannot be null.",
			"" => "PageToken cannot be empty.",
			_ when string.IsNullOrWhiteSpace(value) => "PageToken cannot be whitespace.",
			// Only the URL-safe alphabet, so a token can travel in a query string unescaped; '=' padding is refused too.
			_ when !value.All(IsBase64UrlCharacter) => "PageToken must be base64url without padding.",
			_ => null
		};

	/// <summary>Whether a character belongs to the base64url alphabet: ASCII letters, digits, '-' and '_'.</summary>
	private static bool IsBase64UrlCharacter(char character) =>
		char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}

/// <summary>Reads and writes a <see cref="PageToken"/> as its raw JSON string, with no wrapping object.</summary>
/// <remarks>
/// Applied to <see cref="PageToken"/> by attribute, so it is used without registration. A <c>PageToken?</c> property
/// serialises as <c>null</c> when absent; <c>System.Text.Json</c> handles the nullable wrapper itself. A string that
/// is not a structurally valid token fails deserialisation with a <see cref="JsonException"/> that keeps the
/// <see cref="PagingException"/> as its inner exception, so a JSON body with a malformed token is rejected like any
/// other malformed JSON.
/// </remarks>
public sealed class PageTokenJsonConverter : JsonConverter<PageToken>
{
	/// <inheritdoc />
	/// <exception cref="JsonException">The string is not a structurally valid token.</exception>
	public override PageToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		try
		{
			return PageToken.Parse(reader.GetString()!);
		}
		catch (PagingException exception)
		{
			// Serializers expect JsonException for bad input; the paging message is kept for the caller.
			throw new JsonException(exception.Message, exception);
		}
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, PageToken value, JsonSerializerOptions options) =>
		writer.WriteStringValue(value.Value);
}

/// <summary>
/// Converts a <see cref="PageToken"/> from and to its raw string, for MVC query-string binding and other users of
/// <see cref="TypeDescriptor.GetConverter(Type)"/>.
/// </summary>
/// <remarks>
/// Applied to <see cref="PageToken"/> by attribute. Converting a malformed string throws the same
/// <see cref="PagingException"/> as <see cref="PageToken.Parse(string)"/>.
/// </remarks>
public sealed class PageTokenTypeConverter : TypeConverter
{
	/// <inheritdoc />
	/// <remarks>Only <see cref="string"/> sources are supported beyond the base converter's.</remarks>
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	/// <inheritdoc />
	/// <exception cref="PagingException">The string is not a structurally valid token.</exception>
	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
		value is string text ? PageToken.Parse(text) : base.ConvertFrom(context, culture, value);

	/// <inheritdoc />
	/// <remarks>A <see cref="PageToken"/> converts to its <see cref="PageToken.Value"/>.</remarks>
	public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
		value is PageToken token && destinationType == typeof(string) ? token.Value : base.ConvertTo(context, culture, value, destinationType);
}
