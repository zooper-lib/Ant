using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Tokens;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies <see cref="Pager.TraverseAsync{TItem}"/> against a scripted page read: it follows tokens to the end,
/// fetches one page at a time, surfaces a failure once and unchanged, and never retries or reads ahead
/// (spec <c>paged-traversal</c>). A traversal is how sync jobs and exports read everything, so a skipped page or a
/// silent retry would corrupt their output.
/// </summary>
public class TraverseAsyncTests
{
	/// <summary>The pager under test. The protector is never used: the scripted reads build their pages directly.</summary>
	private readonly Pager _sut = new(Substitute.For<IPageTokenProtector>(), Options.Create(new AntOptions()));

	/// <summary>Every token the traversal passed to the page read, in order; its count is the number of reads.</summary>
	private readonly List<PageToken?> _requested = [];

	/// <summary>
	/// A read of three pages yields all six items in order with exactly three reads: the first without a token, each
	/// later one with the previous page's token. Only the token changes between reads.
	/// </summary>
	[Fact]
	public async Task TraverseAsync_OverThreePages_YieldsEveryItemInOrder_WithThreeReads()
	{
		// Act
		var items = await _sut.TraverseAsync(Script(Page(1, 2, "t1"), Page(3, 4, "t2"), Page(5, 6, null))).ToListAsync();

		// Assert
		items.ShouldBe([1, 2, 3, 4, 5, 6]);
		_requested.ShouldBe([null, PageToken.Parse("t1"), PageToken.Parse("t2")]);
	}

	/// <summary>A read matching nothing ends the sequence after a single read and yields no item.</summary>
	[Fact]
	public async Task TraverseAsync_OverAnEmptyRead_EndsAfterOneRead()
	{
		// Act
		// The cast passes one null token instead of a null params array.
		var items = await _sut.TraverseAsync(Script(Page((object?)null))).ToListAsync();

		// Assert
		items.ShouldBeEmpty();
		_requested.Count.ShouldBe(1);
	}

	/// <summary>
	/// The second page is read only when the consumer moves past the last item of the first, so the full result is
	/// never held in memory and a consumer that stops early pays for nothing more.
	/// </summary>
	[Fact]
	public async Task TraverseAsync_DoesNotReadTheSecondPage_UntilTheFirstPageIsConsumed()
	{
		// Arrange
		await using var enumerator = _sut.TraverseAsync(Script(Page(1, 2, "t1"), Page(3, null))).GetAsyncEnumerator();

		// Act & Assert
		(await enumerator.MoveNextAsync()).ShouldBeTrue();
		_requested.Count.ShouldBe(1);
		(await enumerator.MoveNextAsync()).ShouldBeTrue();
		_requested.Count.ShouldBe(1);
		(await enumerator.MoveNextAsync()).ShouldBeTrue();
		enumerator.Current.ShouldBe(3);
		_requested.Count.ShouldBe(2);
	}

	/// <summary>
	/// When the second page fails, the consumer gets the first page's items and then that same exception, once and
	/// unwrapped; no third page is read and the failed read is not retried.
	/// </summary>
	[Fact]
	public async Task TraverseAsync_WhenTheSecondPageFails_SurfacesThatErrorOnceAndReadsNoThirdPage()
	{
		// Arrange
		var failure = new TimeoutException("storage");
		var received = new List<int>();

		// Act
		var exception = await Should.ThrowAsync<TimeoutException>(async () =>
		{
			await foreach (var item in _sut.TraverseAsync(Script(Page(1, 2, "t1"), () => throw failure, Page(5, null))))
				received.Add(item);
		});

		// Assert
		exception.ShouldBeSameAs(failure);
		received.ShouldBe([1, 2]);
		_requested.Count.ShouldBe(2);
	}

	/// <summary>
	/// The traversal's cancellation token is handed to every page read, and once it is cancelled no further page is
	/// requested.
	/// </summary>
	[Fact]
	public async Task TraverseAsync_WhenCancelled_PassesTheTokenToThePendingReadAndReadsNoFurther()
	{
		// Arrange
		using var cancellation = new CancellationTokenSource();
		var observed = new List<CancellationToken>();

		// Act
		await Should.ThrowAsync<OperationCanceledException>(async () =>
		{
			// Every page claims more exists, so only the cancellation can stop this traversal.
			await foreach (var _ in _sut.TraverseAsync<int>((token, ct) =>
			{
				observed.Add(ct);
				return Task.FromResult<Page<int>>(new ContinuablePage<int>(2, PageToken.Parse("t"), [1]));
			}, cancellation.Token))
				await cancellation.CancelAsync();
		});

		// Assert
		observed.Count.ShouldBe(1);
		observed[0].IsCancellationRequested.ShouldBeTrue();
	}

	/// <summary>A consumer that stops enumerating after the first item causes no further page read.</summary>
	[Fact]
	public async Task TraverseAsync_WhenDisposedEarly_ReadsNoFurtherPage()
	{
		// Act
		await foreach (var _ in _sut.TraverseAsync(Script(Page(1, 2, "t1"), Page(3, null))))
			break;

		// Assert
		_requested.Count.ShouldBe(1);
	}

	/// <summary>
	/// A counted page has no token to follow, so returning one from the page read is a programming error: the
	/// traversal raises <see cref="InvalidOperationException"/> and reads nothing more.
	/// </summary>
	[Fact]
	public async Task TraverseAsync_WhenAPageIsCounted_RaisesInvalidOperationAndReadsNoFurther()
	{
		// Act & Assert
		await Should.ThrowAsync<InvalidOperationException>(async () =>
			await _sut.TraverseAsync(Script(() => new CountedPage<int>(1, 2, 4, [1, 2]), Page(3, null))).ToListAsync());

		_requested.Count.ShouldBe(1);
	}

	/// <summary>
	/// Builds a page read that records each token it receives in <see cref="_requested"/> and answers the n-th call
	/// with the n-th scripted page.
	/// </summary>
	private Func<PageToken?, CancellationToken, Task<Page<int>>> Script(params Func<Page<int>>[] pages) =>
		(token, _) =>
		{
			_requested.Add(token);

			return Task.FromResult(pages[_requested.Count - 1]());
		};

	/// <summary>
	/// Builds a continuable page from its items followed by its next token: a string becomes the token, null means the
	/// last page.
	/// </summary>
	private static Func<Page<int>> Page(params object?[] itemsThenToken) =>
		() => new ContinuablePage<int>(
			2,
			itemsThenToken[^1] is string token ? PageToken.Parse(token) : null,
			itemsThenToken[..^1].Cast<int>().ToList());
}
