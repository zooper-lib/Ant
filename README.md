# Zooper.Ant

<img src="icon.png" alt="Zooper.Ant Logo" width="120" align="right"/>

[![NuGet Version](https://img.shields.io/nuget/v/Zooper.Ant.svg)](https://www.nuget.org/packages/Zooper.Ant/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![GitHub](https://img.shields.io/badge/GitHub-zooper--lib%2FAnt-181717?logo=github)](https://github.com/zooper-lib/Ant)

Caller-selected paging for .NET: continuable, counted and capped pages over any store, with keyset continuation behind encrypted tokens.

## Overview

Every list endpoint needs paging, and every team writes it again: offsets that skip rows when data changes, totals nobody asked for, cursors that leak database values, tokens that can be replayed against another query. Zooper.Ant writes it once.

A read is written once and serves three page kinds, chosen by the caller per request. Continuation is a keyset seek, never an offset, so it stays correct while rows are inserted and deleted, and costs the same on page 1000 as on page 2. Tokens are encrypted with ASP.NET Core Data Protection, bound to the read and its filters, and never expire. Any store can be paged: LINQ stores supply two methods, every other store implements one small interface.

`Zooper.Ant` depends on `Microsoft.AspNetCore.DataProtection.Abstractions` and `Microsoft.Extensions.Options` only. The companion package `Zooper.Ant.AspNetCore` adds the HTTP edge: query binding, 400 problem responses and the OpenAPI description of pages.

## Key Concepts

| Term | Description |
|------|-------------|
| **Paging** | The caller's instruction: which kind of page and how far. Built with `Paging.Continuable`, `Paging.Counted` or `Paging.Capped`, which apply the limits. |
| **Page** | The result: `ContinuablePage<T>`, `CountedPage<T>` or `CappedPage<T>`, all deriving from `Page<T>`. |
| **PageToken** | The opaque, encrypted continuation of a continuable page. Callers pass it back unchanged. |
| **Order** | The order of a read, declared once as typed keys ending in a unique tie-breaker. It sorts the page and drives the seek. |
| **PageScope** | The read's name plus every argument that decides its result set. A token is only accepted under the same scope. |
| **Pager** | The one service you inject. It runs the page kinds, issues tokens and refuses foreign ones. |
| **PagingLimits** | Default size, maximum size and counted depth, configured once per host. |
| **IPageSource** | The interface a store implements to be paged. LINQ stores get one for free through `IQueryExecutor`. |

## Installation

```bash
dotnet add package Zooper.Ant
dotnet add package Zooper.Ant.AspNetCore   # ASP.NET Core hosts: binding, problem responses, OpenAPI
```

## Getting Started

Register Ant once. The host owns Data Protection; Ant uses it for tokens.

```csharp
builder.Services.AddDataProtection();
builder.Services.AddAnt();
```

Inject `Pager` and end a LINQ query with `ToPageAsync`:

```csharp
public sealed class PlayerQueries(PlayerStore store, Pager pager)
{
    public async Task<Page<PlayerView>> ListAsync(string? team, Paging paging, CancellationToken ct)
    {
        var page = await store.Players
            .Where(p => team == null || p.Team == team)
            .ToPageAsync(
                paging,
                Order<Player>.ByDescending(p => p.Rating).ThenBy(p => p.Id),   // order, unique Id last
                PageScope.For(nameof(ListAsync), team),                         // the read and its filters
                pager,
                ct);

        return page.Map(PlayerView.From);                                       // keeps the page kind
    }
}
```

The caller picks the kind:

```csharp
await queries.ListAsync("red", Paging.Continuable(pageToken: null, size: 20), ct); // first page + next token
await queries.ListAsync("red", Paging.Continuable(nextToken, size: 20), ct);     // the page after
await queries.ListAsync("red", Paging.Counted(page: 3, size: 20), ct);           // page 3 of N, with the total
await queries.ListAsync("red", Paging.Capped(cap: 5), ct);                       // top 5, and whether more matched
```

## Example Project

`Zooper.Ant.Example` plays a narrated tour of every feature over twelve players, printing each call and what came back:

```bash
dotnet run --project Zooper.Ant.Example
```

```
1. Continuable pages, the default kind
   > queries.ListAsync(new PlayerFilter(), Paging.Continuable(null, size: 3))
   < page 1: continuable, size 3: Eve (blue, 95), Ada (red, 91), Ivy (green, 91)
     next token: ...ZVCLsuw0xZLyoILU (219 chars, encrypted, different every page)
```

With `-- --serve` it serves the same reads over HTTP through `Zooper.Ant.AspNetCore` (the `?paging=` parameters, 400 problem responses, `/openapi/v1.json`); `Zooper.Ant.Example.http` walks through them. See [`Zooper.Ant.Example/README.md`](Zooper.Ant.Example/README.md).

## Page Kinds

| Kind | The caller gets | Cost | Use for |
|------|-----------------|------|---------|
| **Continuable** (default) | items, and a `NextPageToken` while more exists | the page plus one row | infinite scroll, "load more", sync jobs |
| **Counted** | items, `Page`, `Size` and `TotalCount` | a count of every match, plus an offset | "page 3 of 12" with jumps |
| **Capped** | items, `Cap` and `Truncated` | the cap plus one row | a top-N widget |

### Continuable

```csharp
var page = (ContinuablePage<PlayerView>)await queries.ListAsync(null, Paging.Continuable(token, 20), ct);

page.ItemList;        // the players
page.NextPageToken;   // null on the last page; the only end signal
```

The token marks a position in the order ("after Ivy, rating 91, id 9"), so a row deleted before it does not shift the next page and a row added after it shows up later. The page size may change between pages.

### Counted

```csharp
var page = (CountedPage<PlayerView>)await queries.ListAsync(null, Paging.Counted(page: 2, size: 20), ct);

page.Page;         // 2
page.TotalCount;   // every match, across all pages
```

A page deeper than `MaximumDepth` rows is refused with a `PagingException`, never replaced by another page. Use continuable paging to go further.

### Capped

```csharp
var page = (CappedPage<PlayerView>)await queries.ListAsync("red", Paging.Capped(cap: 3), ct);

page.Truncated;   // true when more than 3 matched
```

### Mapping and Empty Pages

`page.Map(...)` maps the items and keeps the kind with its page number, total or token. A filter known to match nothing can answer without reading:

```csharp
if (minRating > 100)
    return paging.ToEmptyPage<PlayerView>();
```

## Limits

```csharp
builder.Services.AddAnt(options =>
    options.Limits = new PagingLimits(DefaultSize: 20, MaximumSize: 50, MaximumDepth: 1000));   // the defaults

Paging.Continuable(token, size, pager.Limits);   // build Paging from the configured limits
```

| Request | Result |
|---------|--------|
| no size, or a size below 1 | `DefaultSize` |
| a size or cap above `MaximumSize` | clamped to `MaximumSize` and served |
| counted page below 1 | `PagingException` |
| counted `page × size` above `MaximumDepth` | `PagingException` |
| inconsistent limits | the host fails at startup |

## Ordering

```csharp
Order<Player>.ByDescending(p => p.Rating)           // nullable: unrated players sort last
    .ThenBy(p => p.Name)
    .ThenBy(p => p.Id);                              // unique tie-breaker, always last

Order<Player>.By(p => p.Rating, nullsFirst: true);  // nulls first instead
order.Reverse();                                     // every direction and nulls placement flipped
```

- **End with a unique key.** Rows with equal values are then still in a fixed order, so a page boundary between them can never skip or repeat one.
- **Keys are typed lambdas.** No strings: renaming a property is a compile error.
- **Mixed directions work.** Each key seeks in its own direction.
- **Timestamps are truncated to microseconds** in the token, the precision of PostgreSQL and most stores.

## Tokens

```csharp
PageScope.For(nameof(ListAsync), team, minRating)
```

The scope names the read and lists every argument that decides its result set. A token is refused when:

- it is presented to another read,
- any scope value differs (`team=red` token used with `team=blue`),
- the order changed,
- a character was altered, or the keys that encrypted it are gone.

Never put the page size in the scope; it may change between pages. Pass scalars and records.

Tokens have no lifetime. A host with several instances must share one Data Protection key ring (`PersistKeysToFileSystem`, a database, Redis), otherwise a token from one instance is refused by another. The purpose root defaults to `Zooper.Ant.PageToken`; changing it invalidates every token in flight:

```csharp
builder.Services.AddAnt(options => options.PurposeRoot = "MyService.PageToken");
```

To replace the encryption, register your own `IPageTokenProtector` before `AddAnt`.

`PageToken` implements `IParsable<PageToken>` and carries JSON and type converters, so it binds from a query string and serialises as a plain string.

## Reading Everything

`TraverseAsync` follows the tokens for you. It reads the next page only when you reach it, keeps no snapshot, and ends with the error of a failed page:

```csharp
await foreach (var player in pager.TraverseAsync<PlayerView>(
    (token, ct) => queries.ListAsync("red", Paging.Continuable(token, 100), ct), ct))
{
    await Sync(player);
}
```

Data is live: a row whose ordering value changes during the traversal may be skipped or seen twice. Order by an immutable key when that matters.

## Error Handling

| Exception | Raised when |
|-----------|-------------|
| `PagingException` | page below 1, depth beyond the limit, malformed token, counted kind on a store that cannot count |
| `InvalidPageTokenException` | a token from another read, other arguments, another order, altered, or unreadable. Derives from `PagingException`. |

Map one type to one response. An ASP.NET Core host gets that mapping from `Zooper.Ant.AspNetCore`, below.

## Hosting in ASP.NET Core

`Zooper.Ant.AspNetCore` is the HTTP edge, so no host writes it again. Every Ant host speaks the same five query parameters:

| Parameter | Value | Kinds |
|-----------|-------|-------|
| `paging` | `continuable`, `counted` or `capped`, any case; absent means `continuable` | all |
| `pageToken` | the previous page's `nextPageToken` | continuable |
| `page` | one-based page number | counted |
| `size` | page size, defaulted and clamped by the limits | continuable, counted |
| `cap` | cap, defaulted and clamped like a size | capped |

An argument of another kind (`?paging=capped&cap=5&page=2`) is refused, never ignored. An unknown `paging` value or a malformed `pageToken` is a 400 before the endpoint runs. The names are fixed: a host whose API already uses other names binds its own parameters and calls the `Paging` factories with `pager.Limits`.

Bind `PagingRequest` beside your own filter and pass the host's limits:

```csharp
app.MapGet("/players", ([AsParameters] PlayerFilter filter, [AsParameters] PagingRequest paging, PlayerQueries queries, Pager pager, CancellationToken ct) =>
    queries.ListAsync(filter, paging.ToPaging(pager.Limits), ct));

// In an [ApiController]:
public Task<Page<PlayerView>> List([FromQuery] PlayerFilter filter, [FromQuery] PagingRequest paging, CancellationToken ct) =>
    queries.ListAsync(filter, paging.ToPaging(pager.Limits), ct);
```

Two independent one-liners do the rest; the binding needs none:

```csharp
builder.Services.AddAntExceptionHandler();                               // PagingException → 400 problem response
builder.Services.AddOpenApi(options => options.AddAntPageResponses());   // Page<T> → oneOf the three page shapes

app.UseExceptionHandler();                                               // required, as for every IExceptionHandler
app.MapOpenApi();
```

The problem's `type` is `urn:zooper:ant:invalid-page-token` for a refused token (a client restarts from the first page) and `urn:zooper:ant:invalid-paging` for every other refusal; the `detail` is the refusal's message. A host with its own error convention skips `AddAntExceptionHandler` and maps `PagingException` itself. Controllers need `[ApiController]`, which turns a failed parse into a 400; without it an unknown `paging` would bind as absent.

## Adding a Store

**A LINQ provider** implements `IQueryExecutor`, two methods. `QueryablePageSource<T>` builds the ORDER BY and the seek from the `Order<T>`:

```csharp
public sealed class MyProviderExecutor : IQueryExecutor
{
    public Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct) => ...;
    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct) => ...;
}

var page = await pager.ReadAsync(new QueryablePageSource<Player>(query, order, new MyProviderExecutor()), paging, scope, ct);
```

Pass `timestampPrecision` when the store keeps timestamps more precisely than microseconds.

**Any other store** implements `IPageSource<T>`. The Pager builds every kind from it and owns the tokens; the source never sees one:

```csharp
public interface IPageSource<TItem>
{
    string OrderSignature { get; }                                                    // changes when the order does
    Task<int> CountAsync(CancellationToken ct);                                       // counted kind
    Task<IReadOnlyList<TItem>> ReadAsync(int offset, int limit, CancellationToken ct);
    Task<IReadOnlyList<TItem>> ReadAfterAsync(PagePosition position, int limit, CancellationToken ct);
    PagePosition PositionOf(TItem item);                                              // goes into the token
    PagePosition ParsePosition(IReadOnlyList<JsonElement> payload);                   // comes out of it
}
```

A store with its own cursors (Elasticsearch `search_after`, Cosmos DB continuations, Dapr state queries) puts its cursor in the position and throws `NotSupportedException` from `CountAsync` and from `ReadAsync` with an offset. Continuable and capped pages work; the counted kind is refused with a `PagingException`. `ActivityLogPageSource` in the example project is a complete one.

## API Quick Reference

| Member | Purpose |
|--------|---------|
| `services.AddAnt(options => ...)` | Registers `Pager` and the token protector; `Limits`, `PurposeRoot` |
| `services.AddAntExceptionHandler()` | `Zooper.Ant.AspNetCore`: paging refusals as 400 problem responses; needs `app.UseExceptionHandler()` |
| `openApiOptions.AddAntPageResponses()` | `Zooper.Ant.AspNetCore`: `Page<T>` documented as `oneOf` the three page shapes |
| `[AsParameters]` / `[FromQuery] PagingRequest` + `ToPaging(pager.Limits)` | `Zooper.Ant.AspNetCore`: the five query parameters as a `Paging` |
| `Paging.Continuable(token, size, limits)` | First or next continuable page |
| `Paging.Counted(page, size, limits)` | Page by number, with total |
| `Paging.Capped(cap, limits)` | Top N, with truncation flag |
| `paging.ToEmptyPage<T>()` | Empty page of the requested kind |
| `query.ToPageAsync(paging, order, scope, pager, ct)` | Pages an in-memory `IQueryable<T>` or `IEnumerable<T>` |
| `pager.ReadAsync(source, paging, scope, ct)` | Pages any `IPageSource<T>` |
| `pager.TraverseAsync(readPage, ct)` | Every item of a read, page by page |
| `pager.Limits` | The configured `PagingLimits` |
| `page.Map(selector)` | Maps items, keeps the kind |
| `Order<T>.By / ByDescending / ThenBy / ThenByDescending / Reverse` | Declares the order |
| `PageScope.For(readName, values...)` | Binds tokens to a read and its filters |
| `PageToken.Parse / TryParse` | Validates a token's format |

## More from Zooper

Check out the other libraries on [GitHub](https://github.com/zooper-lib).

## Contributing

Contributions are welcome. Open an [issue](https://github.com/zooper-lib/Ant/issues) or submit a [pull request](https://github.com/zooper-lib/Ant/pulls).

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

Made with ❤️ by the Zooper team
