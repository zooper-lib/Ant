using System.Text.Json;
using Zooper.Ant;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Example;

/// <summary>One entry of the activity feed.</summary>
/// <param name="Sequence">The log's own increasing number; the feed is read newest first.</param>
/// <param name="Description">What happened.</param>
public sealed record ActivityEvent(long Sequence, string Description)
{
	/// <summary>The short form the tour prints, for example <c>#8 Blue drew green 1:1</c>.</summary>
	/// <returns>The sequence number and description.</returns>
	public override string ToString() => $"#{Sequence} {Description}";
}

/// <summary>
/// Stands in for a store that pages with its own cursors (Elasticsearch <c>search_after</c>, a Cosmos DB continuation,
/// a log offset): it reads forward from a cursor, newest first, but cannot count or skip.
/// </summary>
/// <remarks>
/// Such stores are why Zooper.Ant's store contract, <see cref="IPageSource{TItem}"/>, carries an opaque
/// <see cref="PagePosition"/> rather than a row offset. A real store would issue its own cursor strings; here the cursor
/// is <c>seq-</c> plus the sequence number, built by <see cref="CursorOf"/>.
/// </remarks>
public sealed class ActivityLog
{
	/// <summary>The events, oldest first; <see cref="ReadAsync"/> serves them newest first.</summary>
	private readonly IReadOnlyList<ActivityEvent> _events =
	[
		new(1, "Ada joined red"), new(2, "Bob joined blue"), new(3, "Red beat blue 3:1"), new(4, "Eve scored twice"),
		new(5, "Ivy joined green"), new(6, "Green beat red 2:0"), new(7, "Kim was transferred"), new(8, "Blue drew green 1:1")
	];

	/// <summary>The store's own opaque cursor of an event: reading after it continues with the next older event.</summary>
	/// <param name="activity">An event this log served.</param>
	/// <returns>The cursor, for example <c>seq-6</c>.</returns>
	public string CursorOf(ActivityEvent activity) => $"seq-{activity.Sequence}";

	/// <summary>Reads up to <paramref name="limit"/> events, newest first, after a cursor.</summary>
	/// <param name="afterCursor">A cursor from <see cref="CursorOf"/>, or <see langword="null"/> to start at the newest event.</param>
	/// <param name="limit">The most events to return.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <returns>The events older than the cursor, newest first.</returns>
	public Task<IReadOnlyList<ActivityEvent>> ReadAsync(string? afterCursor, int limit, CancellationToken ct)
	{
		var after = afterCursor is null ? long.MaxValue : long.Parse(afterCursor["seq-".Length..]);

		return Task.FromResult<IReadOnlyList<ActivityEvent>>(
			_events.Where(e => e.Sequence < after).OrderByDescending(e => e.Sequence).Take(limit).ToList());
	}
}

/// <summary>
/// Pages <see cref="ActivityLog"/> by implementing <see cref="IPageSource{TItem}"/> directly: the way to page any store
/// that has no LINQ provider.
/// </summary>
/// <remarks>
/// <para>
/// A source only answers questions about one ordered result set; it never sees a <see cref="Paging"/> or a
/// <see cref="PageToken"/>. <see cref="Pager.ReadAsync{TItem}"/> does the rest: it asks for one item more than the page
/// to know whether a next page exists, calls <see cref="PositionOf"/> on the last served item, encrypts that position
/// into the next token, and on the next request opens the token and hands the position back to
/// <see cref="ParsePosition"/> and <see cref="ReadAfterAsync"/>.
/// </para>
/// <para>
/// The position here is the store's own cursor, a single string. The Pager protects it like any other position, so a
/// caller never sees or forges it.
/// </para>
/// <para>
/// The store cannot count or skip, so <see cref="CountAsync"/> and <see cref="ReadAsync"/> with an offset throw
/// <see cref="NotSupportedException"/>. The Pager turns exactly that exception into a <see cref="PagingException"/>
/// saying the counted kind is not available on this read. Continuable and capped pages need neither and keep working.
/// </para>
/// </remarks>
/// <param name="log">The store to page.</param>
public sealed class ActivityLogPageSource(ActivityLog log) : IPageSource<ActivityEvent>
{
	/// <summary>An empty signature: the store fixes the order, so there is no order a token could disagree with.</summary>
	/// <remarks>A LINQ source returns its <see cref="Order{T}.OrderSignature"/> here, which refuses a token after the order changes.</remarks>
	public string OrderSignature => "";

	/// <summary>Not supported: the store cannot count. The Pager refuses the counted kind with a <see cref="PagingException"/>.</summary>
	/// <param name="ct">Unused.</param>
	/// <returns>Never returns.</returns>
	/// <exception cref="NotSupportedException">Always.</exception>
	public Task<int> CountAsync(CancellationToken ct) => throw new NotSupportedException();

	/// <summary>Reads the newest events. Only offset 0 is supported, which is all the continuable and capped kinds use.</summary>
	/// <param name="offset">Must be 0; the counted kind asks for later offsets.</param>
	/// <param name="limit">The most events to return; the Pager asks for one more than the page.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <returns>The newest events.</returns>
	/// <exception cref="NotSupportedException"><paramref name="offset"/> is above 0: the store cannot skip.</exception>
	public Task<IReadOnlyList<ActivityEvent>> ReadAsync(int offset, int limit, CancellationToken ct) =>
		offset > 0 ? throw new NotSupportedException() : log.ReadAsync(null, limit, ct);

	/// <summary>Reads the events after a position the Pager recovered from a token.</summary>
	/// <param name="position">The position from <see cref="ParsePosition"/>: one value, the store's cursor.</param>
	/// <param name="limit">The most events to return.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <returns>The events after the cursor.</returns>
	public Task<IReadOnlyList<ActivityEvent>> ReadAfterAsync(PagePosition position, int limit, CancellationToken ct) =>
		log.ReadAsync((string)position.Values[0]!, limit, ct);

	/// <summary>The position of the last served event: its cursor, which the Pager encrypts into the next token.</summary>
	/// <param name="item">The last event of a page.</param>
	/// <returns>A one-value position holding the cursor.</returns>
	public PagePosition PositionOf(ActivityEvent item) => new([log.CursorOf(item)]);

	/// <summary>Reads a position back from a decrypted token payload.</summary>
	/// <remarks>
	/// The payload passed decryption and the scope check, so it was issued by this application for this read. The shape
	/// is still checked: anything other than one <c>seq-</c> cursor is refused as an invalid token rather than crashing
	/// the read later.
	/// </remarks>
	/// <param name="payload">The position values as JSON, in the order <see cref="PositionOf"/> wrote them.</param>
	/// <returns>The position holding the cursor.</returns>
	/// <exception cref="InvalidPageTokenException">The payload is not a single cursor string.</exception>
	public PagePosition ParsePosition(IReadOnlyList<JsonElement> payload) =>
		payload is [{ ValueKind: JsonValueKind.String } cursor] && cursor.GetString()!.StartsWith("seq-")
			? new PagePosition([cursor.GetString()])
			: throw new InvalidPageTokenException();
}
