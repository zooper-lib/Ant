using Microsoft.AspNetCore.Mvc;

namespace Zooper.Ant.AspNetCore;

/// <summary>
/// The paging arguments of a list request as they arrive in the query string: the five fixed parameters
/// <c>paging</c>, <c>pageToken</c>, <c>page</c>, <c>size</c> and <c>cap</c>. <see cref="PagingRequestExtensions.ToPaging"/>
/// turns them into the <see cref="Zooper.Ant.Paging"/> instruction a read passes to <see cref="Pager"/>.
/// </summary>
/// <remarks>
/// <para>
/// The five names are the wire contract of every Ant host and cannot be renamed. A host whose public API already uses
/// other names does not use this type: it binds its own parameters and calls the <see cref="Zooper.Ant.Paging"/>
/// factories with <see cref="Pager.Limits"/>.
/// </para>
/// <para>
/// How it binds, with nothing to register:
/// <list type="bullet">
/// <item><description>Minimal APIs: <c>[AsParameters] PagingRequest paging</c>. Every property binds as if it were a
/// parameter of the endpoint.</description></item>
/// <item><description>MVC: <c>[FromQuery] PagingRequest paging</c> on an action of an <c>[ApiController]</c>. The
/// complex-type binder reads each property by its attribute name, with no prefix whatever the parameter is called, and <c>[ApiController]</c> answers a failed parse
/// with a 400 before the action runs. Without <c>[ApiController]</c> a failed parse leaves the property
/// <see langword="null"/> and the action runs, so an unknown kind would silently become continuable.</description></item>
/// </list>
/// In both hosts <see cref="Paging"/> binds through <see cref="PagingKind"/>'s parser, ignoring case, and
/// <see cref="PageToken"/> binds through <see cref="Zooper.Ant.PageToken"/>'s parser: a blank or
/// non-base64url value is a 400 before the endpoint runs. Whether a structurally valid token is genuine is decided
/// later, by the <see cref="Pager"/>, which raises <see cref="InvalidPageTokenException"/>.
/// </para>
/// <para>
/// The <c>[FromQuery(Name = …)]</c> attributes are there for the API description: query binding is case-insensitive
/// anyway, but without them the Minimal API explorer would document <c>PageToken</c> instead of <c>pageToken</c>.
/// </para>
/// <para>
/// The class is not sealed so a host may derive from it and add its own filter properties. The recommended shape is
/// to keep the filter in a separate parameter beside this one, which keeps the paging arguments out of the host's
/// types.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// app.MapGet("/players", (
///         [AsParameters] PlayerFilter filter,
///         [AsParameters] PagingRequest paging,
///         PlayerQueries queries,
///         Pager pager,
///         CancellationToken ct) =&gt;
///     queries.ListAsync(filter, paging.ToPaging(pager.Limits), ct));
/// </code>
/// </example>
/// <seealso cref="PagingRequestExtensions"/>
/// <seealso cref="PagingExceptionHandler"/>
// An explicit empty prefix: MVC otherwise uses the parameter name as a prefix whenever a query key matches it, and a
// parameter named "paging" beside ?paging=counted would then look for "paging.paging", "paging.page" and so on.
[Bind(Prefix = "")]
public class PagingRequest
{
	/// <summary>The kind of page asked for, from <c>paging</c>; <see langword="null"/> (absent) means continuable.</summary>
	[FromQuery(Name = "paging")]
	public PagingKind? Paging { get; init; }

	/// <summary>
	/// The continuation token of the previous continuable page, from <c>pageToken</c>; <see langword="null"/> for the
	/// first page. Continuable paging only.
	/// </summary>
	[FromQuery(Name = "pageToken")]
	public PageToken? PageToken { get; init; }

	/// <summary>The one-based page number, from <c>page</c>; <see langword="null"/> means the first page. Counted paging only.</summary>
	[FromQuery(Name = "page")]
	public int? Page { get; init; }

	/// <summary>
	/// The requested page size, from <c>size</c>; defaulted and clamped by the host's <see cref="PagingLimits"/>.
	/// Continuable and counted paging only.
	/// </summary>
	[FromQuery(Name = "size")]
	public int? Size { get; init; }

	/// <summary>
	/// The requested cap, from <c>cap</c>; defaulted and clamped like a size. Capped paging only.
	/// </summary>
	[FromQuery(Name = "cap")]
	public int? Cap { get; init; }
}
