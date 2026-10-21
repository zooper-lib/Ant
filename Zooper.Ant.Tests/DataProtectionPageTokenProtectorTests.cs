using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Sources;
using Zooper.Ant.Tokens;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies <see cref="DataProtectionPageTokenProtector"/> (spec <c>continuation-tokens</c>): it round-trips a position,
/// and refuses a token that was altered, issued by another read or issued for other arguments, but never because of its
/// age. Every refusal carries the same message, so a caller cannot learn which check failed.
/// </summary>
public class DataProtectionPageTokenProtectorTests
{
	/// <summary>The Data Protection purpose root the protector under test is configured with.</summary>
	private const string PurposeRoot = "Zooper.Ant.PageToken";

	/// <summary>The read and fingerprint tokens are issued under, unless a test changes one of them.</summary>
	private static readonly BoundScope Scope = new("SearchClubRelationshipsAsync", "fingerprint-a");

	/// <summary>A position mixing a string, a null, a number and an identifier, as an order chain produces.</summary>
	private static readonly PagePosition Position = new(["ada", null, 0.75, "3f2b"]);

	/// <summary>An in-memory key ring, shared by every protector a test creates, as instances share one key ring.</summary>
	private readonly IDataProtectionProvider _provider = new EphemeralDataProtectionProvider();

	private readonly DataProtectionPageTokenProtector _sut;

	public DataProtectionPageTokenProtectorTests() => _sut = Create(_provider);

	/// <summary>A token returned unchanged opens to the same position values, in order, with their JSON types intact.</summary>
	[Fact]
	public void Unprotect_WhenTokenIsUnchanged_ReturnsThePosition()
	{
		// Arrange
		var token = _sut.Protect(Scope, Position);

		// Act
		var position = _sut.Unprotect(Scope, token);

		// Assert
		position.Count.ShouldBe(4);
		position[0].GetString().ShouldBe("ada");
		position[1].ValueKind.ShouldBe(JsonValueKind.Null);
		position[2].GetDouble().ShouldBe(0.75);
		position[3].GetString().ShouldBe("3f2b");
	}

	/// <summary>Changing a single character makes the token fail its integrity check and be refused.</summary>
	[Fact]
	public void Unprotect_WhenOneCharacterIsAltered_Throws()
	{
		// Arrange
		var value = _sut.Protect(Scope, Position).Value;
		var middle = value.Length / 2;
		// Alter a character in the middle: the last characters can carry unused padding bits that decode alike.
		var altered = PageToken.Parse(value[..middle] + (value[middle] == 'A' ? 'B' : 'A') + value[(middle + 1)..]);

		// Act & Assert
		Should.Throw<InvalidPageTokenException>(() => _sut.Unprotect(Scope, altered));
	}

	/// <summary>A token issued by another read is refused, because the read name is part of the Data Protection purpose.</summary>
	[Fact]
	public void Unprotect_WhenTokenWasIssuedByAnotherRead_Throws()
	{
		// Arrange
		var token = _sut.Protect(Scope with { ReadName = "SearchClubsAsync" }, Position);

		// Act & Assert
		Should.Throw<InvalidPageTokenException>(() => _sut.Unprotect(Scope, token));
	}

	/// <summary>A token issued for other arguments is refused, because the fingerprint inside it no longer matches.</summary>
	[Fact]
	public void Unprotect_WhenFingerprintDiffers_Throws()
	{
		// Arrange
		var token = _sut.Protect(Scope, Position);

		// Act & Assert
		Should.Throw<InvalidPageTokenException>(() => _sut.Unprotect(Scope with { Fingerprint = "fingerprint-b" }, token));
	}

	/// <summary>
	/// A token outlives the protector that issued it: another instance over the same key ring opens it, as after a
	/// restart, and nothing about its age is checked.
	/// </summary>
	[Fact]
	public void Unprotect_WhenTokenWasIssuedByAnEarlierProtector_Succeeds()
	{
		// No time limit: a token outlives the protector instance (and request) that issued it, as after a restart with
		// the same key ring.

		// Arrange
		var token = Create(_provider).Protect(Scope, Position);

		// Act & Assert
		Should.NotThrow(() => _sut.Unprotect(Scope, token));
	}

	/// <summary>Well-formed base64url that the service never issued passes the structural check but is refused here.</summary>
	[Fact]
	public void Unprotect_WhenTokenIsNotProtectedData_Throws()
	{
		// Act & Assert
		Should.Throw<InvalidPageTokenException>(() => _sut.Unprotect(Scope, PageToken.Parse("bm90LWEtdG9rZW4")));
	}

	/// <summary>
	/// A payload of version 2 is refused: the version is reserved for backwards paging, which this release does not
	/// read.
	/// </summary>
	[Fact]
	public void Unprotect_WhenVersionIsTwo_Throws()
	{
		// Arrange
		var token = ProtectRaw(new { v = 2, q = Scope.Fingerprint, p = new[] { "ada" } });

		// Act & Assert
		Should.Throw<InvalidPageTokenException>(() => _sut.Unprotect(Scope, token));
	}

	/// <summary>A genuine, correctly bound payload without a position is refused rather than read as an empty position.</summary>
	[Fact]
	public void Unprotect_WhenPositionIsMissing_Throws()
	{
		// Arrange
		var token = ProtectRaw(new { v = 1, q = Scope.Fingerprint });

		// Act & Assert
		Should.Throw<InvalidPageTokenException>(() => _sut.Unprotect(Scope, token));
	}

	/// <summary>A foreign token and garbage get the same message, so the error does not reveal which check failed.</summary>
	[Fact]
	public void Unprotect_EveryRefusal_HasTheSameMessage()
	{
		// Act
		var foreign = Should.Throw<InvalidPageTokenException>(() => _sut.Unprotect(Scope, _sut.Protect(Scope with { ReadName = "Other" }, Position)));
		var garbage = Should.Throw<InvalidPageTokenException>(() => _sut.Unprotect(Scope, PageToken.Parse("abc")));

		// Assert
		foreign.Message.ShouldBe(garbage.Message);
	}

	/// <summary>
	/// The token is protected under the purpose (root, read name) and its payload carries version 1, which pins the
	/// format other instances and future versions rely on.
	/// </summary>
	[Fact]
	public void Protect_UsesThePurposeRootAndTheReadName()
	{
		// Arrange
		var token = _sut.Protect(Scope, Position);

		// Act
		var bytes = _provider.CreateProtector(PurposeRoot, Scope.ReadName).Unprotect(Base64Url.DecodeFromChars(token.Value));

		// Assert
		JsonDocument.Parse(bytes).RootElement.GetProperty("v").GetInt32().ShouldBe(1);
	}

	/// <summary>Creates a protector over <paramref name="provider"/> with the test's purpose root.</summary>
	private static DataProtectionPageTokenProtector Create(IDataProtectionProvider provider) =>
		new(provider, Options.Create(new AntOptions { PurposeRoot = PurposeRoot }));

	/// <summary>
	/// Protects a hand-written payload under the same purpose, so a test can produce a genuine token whose contents the
	/// protector would never write itself.
	/// </summary>
	private PageToken ProtectRaw(object payload) =>
		PageToken.Parse(Base64Url.EncodeToString(
			_provider.CreateProtector(PurposeRoot, Scope.ReadName).Protect(JsonSerializer.SerializeToUtf8Bytes(payload))));
}
