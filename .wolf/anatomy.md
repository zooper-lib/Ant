# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-10-21T08:56:41.565Z
> Files: 29 tracked | Anatomy hits: 0 | Misses: 0

> Project structure index. Auto-maintained by OpenWolf hooks and daemon.
> Run `openwolf scan` to generate, or wait for the first Claude Code session.
> Status: Pending initial scan

## ./

- `AGENTS.md` — OpenWolf (~75 tok)
- `CLAUDE.md` — OpenWolf (~99 tok)
- `paging-package-brief.md` — Paging Package Brief (~33492 tok)
- `README.md` — Project documentation (~3279 tok)

## .github/workflows/

- `nuget.publish.yml` — CI: Build and Publish (~938 tok)

## Zooper.Ant.Example/

- `Players.cs` — What the API returns: pages are mapped from <see cref="Player"/> with <see cref="Page{TItem}.Map{TOu (~975 tok)

## Zooper.Ant.Tests/

- `OrderTests.cs` — Verifies how an <see cref="Order{T}"/> chain is declared and described (spec <c>keyset-ordering</c>) (~959 tok)
- `PagerTests.cs` — Verifies the kind switch of <see cref="Pager.ReadAsync{TItem}"/> against a mocked <see cref="IPageSo (~3037 tok)
- `PageSourceContractShapeTests.cs` — Verifies that <see cref="IPageSource{TItem}"/> is a plain interface any store can implement, here by (~313 tok)
- `QueryablePageSourceTests.cs` — Verifies that <see cref="QueryablePageSource{T}"/> builds the ordered, filtered and sought query its (~1192 tok)
- `SeekExpressionBuilderTests.cs` — Verifies the LINQ order-and-seek builder that every LINQ store receives (spec <c>keyset-ordering</c> (~3798 tok)
- `TraverseAsyncTests.cs` — Verifies <see cref="Pager.TraverseAsync{TItem}"/> against a scripted page read: it follows tokens to (~1679 tok)

## Zooper.Ant/

- `CanonicalJson.cs` — The one set of JSON options for scope fingerprints and token payloads. Pinned explicitly rather than (~646 tok)
- `Pager.cs` — The single paging service of Zooper.Ant: serves every page kind from any <see cref="IPageSource{TIte (~3969 tok)
- `PageScope.cs` — What a continuation token is bound to: the read that issues it and the values that define its result (~1576 tok)

## Zooper.Ant/DependencyInjection/

- `AntOptions.cs` — Host configuration of Zooper.Ant, set through <see cref="ServiceCollectionExtensions.AddAnt"/>. (~525 tok)
- `ServiceCollectionExtensions.cs` — Registers Zooper.Ant in an <see cref="IServiceCollection"/>. (~1093 tok)

## Zooper.Ant/Sources/

- `IPageSource.cs` — The floor a store implements to be paged: one ordered, filtered result set. <see cref="Pager"/> buil (~1974 tok)
- `IQueryExecutor.cs` — How a LINQ provider runs a query: to a list and to a count. This is the only thing a LINQ store adap (~1321 tok)
- `Order.cs` — The order of a LINQ read, declared once as a chain of typed keys ending in a unique tie-breaker. Bot (~3839 tok)
- `PagePosition.cs` — Where a page ended, as an ordered list of JSON-serialisable values the source defines and reads back (~532 tok)
- `QueryableExtensions.cs` — Pages items held in memory with one call at the end of a LINQ query, through the same order-and-seek (~1347 tok)
- `QueryablePageSource.cs` — The page source of any LINQ provider: orders and seeks a query by an <see cref="Order{T}"/> and runs (~1574 tok)
- `SeekExpressionBuilder.cs` — Turns an <see cref="Order{T}"/> into LINQ expressions: the ORDER BY of a page and the keyset seek pr (~5717 tok)

## Zooper.Ant/Tokens/

- `DataProtectionPageTokenProtector.cs` — The default <see cref="IPageTokenProtector"/>: ASP.NET Core Data Protection encrypts and integrity-p (~1739 tok)
- `IPageTokenProtector.cs` — Protects a page position as an opaque <see cref="PageToken"/>, and opens it again only for the same  (~964 tok)

## openspec/

- `config.yaml` (~286 tok)

## openspec/changes/archive/

- `.gitkeep` (~0 tok)

## openspec/specs/

- `.gitkeep` (~0 tok)
