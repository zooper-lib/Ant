using Zooper.Ant;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Example;

/// <summary>
/// A narrated walk through every feature of Zooper.Ant. Each chapter says what it shows, makes the call, and prints
/// what came back. Read it next to its output: the code of a chapter is the usage it explains.
/// </summary>
/// <remarks>
/// <para>
/// Run with <c>dotnet run --project Zooper.Ant.Example</c>. The tour reads through the same
/// <see cref="PlayerQueries.ListAsync"/> the HTTP API uses, with the <see cref="Pager"/> and limits registered in
/// Program.cs (default size 5, maximum 10, counted depth 30), over the twelve players of <see cref="PlayerStore"/>.
/// </para>
/// <para>
/// The output marks each line: <c>&gt;</c> is the call, <c>&lt;</c> what came back, <c>x</c> a refusal with the
/// exception's type and message, <c>i</c> a note. Refusals are caught and printed so the tour can show them and carry on;
/// in a web host <see cref="PagingExceptionHandler"/> turns them into 400 responses instead.
/// </para>
/// <para>
/// Most chapters build <see cref="Paging"/> without limits, which applies <see cref="PagingLimits.Default"/>; they ask for
/// explicit sizes well inside every bound. Chapters 5 and 6, which show the bounds, pass <see cref="Pager.Limits"/> the
/// way application code should.
/// </para>
/// </remarks>
/// <param name="queries">The player list read every chapter calls.</param>
/// <param name="store">The players; mutated by the live-data chapter and restored afterwards.</param>
/// <param name="activity">The cursor-based store of the last chapter.</param>
/// <param name="pager">The Zooper.Ant service, for <see cref="Pager.Limits"/>, traversal and the custom source.</param>
public sealed class Tour(PlayerQueries queries, PlayerStore store, ActivityLog activity, Pager pager)
{
	/// <summary>The number of the chapter printed last; <see cref="Chapter"/> increments it.</summary>
	private int _chapter;

	/// <summary>Prints the players and then every chapter, in reading order.</summary>
	/// <returns>A task that completes when the tour has been printed.</returns>
	public async Task RunAsync()
	{
		Title("Zooper.Ant tour",
			"Twelve players, listed best rating first. Limits in Program.cs: default size 5, maximum size 10, "
			+ "counted pages may reach 30 rows deep.");

		Console.WriteLine();
		foreach (var player in store.All().OrderByDescending(p => p.Rating).ThenBy(p => p.Id))
			Console.WriteLine($"      {player.Id,2}  {PlayerView.From(player)}");

		await ContinuablePages();
		await CountedPage();
		await CappedPage();
		await SameOrderInEveryKind();
		await SizesAreBounded();
		await DepthIsBounded();
		await OrdersAndTieBreakers();
		await TokensBelongToTheirRead();
		await TamperedTokens();
		await LiveData();
		await MappingAndEmptyPages();
		await Traversal();
		await CursorStore();

		Title("Done", "Run `dotnet run -- --serve` to try the same reads over HTTP with Zooper.Ant.Example.http.");
	}

	/// <summary>
	/// Chapter 1, continuable pages (spec continuation-tokens): start without a token, follow each next token, and stop
	/// when a page carries none. The absence of a token is the only end signal.
	/// </summary>
	private async Task ContinuablePages()
	{
		Chapter("Continuable pages, the default kind",
			"Ask for a page without a token to start. Each page carries a next token while more players exist; "
			+ "send it back to get the page after. No next token means you have reached the end. "
			+ "The token is encrypted: callers pass it back unchanged and never read it.");

		// A null token asks for the first page. Each response's NextPageToken is passed back unchanged; the loop ends at
		// the page that carries none.
		PageToken? token = null;
		var number = 1;

		do
		{
			Call($"queries.ListAsync(new PlayerFilter(), Paging.Continuable({(token is null ? "null" : "token")}, size: 3))");
			var page = (ContinuablePage<PlayerView>)await queries.ListAsync(new PlayerFilter(), Paging.Continuable(token, 3));
			Show(page, $"page {number++}");
			token = page.NextPageToken;
		}
		while (token is not null);

		Note("Continuing is a seek ('players after Ivy, 91, id 9'), never an offset, so it costs the same on page 1000 "
			+ "as on page 2.");
	}

	/// <summary>Chapter 2, counted pages (spec paged-reads): a page by number with the total count of every match.</summary>
	private async Task CountedPage()
	{
		Chapter("Counted pages",
			"When a screen needs 'page 2 of 4' and jumps to any page, ask for the counted kind. It costs a count of "
			+ "every match, so it is the expensive kind.");

		Call("queries.ListAsync(new PlayerFilter(), Paging.Counted(page: 2, size: 3))");
		Show(await queries.ListAsync(new PlayerFilter(), Paging.Counted(2, 3)));
	}

	/// <summary>Chapter 3, capped pages (spec paged-reads): the top N and whether more matched, with no continuation.</summary>
	private async Task CappedPage()
	{
		Chapter("Capped pages",
			"For one screen of best results, like a top-3 widget: no continuation, only whether more matched.");

		Call("queries.ListAsync(new PlayerFilter(Team: \"red\"), Paging.Capped(cap: 3))");
		Show(await queries.ListAsync(new PlayerFilter(Team: "red"), Paging.Capped(3)));
	}

	/// <summary>
	/// Chapter 4, one read for every kind (spec paged-reads, "Same order across kinds"): the same call with three
	/// <see cref="Paging"/> kinds returns the same first items in the same order.
	/// </summary>
	private async Task SameOrderInEveryKind()
	{
		Chapter("One read, three kinds",
			"The read is written once. The caller's Paging picks the kind, and every kind uses the same order.");

		Show(await queries.ListAsync(new PlayerFilter(), Paging.Continuable(null, 4)));
		Show(await queries.ListAsync(new PlayerFilter(), Paging.Counted(1, 4)));
		Show(await queries.ListAsync(new PlayerFilter(), Paging.Capped(4)));
	}

	/// <summary>
	/// Chapter 5, size bounds (spec paged-reads): no size gives <see cref="PagingLimits.DefaultSize"/>, an oversized one
	/// is clamped to <see cref="PagingLimits.MaximumSize"/> and served without error.
	/// </summary>
	private async Task SizesAreBounded()
	{
		Chapter("Sizes are bounded, not trusted",
			"No size means the default. A size above the maximum is clamped and served: a smaller page is still "
			+ "a correct answer. Paging is built from pager.Limits, the limits configured in AddAnt.");

		Call("Paging.Continuable(null, size: null, pager.Limits)");
		Show(await queries.ListAsync(new PlayerFilter(), Paging.Continuable(null, null, pager.Limits)));

		Call("Paging.Continuable(null, size: 500, pager.Limits)");
		Show(await queries.ListAsync(new PlayerFilter(), Paging.Continuable(null, 500, pager.Limits)));
	}

	/// <summary>
	/// Chapter 6, counted depth (spec paged-reads, "Depth beyond the bound"): a counted page whose last row lies beyond
	/// <see cref="PagingLimits.MaximumDepth"/> is refused with a <see cref="PagingException"/> naming the limit.
	/// </summary>
	private async Task DepthIsBounded()
	{
		Chapter("Counted depth is bounded",
			"A counted page deeper than the depth limit is refused, not quietly replaced by another page. "
			+ "Continuable pages have no depth limit.");

		// Page 11 at size 3 would end at row 33. Paging.Counted throws before any store is asked; the depth is refused,
		// never clamped, because serving another page under number 11 would be a wrong answer.
		Call("Paging.Counted(page: 11, size: 3, pager.Limits)   // row 33, the limit is 30");
		await Refused(() => queries.ListAsync(new PlayerFilter(), Paging.Counted(11, 3, pager.Limits)));
	}

	/// <summary>
	/// Chapter 7, orders (spec keyset-ordering): another sort key, <see cref="Order{T}.Reverse"/>, nulls placement, and
	/// a page boundary that falls between two players with the same rating, kept exact by the Id tie-breaker.
	/// </summary>
	private async Task OrdersAndTieBreakers()
	{
		Chapter("Orders, ties and reversal",
			"An order is declared once: Order<Player>.ByDescending(p => p.Rating).ThenBy(p => p.Id). It sorts the page "
			+ "and drives the seek. Unrated players sort last. Ada and Ivy share 91, Bob and Dan share 78: the Id "
			+ "decides, so a page boundary between them can never skip or repeat one. Reverse() flips the whole order.");

		Call("sort: Name");
		Show(await queries.ListAsync(new PlayerFilter(Sort: PlayerSort.Name), Paging.Capped(5)));

		Call("sort: Rating, reverse: true   // unrated first, then the lowest rating");
		Show(await queries.ListAsync(new PlayerFilter(Reverse: true), Paging.Capped(5)));

		// Bob (id 2) ends page 1 and Dan (id 4) starts page 2, both rated 78. The token holds (78, 2), so the seek asks for
		// "rating below 78, or rating 78 and id above 2": Dan is found and nobody is repeated.
		Call("Rating order, size 6: the page boundary falls between Bob and Dan, both 78");
		var first = (ContinuablePage<PlayerView>)await queries.ListAsync(new PlayerFilter(), Paging.Continuable(null, 6));
		Show(first, "page 1");
		Show(await queries.ListAsync(new PlayerFilter(), Paging.Continuable(first.NextPageToken, 6)), "page 2");
	}

	/// <summary>
	/// Chapter 8, token binding (spec continuation-tokens, "A token continues the same arguments only"): a token continues
	/// with another page size, and is refused with another team or another order.
	/// </summary>
	private async Task TokensBelongToTheirRead()
	{
		Chapter("A token only continues the read that issued it",
			"PageScope.For(nameof(ListAsync), team, minRating) binds the token to the read and its filters, and the "
			+ "Pager adds the order. Change any of them and the token is refused, instead of quietly continuing a "
			+ "different list. The page size is not bound: it may change between pages.");

		var red = (ContinuablePage<PlayerView>)await queries.ListAsync(new PlayerFilter(Team: "red"), Paging.Continuable(null, 2));
		Call("team: red, size 2");
		Show(red, "page 1");

		Call("the red token, size 3   // another size is fine");
		Show(await queries.ListAsync(new PlayerFilter(Team: "red"), Paging.Continuable(red.NextPageToken, 3)), "page 2");

		// The token's fingerprint covers the read name, the scope values (team, minRating) and the order signature; any
		// difference makes Pager refuse it with InvalidPageTokenException before the store is asked.
		Call("the red token, team: blue");
		await Refused(() => queries.ListAsync(new PlayerFilter(Team: "blue"), Paging.Continuable(red.NextPageToken, 2)));

		Call("the red token, team: red, sort: Name");
		await Refused(() => queries.ListAsync(new PlayerFilter(Team: "red", Sort: PlayerSort.Name), Paging.Continuable(red.NextPageToken, 2)));
	}

	/// <summary>
	/// Chapter 9, token protection (spec continuation-tokens): a token with one changed character is refused as invalid,
	/// and malformed text is refused by <see cref="PageToken.Parse"/> before any read runs.
	/// </summary>
	private async Task TamperedTokens()
	{
		Chapter("Tokens are tamper-proof and never expire",
			"Tokens are encrypted and signed with ASP.NET Core Data Protection. Changing a single character gets the "
			+ "token refused. A token has no lifetime: it works until the keys are gone.");

		var page = (ContinuablePage<PlayerView>)await queries.ListAsync(new PlayerFilter(), Paging.Continuable(null, 3));
		var value = page.NextPageToken!.Value.Value;
		// Still valid base64url, so PageToken.Parse accepts it; Data Protection's integrity check rejects it when opened.
		var tampered = PageToken.Parse(value[..20] + (value[20] == 'A' ? 'B' : 'A') + value[21..]);

		Call("a token with its 21st character changed");
		await Refused(() => queries.ListAsync(new PlayerFilter(), Paging.Continuable(tampered, 3)));

		Call("PageToken.Parse(\"not a token!\")   // the format is checked before any read");
		await Refused(() => Task.FromResult(PageToken.Parse("not a token!")));
	}

	/// <summary>
	/// Chapter 10, live data (spec continuation-tokens, "An earlier item is deleted"): between two pages a served player
	/// is deleted and a new one added; the second page starts exactly after the bookmark and includes the new player.
	/// The store is restored afterwards so later chapters see the original twelve.
	/// </summary>
	private async Task LiveData()
	{
		Chapter("Pages read live data",
			"Nothing is frozen between pages. The token marks a position in the order ('after Ivy, 91, id 9'), not "
			+ "an offset, so deleting a player who was already shown does not shift the next page. A player added "
			+ "after the position shows up on a later page.");

		var first = (ContinuablePage<PlayerView>)await queries.ListAsync(new PlayerFilter(), Paging.Continuable(null, 3));
		Call("page 1, size 3");
		Show(first, "page 1");

		// Eve was served on page 1; Zoe (85) sorts after the bookmark (Ivy, 91, id 9).
		store.Remove("Eve");
		var zoe = store.Add(new NewPlayer("Zoe", "red", 85));
		Call($"meanwhile: Eve is deleted and {zoe} is added");

		Call("page 2 with the page-1 token");
		Show(await queries.ListAsync(new PlayerFilter(), Paging.Continuable(first.NextPageToken, 3)), "page 2");
		Note("With offsets, deleting Eve would have moved Kim onto page 1, and Kim would never be shown.");

		// Restore the original twelve for the later chapters (Eve gets a new id, which no later chapter depends on).
		store.Remove("Zoe");
		store.Add(new NewPlayer("Eve", "blue", 95));
	}

	/// <summary>
	/// Chapter 11, <see cref="Page{TItem}.Map{TOut}"/> and <see cref="Paging.ToEmptyPage{TItem}"/> (spec paged-reads,
	/// "Mapping keeps the kind"): an impossible filter is answered with an empty counted page without reading.
	/// </summary>
	private async Task MappingAndEmptyPages()
	{
		Chapter("Mapping and empty pages",
			"page.Map(PlayerView.From) changes the items and keeps the kind with its page number, total or token. "
			+ "A filter known to match nothing answers with paging.ToEmptyPage<T>(), in the kind that was asked for, "
			+ "without reading.");

		Call("minRating: 101, counted page 1");
		Show(await queries.ListAsync(new PlayerFilter(MinRating: 101), Paging.Counted(1, 3)));
	}

	/// <summary>
	/// Chapter 12, traversal (spec paged-traversal): <see cref="Pager.TraverseAsync{TItem}"/> follows the tokens and
	/// yields every player; the page reads are counted to show it fetches one page at a time.
	/// </summary>
	private async Task Traversal()
	{
		Chapter("Reading everything with TraverseAsync",
			"For a sync job or an export: pager.TraverseAsync follows the tokens for you and yields players one at a "
			+ "time. It reads the next page only when you reach it, and an error ends it with that error.");

		// Count page reads to show the traversal fetches one page at a time: 12 players at size 5 take 3 reads.
		var reads = 0;
		var names = new List<string>();

		Call("await foreach (var player in pager.TraverseAsync<PlayerView>((token, ct) => queries.ListAsync(filter, Paging.Continuable(token, 5), ct)))");
		await foreach (var player in pager.TraverseAsync<PlayerView>((token, ct) =>
		{
			reads++;
			return queries.ListAsync(new PlayerFilter(), Paging.Continuable(token, 5), ct);
		}))
			names.Add(player.Name);

		Result($"{names.Count} players in {reads} page reads: {string.Join(", ", names)}");
	}

	/// <summary>
	/// Chapter 13, any store (spec page-sources, "A source that cannot count or offset is served continuable-only"):
	/// <see cref="ActivityLogPageSource"/> pages a cursor-based store through <see cref="Pager.ReadAsync{TItem}"/>;
	/// continuable pages work and the counted kind is refused.
	/// </summary>
	private async Task CursorStore()
	{
		Chapter("Any store: implement IPageSource<T>",
			"ActivityLog stands in for a store that pages with its own cursors and cannot count. ActivityLogPageSource "
			+ "puts the store's cursor in the position and the Pager encrypts it like any other. Continuable and capped "
			+ "pages work; counted is refused because the store cannot count.");

		// No filters, so the scope holds only the read name. There is no LINQ here: the source is passed to
		// Pager.ReadAsync directly, which is all a LINQ ToPageAsync does internally.
		var scope = PageScope.For("ListActivity");

		Call("pager.ReadAsync(new ActivityLogPageSource(log), Paging.Continuable(null, 3), scope)");
		var first = (ContinuablePage<ActivityEvent>)await pager.ReadAsync(new ActivityLogPageSource(activity), Paging.Continuable(null, 3), scope);
		Show(first, "page 1");
		Show(await pager.ReadAsync(new ActivityLogPageSource(activity), Paging.Continuable(first.NextPageToken, 3), scope), "page 2");

		Call("Paging.Counted(1, 3)");
		await Refused(() => pager.ReadAsync(new ActivityLogPageSource(activity), Paging.Counted(1, 3), scope));
	}

	// ---- Output -----------------------------------------------------------------------------------------------------

	/// <summary>Prints a section title in cyan and its wrapped explanation.</summary>
	/// <param name="title">The title.</param>
	/// <param name="text">The explanation.</param>
	private static void Title(string title, string text)
	{
		Console.WriteLine();
		Write(ConsoleColor.Cyan, $"== {title} ==");
		Wrap(text, "   ");
	}

	/// <summary>Prints the next numbered chapter heading and its explanation.</summary>
	/// <param name="title">The chapter title.</param>
	/// <param name="text">What the chapter shows and why it matters.</param>
	private void Chapter(string title, string text)
	{
		Console.WriteLine();
		Write(ConsoleColor.Cyan, $"{++_chapter}. {title}");
		Wrap(text, "   ");
	}

	/// <summary>Prints the call a step makes, marked <c>&gt;</c>.</summary>
	/// <param name="code">The call, as the reader would write it.</param>
	private static void Call(string code) => Write(ConsoleColor.DarkGray, $"   > {code}");

	/// <summary>Prints what came back, marked <c>&lt;</c>.</summary>
	/// <param name="text">The result in plain words.</param>
	private static void Result(string text) => Write(ConsoleColor.Green, $"   < {text}");

	/// <summary>Prints an explanatory note, marked <c>i</c>.</summary>
	/// <param name="text">The note.</param>
	private static void Note(string text) => Wrap(text, "   i ");

	/// <summary>
	/// Prints a page in plain words, by kind: a continuable page with the tail of its next token (tokens share a
	/// recognisable prefix, so the end is what differs) or a note that it is the last page; a counted page as "page N of
	/// M" with its total; a capped page with whether more matched.
	/// </summary>
	/// <typeparam name="T">The item type; its <c>ToString</c> is printed.</typeparam>
	/// <param name="page">The page to print.</param>
	/// <param name="label">An optional prefix such as <c>page 2</c>.</param>
	private static void Show<T>(Page<T> page, string? label = null)
	{
		var prefix = label is null ? "" : $"{label}: ";
		var items = page.ItemList.Count == 0 ? "(no items)" : string.Join(", ", page.ItemList);

		Result(page switch
		{
			ContinuablePage<T> c => $"{prefix}continuable, size {c.Size}: {items}"
				+ (c.NextPageToken is { } token ? $"\n     next token: ...{token.Value[^16..]} ({token.Value.Length} chars, encrypted, different every page)" : "\n     no next token: this is the last page"),
			CountedPage<T> c => $"{prefix}counted, page {c.Page} of {Math.Max(1, (c.TotalCount + c.Size - 1) / c.Size)} ({c.TotalCount} in total, size {c.Size}): {items}",
			CappedPage<T> c => $"{prefix}capped at {c.Cap}, {(c.Truncated ? "more matched" : "nothing more matched")}: {items}",
			_ => items
		});
	}

	/// <summary>
	/// Runs a call that should be refused and prints the <see cref="PagingException"/> it raised, or a red warning if it
	/// was served. Catching <see cref="PagingException"/> also catches <see cref="InvalidPageTokenException"/>, which
	/// derives from it.
	/// </summary>
	/// <param name="read">The call expected to throw.</param>
	/// <returns>A task that completes when the outcome has been printed.</returns>
	private static async Task Refused(Func<Task> read)
	{
		try
		{
			await read();
			Write(ConsoleColor.Red, "   < served, but should have been refused");
		}
		catch (PagingException exception)
		{
			Write(ConsoleColor.Yellow, $"   x refused with {exception.GetType().Name}: {exception.Message}");
		}
	}

	/// <summary>Prints one line in a colour and resets the colour.</summary>
	/// <param name="color">The foreground colour.</param>
	/// <param name="text">The line.</param>
	private static void Write(ConsoleColor color, string text)
	{
		Console.ForegroundColor = color;
		Console.WriteLine(text);
		Console.ResetColor();
	}

	/// <summary>Prints text wrapped at about 112 characters, the first line after <paramref name="indent"/> and the rest aligned under it.</summary>
	/// <param name="text">The text to wrap.</param>
	/// <param name="indent">The prefix of the first line; its length indents the following lines.</param>
	private static void Wrap(string text, string indent)
	{
		var line = indent;

		foreach (var word in text.Split(' '))
		{
			if (line.Length + word.Length > 112)
			{
				Console.WriteLine(line.TrimEnd());
				line = new string(' ', indent.Length);
			}

			line += word + " ";
		}

		Console.WriteLine(line.TrimEnd());
	}
}
