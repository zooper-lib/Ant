using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using Xunit;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies that <see cref="IPageSource{TItem}"/> is a plain interface any store can implement, here by a mock
/// (spec <c>page-sources</c>). If this stops compiling, every hand-written store source breaks with it.
/// </summary>
public class PageSourceContractShapeTests
{
	/// <summary>
	/// A mocked source answers reads and positions like a real one, and an unconfigured member returns its default.
	/// The floor needs nothing beyond its five members.
	/// </summary>
	[Fact]
	public async Task IPageSource_CanBeImplementedByAMock()
	{
		// Arrange
		var sut = Substitute.For<IPageSource<string>>();
		sut.ReadAsync(0, 2, Arg.Any<CancellationToken>()).Returns(["a", "b"]);
		sut.PositionOf("b").Returns(new PagePosition(["b"]));

		// Act & Assert
		(await sut.ReadAsync(0, 2, CancellationToken.None)).ShouldBe(["a", "b"]);
		sut.PositionOf("b").Values.ShouldBe(["b"]);
		sut.ParsePosition(new List<JsonElement>()).ShouldBeNull();
	}
}
