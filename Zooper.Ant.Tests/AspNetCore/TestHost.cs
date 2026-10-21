using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Zooper.Ant.AspNetCore;
using Zooper.Ant.AspNetCore.DependencyInjection;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests.AspNetCore;

/// <summary>An item of the test hosts' list.</summary>
/// <param name="Id">Unique, so it ends the order.</param>
/// <param name="Name">Anything.</param>
public sealed record Item(int Id, string Name);

/// <summary>A host's own filter, bound beside the paging arguments to show they do not collide.</summary>
public sealed class ItemFilter
{
	/// <summary>A filter value from <c>?team=</c>; echoed back so a test sees it bound.</summary>
	[FromQuery(Name = "team")]
	public string? Team { get; init; }
}

/// <summary>What an endpoint bound, as JSON, so a test can read the <see cref="Paging"/> back.</summary>
/// <param name="Kind">The instruction's kind.</param>
/// <param name="Size">Continuable and counted.</param>
/// <param name="Page">Counted.</param>
/// <param name="Cap">Capped.</param>
/// <param name="PageToken">Continuable.</param>
/// <param name="Team">The filter value bound beside the paging.</param>
public sealed record BoundPaging(string Kind, int? Size, int? Page, int? Cap, string? PageToken, string? Team)
{
	/// <summary>Describes <paramref name="paging"/> and the filter.</summary>
	/// <param name="paging">The built instruction.</param>
	/// <param name="filter">The bound filter.</param>
	/// <returns>The description.</returns>
	public static BoundPaging From(Paging paging, ItemFilter filter) =>
		paging switch
		{
			ContinuablePaging c => new("continuable", c.Size, null, null, c.PageToken?.Value, filter.Team),
			CountedPaging c => new("counted", c.Size, c.Page, null, null, filter.Team),
			CappedPaging c => new("capped", null, null, c.Cap, null, filter.Team),
			_ => throw new InvalidOperationException()
		};
}

/// <summary>Counts endpoint and action invocations, so a test can show a 400 came before the endpoint ran.</summary>
public sealed class Hits
{
	private int _count;

	/// <summary>How many times an endpoint ran.</summary>
	public int Count => _count;

	/// <summary>Records one run.</summary>
	public void Add() => Interlocked.Increment(ref _count);
}

/// <summary>
/// A real ASP.NET Core pipeline on <see cref="TestServer"/>, with Zooper.Ant registered under custom limits. Built as a
/// Minimal API host or an MVC host; disposed by the test.
/// </summary>
/// <remarks>
/// The limits (default 25, maximum 100, depth 500) differ from <see cref="PagingLimits.Default"/> in every bound, so a
/// test can tell the host's limits from the built-in ones.
/// </remarks>
public sealed class TestHost : IAsyncDisposable
{
	/// <summary>The host's limits.</summary>
	public static readonly PagingLimits Limits = new(DefaultSize: 25, MaximumSize: 100, MaximumDepth: 500);

	/// <summary>The list every paged endpoint reads.</summary>
	private static readonly Item[] ItemList = Enumerable.Range(1, 30).Select(id => new Item(id, $"Item {id}")).ToArray();

	private readonly WebApplication _app;

	private TestHost(WebApplication app)
	{
		_app = app;
		Client = app.GetTestClient();
	}

	/// <summary>A client that talks to the host in-process.</summary>
	public HttpClient Client { get; }

	/// <summary>How many times an endpoint or action ran.</summary>
	public Hits Hits => _app.Services.GetRequiredService<Hits>();

	/// <summary>Builds and starts the Minimal API host.</summary>
	/// <param name="withExceptionHandler">Whether to call <c>AddAntExceptionHandler()</c>; without it a paging refusal is a 500.</param>
	/// <returns>The started host.</returns>
	public static async Task<TestHost> StartMinimalAsync(bool withExceptionHandler = true)
	{
		var builder = CreateBuilder();

		// Without the Ant handler the host still has the stock problem handling, as a host with its own would.
		if (withExceptionHandler)
			builder.Services.AddAntExceptionHandler();
		else
			builder.Services.AddProblemDetails();

		builder.Services.AddOpenApi(options => options.AddAntPageResponses());

		var app = builder.Build();

		app.UseExceptionHandler();

		app.MapOpenApi();

		// The binding probe: echoes what was bound.
		app.MapGet("/items", ([AsParameters] ItemFilter filter, [AsParameters] PagingRequest paging, Pager pager, Hits hits) =>
		{
			hits.Add();
			return BoundPaging.From(paging.ToPaging(pager.Limits), filter);
		});

		// Real reads, declared as the abstract page: the transformer rewrites both, sharing the three schemas.
		app.MapGet("/players", ([AsParameters] PagingRequest paging, Pager pager, CancellationToken ct) => ReadAsync(paging, pager, ct));
		app.MapGet("/players/again", ([AsParameters] PagingRequest paging, Pager pager, CancellationToken ct) => ReadAsync(paging, pager, ct));

		// A paged read that also documents its 400; only its 200 is rewritten.
		app.MapGet("/players/checked", ([AsParameters] PagingRequest paging, Pager pager, CancellationToken ct) => ReadAsync(paging, pager, ct))
			.ProducesProblem(StatusCodes.Status400BadRequest);

		// Responses the transformer must leave alone.
		app.MapGet("/players/continuable", () => (ContinuablePage<Item>)Paging.Continuable(null, null).ToEmptyPage<Item>());
		app.MapGet("/player", () => ItemList[0]);

		await app.StartAsync();
		return new TestHost(app);
	}

	/// <summary>Builds and starts the MVC host, serving <see cref="ItemsController"/> from this assembly.</summary>
	/// <returns>The started host.</returns>
	public static async Task<TestHost> StartMvcAsync()
	{
		var builder = CreateBuilder();
		builder.Services.AddControllers().AddApplicationPart(typeof(TestHost).Assembly);

		var app = builder.Build();
		app.MapControllers();

		await app.StartAsync();
		return new TestHost(app);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		Client.Dispose();
		await _app.DisposeAsync();
	}

	/// <summary>The registrations both hosts share: a test server, an in-memory key ring and Zooper.Ant.</summary>
	private static WebApplicationBuilder CreateBuilder()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		// Ephemeral keys: nothing written to disk, and tokens die with the host, which is all a test needs.
		builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
		builder.Services.AddAnt(options => options.Limits = Limits);
		builder.Services.AddSingleton<Hits>();
		return builder;
	}

	/// <summary>Pages <see cref="ItemList"/> by id.</summary>
	private static Task<Page<Item>> ReadAsync(PagingRequest paging, Pager pager, CancellationToken ct) =>
		ItemList.ToPageAsync(paging.ToPaging(pager.Limits), Order<Item>.By(item => item.Id), PageScope.For("ListItems"), pager, ct);
}

/// <summary>The MVC host's list endpoint: the same binding as <c>/items</c>, as a controller action.</summary>
[ApiController]
[Route("items")]
public sealed class ItemsController(Pager pager, Hits hits) : ControllerBase
{
	/// <summary>Echoes what was bound.</summary>
	/// <param name="filter">The host's own filter.</param>
	/// <param name="paging">The paging arguments.</param>
	/// <returns>The bound instruction and filter.</returns>
	[HttpGet]
	public BoundPaging Get([FromQuery] ItemFilter filter, [FromQuery] PagingRequest paging)
	{
		hits.Add();
		return BoundPaging.From(paging.ToPaging(pager.Limits), filter);
	}
}
