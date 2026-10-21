using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Sources;
using Zooper.Ant.Tokens;

namespace Zooper.Ant;

/// <summary>
/// The single paging service of Zooper.Ant: serves every page kind from any <see cref="IPageSource{TItem}"/>, issues
/// the next token of a continuable page, and refuses a token that was not issued for the same read. It is the one type a
/// consumer injects for paging.
/// </summary>
/// <remarks>
/// <para>
/// Why one service. The kind switch (continuable, counted, capped), the issuing of tokens and the refusal of
/// foreign ones are implemented here once, over the small <see cref="IPageSource{TItem}"/> floor. A store implements
/// that floor and gets every kind for free; it is never told which kind is being served and never handles a token.
/// The LINQ entry point <see cref="QueryableExtensions"/> (<c>query.ToPageAsync(paging, order, scope, pager, ct)</c>)
/// is a thin wrapper that builds a <see cref="QueryablePageSource{T}"/> and calls <see cref="ReadAsync{TItem}"/>.
/// </para>
/// <para>
/// Lifetime. <c>services.AddAnt()</c> (<see cref="ServiceCollectionExtensions.AddAnt"/>) registers it as a
/// singleton. It holds no per-request state: the <see cref="IPageTokenProtector"/> it wraps and the
/// <see cref="AntOptions"/> it reads are both singletons too. There is no interface over it on purpose: there is one
/// implementation, and tests use the real <see cref="Pager"/> with a test protector.
/// </para>
/// <para>
/// Tokens. For a continuable page the pager binds the caller's <see cref="PageScope"/> to the source's
/// <see cref="IPageSource{TItem}.OrderSignature"/> (<see cref="PageScope.Bind"/>), so a token issued under one read,
/// one set of filter values or one ordering is refused under any other. What a token carries is the source's own
/// <see cref="PagePosition"/>; the pager protects and returns it but never interprets its values.
/// </para>
/// <para>
/// Limits. Sizes are bounded where the <see cref="Paging"/> instruction is built, not here. Build instructions
/// from <see cref="Limits"/> so every caller in a host obeys the configured <see cref="PagingLimits"/>.
/// </para>
/// </remarks>
/// <example>
/// A query service that pages a LINQ query and a custom store through the same pager:
/// <code>
/// public sealed class PlayerQueries(AppDbContext db, Pager pager)
/// {
///     public Task&lt;Page&lt;Player&gt;&gt; ListAsync(string? team, PageToken? token, int? size, CancellationToken ct) =>
///         db.Players
///             .Where(p => team == null || p.Team == team)
///             .ToPageAsync(
///                 Paging.Continuable(token, size, pager.Limits),
///                 Order&lt;Player&gt;.ByDescending(p => p.Rating).ThenBy(p => p.Id),
///                 PageScope.For(nameof(ListAsync), team),
///                 pager,
///                 ct);
///
///     public Task&lt;Page&lt;ActivityEvent&gt;&gt; ActivityAsync(Paging paging, CancellationToken ct) =>
///         pager.ReadAsync(new ActivityLogPageSource(log), paging, PageScope.For(nameof(ActivityAsync)), ct);
/// }
/// </code>
/// </example>
/// <param name="protector">
/// Protects positions as tokens and opens them again; by default the <see cref="DataProtectionPageTokenProtector"/>
/// registered by <c>AddAnt</c>.
/// </param>
/// <param name="options">Supplies the configured <see cref="Limits"/>.</param>
/// <seealso cref="IPageSource{TItem}"/>
/// <seealso cref="Paging"/>
/// <seealso cref="Page{TItem}"/>
/// <seealso cref="PageScope"/>
public sealed class Pager(IPageTokenProtector protector, IOptions<AntOptions> options)
{
	/// <summary>The host's bounds, as configured in <see cref="AntOptions.Limits"/>.</summary>
	/// <remarks>
	/// Pass them to the <see cref="Paging"/> factories (<c>Paging.Continuable(token, size, pager.Limits)</c>) so that a
	/// size is defaulted and clamped, and a counted page is depth-checked, by the host's values rather than by
	/// <see cref="PagingLimits.Default"/>.
	/// </remarks>
	public PagingLimits Limits => options.Value.Limits;

	/// <summary>Serves the page <paramref name="paging"/> asks for from <paramref name="source"/>.</summary>
	/// <remarks>
	/// <para>The call pattern on the source depends on the kind, and is the whole cost of the read:</para>
	/// <list type="bullet">
	/// <item><description>
	/// Counted: one <see cref="IPageSource{TItem}.CountAsync"/> and one
	/// <see cref="IPageSource{TItem}.ReadAsync"/> at offset <c>(page - 1) × size</c>.
	/// </description></item>
	/// <item><description>
	/// Capped: one <see cref="IPageSource{TItem}.ReadAsync"/> of <c>cap + 1</c> items from offset 0; the extra
	/// item only answers <see cref="CappedPage{TItem}.Truncated"/>.
	/// </description></item>
	/// <item><description>
	/// Continuable: without a token, <see cref="IPageSource{TItem}.ReadAsync"/> of <c>size + 1</c> from offset 0;
	/// with a token, the token is opened, the position parsed by the source, and
	/// <see cref="IPageSource{TItem}.ReadAfterAsync"/> reads <c>size + 1</c> items strictly after it. The extra item
	/// only answers whether a next token is issued; it is never served.
	/// </description></item>
	/// </list>
	/// </remarks>
	/// <typeparam name="TItem">The item type.</typeparam>
	/// <param name="source">The ordered, filtered result set of one read.</param>
	/// <param name="paging">The caller's paging instruction, already bounded by its factory.</param>
	/// <param name="scope">
	/// The read's name and its result-defining arguments. Only the continuable kind uses it, to bind tokens; it is
	/// required for every kind so a call site does not change when the caller changes the kind.
	/// </param>
	/// <param name="ct">Cancels the read; passed to every source call.</param>
	/// <returns>
	/// A <see cref="ContinuablePage{TItem}"/>, <see cref="CountedPage{TItem}"/> or <see cref="CappedPage{TItem}"/>,
	/// matching the concrete type of <paramref name="paging"/>.
	/// </returns>
	/// <exception cref="PagingException">
	/// The counted kind was asked of a source that throws <see cref="NotSupportedException"/> from
	/// <see cref="IPageSource{TItem}.CountAsync"/> or from an offset <see cref="IPageSource{TItem}.ReadAsync"/>.
	/// </exception>
	/// <exception cref="InvalidPageTokenException">
	/// The token was not issued by this read for these arguments and this ordering, was altered, cannot be opened with
	/// the available keys, or carries a position the source can no longer parse.
	/// </exception>
	/// <exception cref="InvalidOperationException"><paramref name="paging"/> is not one of the three known kinds.</exception>
	public async Task<Page<TItem>> ReadAsync<TItem>(IPageSource<TItem> source, Paging paging, PageScope scope, CancellationToken ct = default)
	{
		switch (paging)
		{
			case CountedPaging counted:
			{
				// The total and one offset range. Both calls are wrapped: a native-cursor store signals that it cannot
				// count or skip by throwing NotSupportedException from exactly these two calls, and that is a refusal
				// of the kind ("not on this read"), not an internal error. The depth bound was already checked by
				// Paging.Counted, so the offset here is at most MaximumDepth.
				var (total, items) = await CountedKind(
					async () => (await source.CountAsync(ct), await source.ReadAsync((counted.Page - 1) * counted.Size, counted.Size, ct)));

				return new CountedPage<TItem>(counted.Page, counted.Size, total, items);
			}

			case CappedPaging capped:
			{
				// One item beyond the cap answers "did more match?" without counting. Offset 0 is a plain first read,
				// so a source that cannot skip still serves this kind.
				var items = await source.ReadAsync(0, capped.Cap + 1, ct);

				return new CappedPage<TItem>(capped.Cap, items.Count > capped.Cap, items.Take(capped.Cap).ToList());
			}

			case ContinuablePaging continuable:
			{
				// The token binding: read name + result-defining values + the source's ordering. Binding the order
				// signature here, for every source, is what refuses a token after the ordering changed.
				var bound = scope.Bind(source.OrderSignature);

				// First page: a plain read from the start. Later pages: open the token (refusing a foreign one before
				// the store is touched) and seek strictly after the position it carries. Both read one item more than
				// the page, so the existence of a next page is known without a count.
				var items = continuable.PageToken is { } token
					? await source.ReadAfterAsync(OpenPosition(source, bound, token), continuable.Size + 1, ct)
					: await source.ReadAsync(0, continuable.Size + 1, ct);

				// Size items or fewer: this is the last page, and the absence of a next token is the end signal.
				if (items.Count <= continuable.Size)
					return new ContinuablePage<TItem>(continuable.Size, null, items);

				// The token marks the last item actually served (index size - 1), not the probe item: continuation
				// starts strictly after it, so the probe item is the first item of the next page.
				var next = protector.Protect(bound, source.PositionOf(items[continuable.Size - 1]));

				return new ContinuablePage<TItem>(continuable.Size, next, items.Take(continuable.Size).ToList());
			}

			default:
				// Paging has a private protected constructor, so only the three kinds above exist.
				throw new InvalidOperationException("Paging kind is not known.");
		}
	}

	/// <summary>
	/// Follows continuation tokens from the first page to the last and yields every item, fetching one page at a time
	/// as the consumer advances.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The traversal starts without a token, invokes <paramref name="readPage"/> once per page, and stops when a page
	/// carries no next token. Only the token differs between invocations; the caller's filters and ordering stay in the
	/// delegate. A page is fetched only when the consumer has taken every item of the previous one, so the full result
	/// is never held in memory.
	/// </para>
	/// <para>
	/// There is no snapshot: each page reads live data. An item whose ordering value changes during the traversal may be
	/// omitted or returned twice (spec <c>paged-traversal</c>); order by an immutable key when neither is acceptable.
	/// There is no retry, buffering or prefetch: a failed page read surfaces once, unchanged, and ends the sequence; a
	/// later attempt starts again from the first page.
	/// </para>
	/// <para>
	/// It is an instance method although it uses no state, so <see cref="Pager"/> stays the single entry point for
	/// paging.
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// await foreach (var player in pager.TraverseAsync&lt;PlayerView&gt;(
	///     (token, ct) => queries.ListAsync("red", Paging.Continuable(token, 100, pager.Limits), ct), ct))
	/// {
	///     await search.IndexAsync(player, ct);
	/// }
	/// </code>
	/// </example>
	/// <typeparam name="TItem">The item type.</typeparam>
	/// <param name="readPage">
	/// Reads one continuable page for a token; <see langword="null"/> asks for the first page. It must return a
	/// <see cref="ContinuablePage{TItem}"/>.
	/// </param>
	/// <param name="ct">
	/// Cancels the traversal. It is passed to every page read and checked before each one, so a cancelled traversal
	/// requests no further page.
	/// </param>
	/// <returns>Every item of the read, in order.</returns>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="readPage"/> returned a counted or capped page, which carries no token to follow.
	/// </exception>
	/// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
	public async IAsyncEnumerable<TItem> TraverseAsync<TItem>(
		Func<PageToken?, CancellationToken, Task<Page<TItem>>> readPage,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		PageToken? token = null;

		do
		{
			ct.ThrowIfCancellationRequested();

			// Any exception from readPage propagates as is: no wrapping, no retry, no further page.
			var page = await readPage(token, ct) as ContinuablePage<TItem>
				?? throw new InvalidOperationException("TraverseAsync needs continuable pages; the page read returned another kind.");

			// Yielding item by item is what makes the next fetch wait until the consumer reaches it.
			foreach (var item in page.ItemList)
				yield return item;

			token = page.NextPageToken;
		}
		while (token is not null);
	}

	/// <summary>Opens a token and lets the source read the position it carries.</summary>
	/// <typeparam name="TItem">The item type.</typeparam>
	/// <param name="source">The source that issued the position and reads it back.</param>
	/// <param name="bound">The scope of the current request, bound to the source's ordering.</param>
	/// <param name="token">The token the caller returned.</param>
	/// <returns>The position to continue strictly after.</returns>
	/// <exception cref="InvalidPageTokenException">
	/// The protector refused the token, or the source could not parse its position.
	/// </exception>
	private PagePosition OpenPosition<TItem>(IPageSource<TItem> source, BoundScope bound, PageToken token)
	{
		// Throws InvalidPageTokenException itself for a foreign, altered or unreadable token.
		var payload = protector.Unprotect(bound, token);

		try
		{
			return source.ParsePosition(payload);
		}
		catch (Exception exception) when (exception is not PagingException)
		{
			// The payload passed the binding but no longer fits the source, for example after a key was added to the
			// ordering or a key's type changed: a refused token (spec continuation-tokens), not an internal error.
			// A PagingException from the source (normally InvalidPageTokenException) already says that and passes
			// through unchanged.
			throw new InvalidPageTokenException(exception);
		}
	}

	/// <summary>Runs the two source calls of the counted kind, turning "cannot count or skip" into a refusal.</summary>
	/// <typeparam name="T">The combined result of the calls.</typeparam>
	/// <param name="read">The count and the offset read.</param>
	/// <returns>The result of <paramref name="read"/>.</returns>
	/// <exception cref="PagingException">A call threw <see cref="NotSupportedException"/>.</exception>
	private static async Task<T> CountedKind<T>(Func<Task<T>> read)
	{
		try
		{
			return await read();
		}
		catch (NotSupportedException exception)
		{
			// Only these two calls may signal an unsupported kind; a NotSupportedException anywhere else is a bug and
			// surfaces as one.
			throw new PagingException("The counted kind is not available on this read.", exception);
		}
	}
}
