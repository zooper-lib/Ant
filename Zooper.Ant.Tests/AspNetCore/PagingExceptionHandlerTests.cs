using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;
using Xunit;
using Zooper.Ant.AspNetCore;

namespace Zooper.Ant.Tests.AspNetCore;

/// <summary>
/// Verifies <see cref="PagingExceptionHandler"/>: every paging refusal becomes a 400 problem with a stable
/// <c>type</c>, any other exception is declined, and nothing handles paging refusals unless the host opts in (spec
/// <c>http-paging-errors</c>).
/// </summary>
public class PagingExceptionHandlerTests
{
	/// <summary>Sends <paramref name="url"/> and reads the 400 problem body.</summary>
	private static async Task<ProblemDetails> ProblemAsync(TestHost host, string url)
	{
		var response = await host.Client.GetAsync(url);
		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
		return (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
	}

	/// <summary>An unrelated exception is declined without writing anything, so the host's other handlers decide.</summary>
	[Fact]
	public async Task TryHandleAsync_ForAnotherException_DeclinesAndLeavesTheResponse()
	{
		var problemDetails = Substitute.For<IProblemDetailsService>();
		var context = new DefaultHttpContext();

		var handled = await new PagingExceptionHandler(problemDetails).TryHandleAsync(context, new InvalidOperationException(), default);

		handled.ShouldBeFalse();
		context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
		await problemDetails.DidNotReceiveWithAnyArgs().TryWriteAsync(default!);
	}

	/// <summary>A foreign argument is a 400 <c>invalid-paging</c> problem whose detail names the argument and kind.</summary>
	[Fact]
	public async Task ForeignArgument_IsInvalidPaging()
	{
		await using var host = await TestHost.StartMinimalAsync();

		var problem = await ProblemAsync(host, "/players?paging=capped&cap=5&page=2");

		problem.Type.ShouldBe(PagingExceptionHandler.InvalidPagingType);
		problem.Title.ShouldBe("Invalid paging");
		problem.Detail.ShouldBe("Paging argument 'page' does not belong to capped paging.");
	}

	/// <summary>A counted page beyond the host's depth (500, not the built-in 1000) carries the core's depth message.</summary>
	[Fact]
	public async Task CountedPageBeyondTheHostDepth_IsInvalidPagingWithTheCoreMessage()
	{
		await using var host = await TestHost.StartMinimalAsync();

		var problem = await ProblemAsync(host, "/players?paging=counted&page=60&size=10");

		problem.Type.ShouldBe(PagingExceptionHandler.InvalidPagingType);
		problem.Detail.ShouldBe($"Paging cannot reach beyond {TestHost.Limits.MaximumDepth} rows; request continuable paging to go further.");
	}

	/// <summary>A structurally valid token the read did not issue binds, then the read refuses it as an invalid token.</summary>
	[Fact]
	public async Task ForeignToken_IsInvalidPageToken()
	{
		await using var host = await TestHost.StartMinimalAsync();

		var problem = await ProblemAsync(host, "/players?pageToken=abcd");

		problem.Type.ShouldBe(PagingExceptionHandler.InvalidPageTokenType);
		problem.Title.ShouldBe("Invalid page token");
		problem.Detail.ShouldBe(new InvalidPageTokenException().Message);
	}

	/// <summary>Without <c>AddAntExceptionHandler()</c> nothing maps paging refusals: the host's default answers 500.</summary>
	[Fact]
	public async Task WithoutTheRegistration_ForeignArgumentIs500()
	{
		await using var host = await TestHost.StartMinimalAsync(withExceptionHandler: false);

		var response = await host.Client.GetAsync("/players?paging=capped&cap=5&page=2");

		response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
	}
}
