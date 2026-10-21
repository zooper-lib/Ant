using System.ComponentModel;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies the structural check of <see cref="PageToken"/> (spec <c>continuation-tokens</c>, "A token value is validated
/// structurally at the boundary"): non-blank base64url without padding is accepted, anything else is refused before any
/// read runs. Also covers the JSON and type converters a host uses to bind a token.
/// </summary>
public class PageTokenTests
{
	/// <summary>Base64url text, the form every issued token has, is accepted unchanged.</summary>
	[Fact]
	public void Parse_WhenValueIsBase64Url_Succeeds()
	{
		// Act
		var sut = PageToken.Parse("CfDJ8Abc-123_xyz");

		// Assert
		sut.Value.ShouldBe("CfDJ8Abc-123_xyz");
	}

	/// <summary>
	/// Blank and malformed text is refused by both <c>Parse</c> and <c>TryParse</c>. The cases: empty, whitespace, the
	/// standard-base64 characters <c>+</c> and <c>/</c>, padding, and a space.
	/// </summary>
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("abc+def")]
	[InlineData("abc/def")]
	[InlineData("abcdef==")]
	[InlineData("abc def")]
	public void Parse_WhenValueIsBlankOrNotBase64Url_Throws(string value)
	{
		// Act & Assert
		Should.Throw<PagingException>(() => PageToken.Parse(value));
		PageToken.TryParse(value, out _).ShouldBeFalse();
	}

	/// <summary>A missing value is not a token; absence is expressed by a null <c>PageToken?</c>, never an empty token.</summary>
	[Fact]
	public void TryParse_WhenValueIsNull_ReturnsFalse()
	{
		// Act & Assert
		PageToken.TryParse(null, out _).ShouldBeFalse();
	}

	/// <summary>A token is written to JSON as its raw string and read back equal; a JSON null reads as no token.</summary>
	[Fact]
	public void Json_RoundTripsAsTheRawString()
	{
		// Arrange
		var token = PageToken.Parse("abc-_1");

		// Act
		var json = JsonSerializer.Serialize<PageToken?>(token);

		// Assert
		json.ShouldBe("\"abc-_1\"");
		JsonSerializer.Deserialize<PageToken?>(json).ShouldBe(token);
		JsonSerializer.Deserialize<PageToken?>("null").ShouldBeNull();
	}

	/// <summary>Malformed token text in a JSON body fails as a JSON error, which hosts already answer as a bad request.</summary>
	[Fact]
	public void Json_WhenStringIsNotBase64Url_Throws()
	{
		// Act & Assert
		Should.Throw<JsonException>(() => JsonSerializer.Deserialize<PageToken>("\"a+b\""));
	}

	/// <summary>The type converter, used for query-string binding, accepts valid text and refuses malformed text.</summary>
	[Fact]
	public void TypeConverter_ConvertsFromString()
	{
		// Arrange
		var converter = TypeDescriptor.GetConverter(typeof(PageToken));

		// Act & Assert
		converter.ConvertFromString("abc-_1").ShouldBe(PageToken.Parse("abc-_1"));
		Should.Throw<PagingException>(() => converter.ConvertFromString("a+b"));
	}
}
