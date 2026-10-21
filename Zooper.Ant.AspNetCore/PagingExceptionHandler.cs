using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Zooper.Ant.AspNetCore;

/// <summary>
/// Answers every Zooper.Ant refusal with a 400 RFC 9457 problem response whose <c>type</c> tells a client which kind
/// of refusal it got, so a client sees why its paging request was rejected instead of a 500.
/// </summary>
/// <remarks>
/// <para>
/// Zooper.Ant reports every refusal as a <see cref="PagingException"/>: an argument of another kind
/// (<see cref="PagingRequestExtensions.ToPaging"/>), a page below one, a counted page beyond the depth bound, the
/// counted kind on a store that cannot count, and every token problem as the derived
/// <see cref="InvalidPageTokenException"/>. The response is:
/// <list type="table">
/// <listheader><term>Exception</term><description><c>type</c> / <c>title</c></description></listheader>
/// <item><term><see cref="InvalidPageTokenException"/></term><description><see cref="InvalidPageTokenType"/> /
/// <c>Invalid page token</c></description></item>
/// <item><term>any other <see cref="PagingException"/></term><description><see cref="InvalidPagingType"/> /
/// <c>Invalid paging</c></description></item>
/// </list>
/// The <c>detail</c> is the exception's message, which names the bound or argument refused. A token refusal always has
/// the same fixed message, so a caller cannot learn which check failed. A client branches on <c>type</c>, never on the
/// English title or detail: on <see cref="InvalidPageTokenType"/> it restarts from the first page.
/// </para>
/// <para>
/// Opt-in: registered only by <see cref="DependencyInjection.ServiceCollectionExtensions.AddAntExceptionHandler"/>, and
/// active only once the host calls <c>app.UseExceptionHandler()</c>, as for every <see cref="IExceptionHandler"/>. A
/// host with its own error convention (its own error code field, say) skips the registration and maps
/// <see cref="PagingException"/> itself. Any other exception is declined, so the host's remaining handlers decide.
/// </para>
/// </remarks>
/// <param name="problemDetails">Writes the problem response; registered by <c>AddProblemDetails()</c>.</param>
public sealed class PagingExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
	/// <summary>The problem <c>type</c> of every paging refusal other than a token refusal.</summary>
	public const string InvalidPagingType = "urn:zooper:ant:invalid-paging";

	/// <summary>The problem <c>type</c> of a refused continuation token (<see cref="InvalidPageTokenException"/>).</summary>
	public const string InvalidPageTokenType = "urn:zooper:ant:invalid-page-token";

	/// <summary>Writes a 400 problem response when <paramref name="exception"/> is a paging refusal.</summary>
	/// <param name="httpContext">The failed request.</param>
	/// <param name="exception">The exception the endpoint threw.</param>
	/// <param name="cancellationToken">The request's cancellation token.</param>
	/// <returns>
	/// <see langword="true"/> when the response was written; <see langword="false"/> for any other exception, without
	/// touching the response, so the next handler can try.
	/// </returns>
	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
	{
		if (exception is not PagingException paging)
			return false;

		var isToken = paging is InvalidPageTokenException;

		// A refusal is the client's fault (bad arguments or a bad token), never the server's.
		httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

		return await problemDetails.TryWriteAsync(new ProblemDetailsContext
		{
			HttpContext = httpContext,
			Exception = exception,
			ProblemDetails =
			{
				Status = StatusCodes.Status400BadRequest,
				Type = isToken ? InvalidPageTokenType : InvalidPagingType,
				Title = isToken ? "Invalid page token" : "Invalid paging",
				Detail = paging.Message
			}
		});
	}
}
