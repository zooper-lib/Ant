using Microsoft.AspNetCore.Diagnostics;
using Zooper.Ant;

namespace Zooper.Ant.Example;

/// <summary>
/// Turns every Zooper.Ant refusal into a 400 problem response, so a client sees why its paging request was rejected
/// instead of a 500.
/// </summary>
/// <remarks>
/// <para>
/// Zooper.Ant reports every refusal with one exception type, <see cref="PagingException"/>: a page below one, a counted
/// page beyond the depth limit, an argument of another kind (see <see cref="PagingQuery.ToPaging"/>), the counted kind
/// on a store that cannot count, and every token problem. <see cref="InvalidPageTokenException"/> derives from it, so
/// this one handler covers both; it is told apart here only to give token refusals their own title. The detail is the
/// exception's message, which names the bound or argument refused; a token refusal always has the same fixed message,
/// so a caller cannot learn which check failed.
/// </para>
/// <para>
/// Registered with <c>AddExceptionHandler&lt;PagingExceptionHandler&gt;()</c> and activated by
/// <c>UseExceptionHandler()</c> in Program.cs. Any other exception is left to the default handler (a 500). The planned
/// Zooper.Ant.AspNetCore package will ship an equivalent handler.
/// </para>
/// </remarks>
/// <param name="problemDetails">Writes RFC 9457 problem responses; registered by <c>AddProblemDetails()</c>.</param>
public sealed class PagingExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
	/// <summary>Writes a 400 problem response when <paramref name="exception"/> is a paging refusal.</summary>
	/// <param name="httpContext">The failed request.</param>
	/// <param name="exception">The exception the endpoint threw.</param>
	/// <param name="ct">The request's cancellation token.</param>
	/// <returns><see langword="true"/> when the response was written; <see langword="false"/> to let the next handler try.</returns>
	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
	{
		// Not a paging refusal: leave it to the default handling.
		if (exception is not PagingException paging)
			return false;

		// A refusal is the client's fault (bad arguments or a bad token), never the server's.
		httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

		return await problemDetails.TryWriteAsync(new ProblemDetailsContext
		{
			HttpContext = httpContext,
			Exception = exception,
			ProblemDetails =
			{
				Status = StatusCodes.Status400BadRequest,
				Title = paging is InvalidPageTokenException ? "Invalid page token" : "Invalid paging",
				Detail = paging.Message
			}
		});
	}
}
