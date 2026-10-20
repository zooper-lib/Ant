using Zooper.Ant;

namespace Zooper.Ant.Example;

/// <summary>
/// The tour's reads over HTTP, as minimal API endpoints. Run with <c>dotnet run -- --serve</c> and send the requests
/// in Zooper.Ant.Example.http.
/// </summary>
/// <remarks>
/// <para>
/// The endpoints stay thin: they bind the query string, build a <see cref="Paging"/> from the host's
/// <see cref="Pager.Limits"/> with <see cref="PagingQuery.ToPaging"/>, and hand both to the same
/// <see cref="PlayerQueries"/> the tour uses. A returned <see cref="Page{TItem}"/> is serialised by its runtime kind,
/// so the JSON carries <c>nextPageToken</c>, <c>page</c>/<c>totalCount</c> or <c>cap</c>/<c>truncated</c> as fits.
/// </para>
/// <para>
/// Every <see cref="PagingException"/> thrown along the way becomes a 400 through <see cref="PagingExceptionHandler"/>.
/// </para>
/// </remarks>
public static class Api
{
	/// <summary>Maps <c>GET /players</c>, <c>GET /players/export</c>, <c>POST /players</c> and <c>GET /activity</c>.</summary>
	/// <param name="app">The application built in Program.cs.</param>
	public static void MapPlayerApi(this WebApplication app)
	{
		// GET /players: one endpoint, three kinds. The caller picks with ?kind= and the arguments of that kind:
		//   continuable: token, size     counted: page, size     capped: cap
		// The filter (team, minRating, sort, reverse) is bound separately; it decides which players the list contains
		// and therefore goes into the token's scope inside PlayerQueries.
		app.MapGet("/players", (
				[AsParameters] PlayerFilter filter,
				[AsParameters] PagingQuery paging,
				PlayerQueries queries,
				Pager pager,
				CancellationToken ct) =>
			queries.ListAsync(filter, paging.ToPaging(pager.Limits), ct));

		// GET /players/export: every matching player as one JSON array. Pager.TraverseAsync returns an
		// IAsyncEnumerable, which ASP.NET Core streams: each page is read only when the response writer reaches it, so the
		// full list is never held in memory. Each page uses the largest allowed size to keep the number of reads low.
		app.MapGet("/players/export", ([AsParameters] PlayerFilter filter, PlayerQueries queries, Pager pager, CancellationToken ct) =>
			pager.TraverseAsync<PlayerView>(
				(token, ct) => queries.ListAsync(filter, Paging.Continuable(token, pager.Limits.MaximumSize, pager.Limits), ct),
				ct));

		// POST /players: adds a player, to show that pages read live data. Continue an earlier read afterwards and the new
		// player appears on a later page when it sorts after the token's position.
		app.MapPost("/players", (NewPlayer player, PlayerStore store) => Results.Created("/players", store.Add(player)));

		// GET /activity: a store with its own cursors, paged through a hand-written IPageSource. There is no LINQ here, so
		// Pager.ReadAsync is called directly with the source. The scope has no filter values because the read has none.
		// Continuable and capped work; counted is a 400 because the store cannot count.
		app.MapGet("/activity", ([AsParameters] PagingQuery paging, ActivityLog log, Pager pager, CancellationToken ct) =>
			pager.ReadAsync(new ActivityLogPageSource(log), paging.ToPaging(pager.Limits), PageScope.For("ListActivity"), ct));
	}
}
