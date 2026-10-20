# Zooper.Ant.Example

A guided tour of every Zooper.Ant feature over twelve players you can follow by eye.

```bash
dotnet run --project Zooper.Ant.Example
```

The tour prints numbered chapters. Each chapter says what it shows, the call it makes (`>`), what came back (`<`), and every refusal (`x`):

```
1. Continuable pages, the default kind
   > queries.ListAsync(new PlayerFilter(), Paging.Continuable(null, size: 3))
   < page 1: continuable, size 3: Eve (blue, 95), Ada (red, 91), Ivy (green, 91)
     next token: ...Xb3nQ2rTt9kLmA1c (219 chars, encrypted, different every page)
```

| # | Chapter | What you see |
| --- | --- | --- |
| 1 | Continuable pages | Following next tokens page by page until there is none |
| 2 | Counted pages | "page 2 of 4" with the total |
| 3 | Capped pages | A top 3 and whether more matched |
| 4 | One read, three kinds | The same read answering all kinds in the same order |
| 5 | Sizes are bounded | The default size, and a size of 500 clamped to 10 |
| 6 | Counted depth is bounded | A page too deep, refused |
| 7 | Orders, ties and reversal | Unrated players last, ties broken by Id across a page boundary, `Reverse()` |
| 8 | Tokens belong to their read | Another size is fine; another team or order is refused |
| 9 | Tamper-proof tokens | One changed character, refused; a malformed token, refused |
| 10 | Live data | A deleted and an added player between two pages |
| 11 | Mapping and empty pages | `Map` keeps the kind; `ToEmptyPage` without reading |
| 12 | `TraverseAsync` | All players, one page read at a time |
| 13 | Any store | A cursor-based store via `IPageSource<T>`: continuable works, counted is refused |

## Where to look

| File | Shows |
| --- | --- |
| `Program.cs` | `AddDataProtection`, `AddAnt` with limits and purpose root |
| `Players.cs` | `PlayerQueries.ListAsync`: a LINQ query ending in `ToPageAsync`, the `Order<T>`, the `PageScope`, `Map`, `ToEmptyPage` |
| `Activity.cs` | `ActivityLogPageSource`: paging a store that has its own cursors and cannot count |
| `Tour.cs` | Every chapter, in reading order |
| `Api.cs`, `PagingQuery.cs`, `PagingExceptionHandler.cs` | The same reads over HTTP: picking the kind from the query string, refusals as 400 problem responses |

## Over HTTP

```bash
dotnet run --project Zooper.Ant.Example -- --serve
```

Then send the requests in `Zooper.Ant.Example.http` in order. They carry the token from one request to the next themselves; nothing needs copying.
