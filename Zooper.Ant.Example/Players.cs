using Zooper.Ant;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Example;

/// <summary>A player as the store keeps it: the row type the paged LINQ query runs over.</summary>
/// <remarks>
/// <see cref="Id"/> is unique, which is what makes it usable as the tie-breaker that ends every <see cref="Order{T}"/>
/// in <see cref="PlayerQueries"/>. <see cref="Rating"/> is nullable on purpose: it shows how Zooper.Ant places nulls
/// (last by default) and seeks past a null bookmark.
/// </remarks>
/// <param name="Id">The unique identifier; the tie-breaker of every order.</param>
/// <param name="Name">The player's name; the key of the name order.</param>
/// <param name="Team">The team; a filter value, and so part of the token's scope.</param>
/// <param name="Rating">The rating from 0 to 100, or <see langword="null"/> for an unrated player.</param>
public sealed record Player(int Id, string Name, string Team, int? Rating);

/// <summary>What callers get back: the view a page of <see cref="Player"/> is mapped to.</summary>
/// <remarks>
/// <see cref="PlayerQueries.ListAsync"/> reads a <see cref="Page{TItem}"/> of <see cref="Player"/> and calls
/// <see cref="Page{TItem}.Map{TOut}"/> with <see cref="From"/>. Map changes only the items: a counted page stays counted
/// with its page number and total, a continuable page keeps its next token. In a real application the view is where
/// you drop columns a caller must not see.
/// </remarks>
/// <param name="Id">The player's identifier.</param>
/// <param name="Name">The player's name.</param>
/// <param name="Team">The player's team.</param>
/// <param name="Rating">The rating, or <see langword="null"/> when unrated.</param>
public sealed record PlayerView(int Id, string Name, string Team, int? Rating)
{
	/// <summary>Maps a stored player to its view; passed to <see cref="Page{TItem}.Map{TOut}"/>.</summary>
	/// <param name="player">The stored player.</param>
	/// <returns>The view of the player.</returns>
	public static PlayerView From(Player player) => new(player.Id, player.Name, player.Team, player.Rating);

	/// <summary>The short form the tour prints, for example <c>Ada (red, 91)</c>.</summary>
	/// <returns>The name, team and rating, or <c>unrated</c>.</returns>
	public override string ToString() => $"{Name} ({Team}, {Rating?.ToString() ?? "unrated"})";
}

/// <summary>The body of <c>POST /players</c>: a player to add while a read is in progress.</summary>
/// <param name="Name">The new player's name.</param>
/// <param name="Team">The new player's team.</param>
/// <param name="Rating">The rating, or <see langword="null"/> for unrated.</param>
public sealed record NewPlayer(string Name, string Team, int? Rating);

/// <summary>The arguments that decide which players a list contains and in which order.</summary>
/// <remarks>
/// <para>
/// <see cref="Team"/> and <see cref="MinRating"/> decide the result set, so <see cref="PlayerQueries.ListAsync"/> puts
/// them into the <see cref="PageScope"/>: a token issued for one team is refused for another instead of continuing a
/// different list. <see cref="Sort"/> and <see cref="Reverse"/> decide the order; they are not listed in the scope
/// because the <see cref="Pager"/> folds the order's signature into the token itself.
/// </para>
/// <para>In HTTP mode it is bound from the query string with <c>[AsParameters]</c>.</para>
/// </remarks>
/// <param name="Team">Only players of this team; all teams when <see langword="null"/>.</param>
/// <param name="MinRating">Only players rated at least this; unrated players never match a minimum.</param>
/// <param name="Sort">The order; best rating first when <see langword="null"/>.</param>
/// <param name="Reverse">Whether to serve the order reversed as a whole.</param>
public sealed record PlayerFilter(string? Team = null, int? MinRating = null, PlayerSort? Sort = null, bool? Reverse = null);

/// <summary>The orders a player list can be read in.</summary>
/// <remarks>
/// An enum rather than a string, so ASP.NET Core rejects an unknown <c>?sort=</c> with a 400 by itself and the
/// switch in <see cref="PlayerQueries"/> cannot miss a case. Binding is case-sensitive: <c>Rating</c>, <c>Name</c>.
/// </remarks>
public enum PlayerSort
{
	/// <summary>Best rating first, unrated players last, ties by <see cref="Player.Id"/>.</summary>
	Rating,

	/// <summary>Alphabetical by name, ties by <see cref="Player.Id"/>.</summary>
	Name
}

/// <summary>
/// A typical query service: it injects <see cref="Pager"/> and ends its LINQ query with
/// <see cref="QueryableExtensions.ToPageAsync{T}(IEnumerable{T}, Paging, Order{T}, PageScope, Pager, CancellationToken)"/>.
/// This is the shape most application code using Zooper.Ant has.
/// </summary>
/// <remarks>
/// <para>
/// The read is written once and serves every page kind: the caller's <see cref="Paging"/> decides whether the answer is
/// a <see cref="ContinuablePage{TItem}"/>, a <see cref="CountedPage{TItem}"/> or a <see cref="CappedPage{TItem}"/>.
/// Both the tour and the HTTP endpoints call this same method.
/// </para>
/// <para>
/// Here the store is an in-memory list, so the in-memory <c>ToPageAsync</c> from <c>Zooper.Ant.Sources</c> is used. With
/// a database the query would start at a <c>DbSet</c> and end with the adapter package's <c>ToPageAsync</c>; the order,
/// scope and paging arguments stay the same.
/// </para>
/// </remarks>
/// <param name="store">The table the players are read from.</param>
/// <param name="pager">The Zooper.Ant service that runs the page kinds and issues and checks tokens.</param>
public sealed class PlayerQueries(PlayerStore store, Pager pager)
{
	/// <summary>Reads one page of players matching <paramref name="filter"/>, of the kind <paramref name="paging"/> asks for.</summary>
	/// <param name="filter">Which players, and in which order.</param>
	/// <param name="paging">The caller's paging instruction, already bounded by the limits.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <returns>A page of player views of the requested kind.</returns>
	/// <exception cref="InvalidPageTokenException">
	/// The token was issued by another read, for another team or minimum rating, under another order, or was altered.
	/// </exception>
	public async Task<Page<PlayerView>> ListAsync(PlayerFilter filter, Paging paging, CancellationToken ct = default)
	{
		// A filter that cannot match anything (ratings stop at 100) is answered without reading. ToEmptyPage builds an
		// empty page of the kind the caller asked for: an empty counted page still reports its page number and a total
		// of 0, an empty continuable page has no next token.
		if (filter.MinRating > 100)
			return paging.ToEmptyPage<PlayerView>();

		// The filtered result set, still unordered: ToPageAsync applies the order and, for a continuation, the seek.
		var players = store.All()
			.Where(p => filter.Team == null || p.Team == filter.Team)
			.Where(p => filter.MinRating == null || p.Rating >= filter.MinRating);

		var page = await players.ToPageAsync(
			paging,
			OrderFor(filter.Sort, filter.Reverse == true),
			// The scope binds tokens to this read: its name plus every argument that decides the result set. A token
			// issued for team=red is refused for team=blue instead of quietly continuing another list. The order is not
			// listed: the Pager adds its signature itself. The page size is left out on purpose, so a caller may change
			// it between pages.
			PageScope.For(nameof(ListAsync), filter.Team, filter.MinRating),
			pager,
			ct);

		// Map changes the item type and keeps the kind with its page number, total, cap or next token.
		return page.Map(PlayerView.From);
	}

	/// <summary>Declares the order for a sort choice, reversed when asked.</summary>
	/// <remarks>
	/// <para>
	/// Every order ends with the unique <see cref="Player.Id"/>. Ada and Ivy both have 91, Bob and Dan both 78: without
	/// the Id their relative order would be undefined, and a page boundary between them could skip or repeat one. With
	/// it, the position "after Ivy, 91, id 9" is exact.
	/// </para>
	/// <para>
	/// <see cref="Player.Rating"/> is nullable, so unrated players sort last in both directions by default.
	/// <see cref="Order{T}.Reverse"/> flips every key's direction and nulls placement, so reversed rating order starts
	/// with the unrated players and then the lowest rating.
	/// </para>
	/// </remarks>
	/// <param name="sort">The sort choice; rating when <see langword="null"/>.</param>
	/// <param name="reverse">Whether to reverse the whole order.</param>
	/// <returns>The order the read is sorted and continued by.</returns>
	private static Order<Player> OrderFor(PlayerSort? sort, bool reverse)
	{
		var order = sort switch
		{
			null or PlayerSort.Rating => Order<Player>.ByDescending(p => p.Rating).ThenBy(p => p.Id),
			PlayerSort.Name => Order<Player>.By(p => p.Name).ThenBy(p => p.Id),
			_ => throw new ArgumentOutOfRangeException(nameof(sort))
		};

		return reverse ? order.Reverse() : order;
	}
}

/// <summary>An in-memory table of twelve players, small enough to follow by eye.</summary>
/// <remarks>
/// <para>
/// The data is chosen to show edge cases: two ties on rating (Ada and Ivy at 91, Bob and Dan at 78) and two unrated
/// players (Cid and Gus). In rating order with pages of 3 the list is Eve, Ada, Ivy | Kim, Hal, Bob | Dan, Joe, Fay |
/// Lea, Cid, Gus.
/// </para>
/// <para>
/// It is mutable so the tour and <c>POST /players</c> can show that pages read live data. A real application would use
/// a database here; Zooper.Ant only ever sees the query built from it.
/// </para>
/// </remarks>
public sealed class PlayerStore
{
	/// <summary>Guards <see cref="_players"/>; the HTTP API may add players while another request reads.</summary>
	private readonly Lock _lock = new();

	/// <summary>The rows, in insertion order. The order of a read comes from <see cref="Order{T}"/>, never from here.</summary>
	private readonly List<Player> _players =
	[
		new(1, "Ada", "red", 91),
		new(2, "Bob", "blue", 78),
		new(3, "Cid", "green", null),
		new(4, "Dan", "red", 78),
		new(5, "Eve", "blue", 95),
		new(6, "Fay", "green", 64),
		new(7, "Gus", "red", null),
		new(8, "Hal", "blue", 82),
		new(9, "Ivy", "green", 91),
		new(10, "Joe", "red", 70),
		new(11, "Kim", "blue", 88),
		new(12, "Lea", "green", 59)
	];

	/// <summary>A copy of every player as it is now.</summary>
	/// <remarks>
	/// Each page read takes a fresh copy, so a player added or removed between two pages is seen by the next page: there
	/// is no snapshot of the whole read.
	/// </remarks>
	/// <returns>The current players.</returns>
	// ponytail: copy under a global lock; a real store reads live rows itself.
	public IReadOnlyList<Player> All()
	{
		lock (_lock)
			return _players.ToList();
	}

	/// <summary>Adds a player with the next free Id.</summary>
	/// <param name="player">The player to add.</param>
	/// <returns>The added player's view.</returns>
	public PlayerView Add(NewPlayer player)
	{
		lock (_lock)
		{
			var added = new Player(_players.Max(p => p.Id) + 1, player.Name, player.Team, player.Rating);
			_players.Add(added);

			return PlayerView.From(added);
		}
	}

	/// <summary>Removes every player with the given name; the tour uses it to delete a row between two pages.</summary>
	/// <param name="name">The name to remove.</param>
	public void Remove(string name)
	{
		lock (_lock)
			_players.RemoveAll(p => p.Name == name);
	}
}
