using System;
using System.Globalization;
using Shouldly;
using Xunit;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies the token binding built by <see cref="PageScope"/> (spec <c>continuation-tokens</c>, "The service, not the
/// read, binds a token to its arguments"): the fingerprint is independent of culture, changes when a declared value or
/// the order changes, and does not depend on the page size.
/// </summary>
public class PageScopeTests
{
	/// <summary>A timestamp with milliseconds, whose text form differs between cultures.</summary>
	private static readonly DateTime Date = new(2026, 10, 20, 13, 45, 7, 123, DateTimeKind.Utc);

	/// <summary>
	/// A token issued under one thread culture is accepted under another: the same values give the same fingerprint in
	/// German and in Saudi Arabic, whose number and date formats differ.
	/// </summary>
	[Fact]
	public void Bind_UnderTwoCultures_GivesTheSameFingerprint()
	{
		// Arrange
		var fingerprints = new string[2];
		var original = CultureInfo.CurrentCulture;

		// Act
		try
		{
			string[] cultures = ["de-DE", "ar-SA"];

			for (var i = 0; i < cultures.Length; i++)
			{
				CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(cultures[i]);
				fingerprints[i] = PageScope.For("SearchAsync", 1.5, Date, "ada").Bind("o").Fingerprint;
			}
		}
		finally
		{
			// Restore the culture so other tests on this thread are unaffected.
			CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = original;
		}

		// Assert
		fingerprints[0].ShouldBe(fingerprints[1]);
	}

	/// <summary>A changed filter value gives another fingerprint, so a token issued for one date is refused for another.</summary>
	[Fact]
	public void Bind_WhenAValueChanges_GivesAnotherFingerprint()
	{
		// Arrange
		var sut = PageScope.For("SearchAsync", Date, "ada").Bind("o");

		// Act & Assert
		PageScope.For("SearchAsync", Date.AddDays(1), "ada").Bind("o").Fingerprint.ShouldNotBe(sut.Fingerprint);
	}

	/// <summary>A changed order gives another fingerprint, so a token issued under one order is refused under another.</summary>
	[Fact]
	public void Bind_WhenTheOrderSignatureChanges_GivesAnotherFingerprint()
	{
		// Arrange
		var scope = PageScope.For("SearchAsync", "ada");

		// Act & Assert
		scope.Bind("Name asc").Fingerprint.ShouldNotBe(scope.Bind("Name desc").Fingerprint);
	}

	/// <summary>The bound scope keeps the read name, and the same arguments always bind alike.</summary>
	[Fact]
	public void Bind_KeepsTheReadName_AndTakesNoSize()
	{
		// Size is not an input: For takes only the read name and its result-defining values, so two pages of different
		// sizes bind alike.

		// Act
		var sut = PageScope.For("SearchAsync", "ada").Bind("o");

		// Assert
		sut.ReadName.ShouldBe("SearchAsync");
		PageScope.For("SearchAsync", "ada").Bind("o").ShouldBe(sut);
	}

	/// <summary>A blank read name is refused, because it would let unrelated reads accept each other's tokens.</summary>
	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	public void For_WhenReadNameIsBlank_Throws(string readName)
	{
		// Act & Assert
		Should.Throw<ArgumentException>(() => PageScope.For(readName));
	}
}
