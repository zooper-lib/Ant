using System;

namespace Zooper.Ant;

/// <summary>
/// A paging refusal: the request asked for something the paging rules do not allow. The one exception type a host
/// maps to its "bad request" answer for every paged read.
/// </summary>
/// <remarks>
/// <para>
/// Every refusal in Zooper.Ant is raised as this type or its specialisation, never as a store- or read-specific
/// exception, so the same invalid arguments produce the same error on every read. It is raised for:
/// <list type="bullet">
/// <item><description>a counted page below one, or deeper than <see cref="PagingLimits.MaximumDepth"/>
/// (<see cref="Paging.Counted(int?, int?, PagingLimits)"/>);</description></item>
/// <item><description>a blank or non-base64url token text (<see cref="PageToken.Parse(string)"/>);</description></item>
/// <item><description>inconsistent <see cref="PagingLimits"/> (<see cref="PagingLimits.Validate"/>);</description></item>
/// <item><description>the counted kind on a source that cannot count or skip (<see cref="Pager.ReadAsync{TItem}"/>);</description></item>
/// <item><description>a refused token, as <see cref="InvalidPageTokenException"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// The message names the bound or argument refused and is safe to show to a caller. Zooper.Ant has no dependency on
/// HTTP; the mapping to a response belongs to the host. An ASP.NET Core host gets it from the companion package
/// Zooper.Ant.AspNetCore: <c>services.AddAntExceptionHandler()</c> answers every refusal with a 400 problem response
/// whose <c>type</c> is <c>urn:zooper:ant:invalid-paging</c>, or <c>urn:zooper:ant:invalid-page-token</c> for an
/// <see cref="InvalidPageTokenException"/>. Any other host maps it itself, as in the example.
/// </para>
/// </remarks>
/// <example>
/// A host without the companion package, mapping the refusal by hand:
/// <code>
/// try
/// {
///     return Results.Ok(await queries.ListAsync(filter, paging, ct));
/// }
/// catch (PagingException exception)   // also catches InvalidPageTokenException
/// {
///     return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
/// }
/// </code>
/// </example>
/// <seealso cref="InvalidPageTokenException"/>
public class PagingException : Exception
{
	/// <summary>Creates a paging refusal.</summary>
	/// <param name="message">Names the bound or argument refused; shown to callers.</param>
	public PagingException(string message) : base(message) { }

	/// <summary>Creates a paging refusal caused by another failure, keeping that failure for diagnostics.</summary>
	/// <param name="message">Names the bound or argument refused; shown to callers.</param>
	/// <param name="innerException">The failure that caused the refusal, such as the <see cref="NotSupportedException"/> of a source that cannot count.</param>
	public PagingException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// A continuation token was refused. It derives from <see cref="PagingException"/>, so a host that handles paging
/// errors handles it too, and a host that wants a separate answer for bad tokens catches this type.
/// </summary>
/// <remarks>
/// <para>
/// A token is refused when it was altered, was issued by another read or for other result-defining arguments or another
/// order (see <see cref="PageScope"/>), cannot be decrypted with the available keys, has an unknown payload version, or
/// carries a position that no longer fits the read's order (wrong number of values, a value of the wrong type). Never
/// because of its age: tokens do not expire.
/// </para>
/// <para>
/// Every refusal carries the same fixed message, so a caller cannot probe which check failed. The cause, when there is
/// one (a <c>CryptographicException</c>, a <c>JsonException</c>, …), is kept in <see cref="Exception.InnerException"/>
/// for logs. Raised by <see cref="Zooper.Ant.Tokens.IPageTokenProtector.Unprotect"/>, by
/// <see cref="Zooper.Ant.Sources.IPageSource{TItem}.ParsePosition"/> and by <see cref="Pager.ReadAsync{TItem}"/>, which
/// wraps any other failure of <c>ParsePosition</c> in it.
/// </para>
/// </remarks>
/// <seealso cref="PageToken"/>
public sealed class InvalidPageTokenException : PagingException
{
	/// <summary>The one message of every token refusal. Internal so tests and protectors can compare against it.</summary>
	internal const string FixedMessage = "The page token was not issued by this read for these arguments.";

	/// <summary>Creates a token refusal with the fixed message.</summary>
	public InvalidPageTokenException() : base(FixedMessage) { }

	/// <summary>Creates a token refusal with the fixed message, keeping the cause for diagnostics.</summary>
	/// <param name="innerException">The failure that caused the refusal; logged, never shown to the caller.</param>
	public InvalidPageTokenException(Exception innerException) : base(FixedMessage, innerException) { }
}
