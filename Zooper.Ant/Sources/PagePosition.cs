using System.Collections.Generic;
using Zooper.Ant.Tokens;

namespace Zooper.Ant.Sources;

/// <summary>
/// Where a page ended, as an ordered list of JSON-serialisable values the source defines and reads back. A keyset
/// source puts its chain values here, tie-breaker last; a native-cursor source puts its own continuation as one value.
/// The core protects and carries a position but never interprets it.
/// </summary>
/// <remarks>
/// <para>
/// Life cycle. <see cref="Pager"/> asks the source for the position of the last served item
/// (<see cref="IPageSource{TItem}.PositionOf"/>), the <see cref="IPageTokenProtector"/> serialises its
/// <see cref="Values"/> into the token, and on the next request the protector returns them as JSON elements, which the
/// source turns back into a position (<see cref="IPageSource{TItem}.ParsePosition"/>) and seeks after
/// (<see cref="IPageSource{TItem}.ReadAfterAsync"/>).
/// </para>
/// <para>
/// Keyset source. One value per key of the order, in chain order, ending with the unique tie-breaker. For
/// <c>Order&lt;Player&gt;.ByDescending(p =&gt; p.Rating).ThenBy(p =&gt; p.Id)</c> the position of Ivy is
/// <c>[91, 9]</c>. <see cref="QueryablePageSource{T}"/> builds it this way, truncating timestamps to the store's
/// precision.
/// </para>
/// <para>
/// Native-cursor source. A single value: the store's own continuation string (an Elasticsearch
/// <c>search_after</c> key, a Cosmos DB continuation token). The core encrypts it like any other position, so the
/// caller never sees the store's cursor.
/// </para>
/// <para>
/// One position model serves every store, so <see cref="Pager"/> needs no store-specific branch. A value that does not
/// serialise to JSON cannot be put in a token.
/// </para>
/// </remarks>
/// <param name="Values">The position values, in the source's order. <see langword="null"/> entries are allowed.</param>
public sealed record PagePosition(IReadOnlyList<object?> Values);
