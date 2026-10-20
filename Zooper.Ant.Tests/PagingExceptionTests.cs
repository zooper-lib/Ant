using Shouldly;
using Xunit;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies the shape of the paging error types (spec <c>paged-reads</c> and <c>continuation-tokens</c>): a host maps one
/// <see cref="PagingException"/> to one response, and can still single out an invalid token when it wants to.
/// </summary>
public class PagingExceptionTests
{
	/// <summary>
	/// An invalid token is a specialised paging error with a message, so a host that handles
	/// <see cref="PagingException"/> covers token refusals without extra code.
	/// </summary>
	[Fact]
	public void InvalidPageTokenException_IsAPagingException_WithAMessage()
	{
		// Act
		PagingException sut = new InvalidPageTokenException();

		// Assert
		sut.ShouldBeAssignableTo<PagingException>();
		sut.Message.ShouldNotBeNullOrWhiteSpace();
	}

	/// <summary>A paging error carries the message it was given, which names the bound or argument refused.</summary>
	[Fact]
	public void PagingException_CarriesItsMessage()
	{
		// Act
		var sut = new PagingException("Paging page must be at least 1.");

		// Assert
		sut.Message.ShouldBe("Paging page must be at least 1.");
	}
}
