# Ant Extensibility Design

Status: decision record, built up during the design interview of 2026-10-20. Complements `paging-package-brief.md`; where they disagree, this document wins. Each decision states what it settles, why, and what it rules out.

## Cross-cutting constraint: easy to use

Every decision below is judged against two tests, in this order:

1. A consumer on a common stack (EF Core, Marten, Npgsql, Dapper) declares one order chain and makes one call per read. They never write a seek predicate, an ORDER BY, a token payload or a kind switch.
2. A third-party adapter author for an uncommon store implements the smallest possible floor and tests it themselves.

Rationale: stated by the developer on 2026-10-20. Rules out any design that is maximally general at the cost of a longer call site for the common case.

## Decision 1: Layered store seam

**Settled.** The store seam has two layers.

**The floor.** `IPageSource<T>` is the only thing core's `PageReader` knows. Any store can implement it directly. Its position type, `PagePosition`, is an ordered list of JSON-serialisable values that the source itself defines and reads back. A keyset source puts its chain values there. A native-cursor store (Dapr state query, DynamoDB, Elasticsearch `search_after`) puts its own continuation string there as a single value; core protects it like any other position and never inspects it.

**The builders.** Core also ships two reusable builders that produce ready `IPageSource<T>` implementations from one declared order chain, so that ORDER BY and the seek predicate come from the same declaration and cannot drift:

- an expression-tree builder for `IQueryable<T>` providers (EF Core, Marten, MongoDB driver, Cosmos SDK, in-memory). An adapter supplies only an async executor: run a query to a list, run a count.
- a SQL-text builder for raw-statement providers (Npgsql, Dapper, SQL Server, SQLite ADO.NET). An adapter supplies a dialect and a command runner.

Both builders are dependency-free: `System.Linq.Expressions` is BCL, and the text builder emits strings.

**Why.** Under the brief's original contract every adapter wrote its own `TakeAfterAsync`, meaning its own expanded predicate `(c1 > v1) OR (c1 = v1 AND c2 > v2) OR …`, NULLS LAST handling, reversal and ORDER BY. All three Marten bugs Feng hit lived in that code, and nothing stopped ORDER BY and seek from disagreeing, which silently skips or repeats rows at page boundaries. Sources: ports and adapters (Cockburn) as already decided in the brief; MR.EntityFrameworkCore.KeysetPagination's `KeysetPaginationBuilder` for the expression builder; SeekKit's `.Core` / `.EntityFramework` / `.MongoDB` split; the dialect seam as used by Dapper.Contrib and SqlKata.

**Rules out.** Option A (the brief's single interface with adapters building their own predicates) as the whole contract. Option C (a LINQ-only seam), because raw Npgsql in milestone 3 and native-cursor stores do not fit.

**Affects the brief.** "Core Abstractions" and "Extensibility Contract: Adding A Store" must be rewritten: step 3 ("build `TakeAfterAsync` from the `OrderChain` with your technology's predicate form") becomes "pick a builder, or implement the floor for a store neither builder covers".

## Decision 2: Two order-chain flavours, one per builder family

**Settled.** A consumer declares the order in the natural form of their builder family. Core's `PageReader` never sees a chain, only `PagePosition`, so the two flavours cost nothing in core.

- LINQ family: typed lambdas, member name and value type inferred.
  `Order<Player>.By(p => p.CreatedAt).ThenByDescending(p => p.Score).ThenBy(p => p.Id)`
- SQL-text family: column or expression text plus the value type to read it back.
  `SqlOrder.By("created_at", SqlType.TimestampTz).ThenByDescending("round(score, 2) AS score_key", SqlType.Double).ThenBy("id", SqlType.Uuid)`

In both flavours the last key is the tie-breaker by position; there is no separate tie-breaker parameter. Nulls sort last by default with a per-key `NullsFirst` option.

**Why.** One untyped record for both families (the brief's `OrderKey<T>(string Name, Func<T, object?> Value, Type ValueType, …)`) makes the EF user type the member twice, once as a string that only fails at runtime, and still cannot feed an expression builder; a lambda-only key cannot express a raw-SQL computed criterion. Sources: MR.EntityFrameworkCore.KeysetPagination's builder (`b => b.Ascending(x => x.Created).Ascending(x => x.Id)`) and EF Core's own `OrderBy`/`ThenBy` for the LINQ form; the Dapper convention that SQL stays SQL for the text form.

**Rules out.** The brief's single `OrderKey<T>`/`OrderChain<T>` record. Lambdas-everywhere with a hidden member-to-column naming convention for SQL adapters (Dapper.FluentMap ships that as an opt-in add-on, not a default).

**Affects the brief.** "The Ordering Chain And The Position" is replaced by this section; "one position model" stays, "one chain model" does not.

## Decision 3: One injected `Pager`, query-first call sites

**Settled** (by delegation). `services.AddAnt(...)` registers one concrete sealed `Pager` holding the `IPageTokenProtector` and the `PagingLimits`. It is the only thing a consumer injects for paging.

- Common stack, query-first, so the page call ends a LINQ chain:
  `await db.Players.Where(...).ToPageAsync(paging, Order<Player>.By(p => p.CreatedAt).ThenBy(p => p.Id), scope, pager, ct)`
- SQL text: `await connection.ReadPageAsync(sql, bind, order, readRow, paging, scope, pager, ct)`
- Any store through the floor: `await pager.ReadAsync(mySource, paging, scope, ct)`

`Pager` has no interface: one implementation. Tests use the real `Pager` with the in-memory protector the package ships. `IPageTokenProtector` remains an interface so hosts can swap in an HMAC protector.

**Why.** The brief's static `PageReader.ReadAsync(source, paging, scope, protector, ct)` makes every handler inject the protector only to forward it, and leaves `AddAnt()` nothing to register; any later per-call need (limits override, clock, logging) would change every call site. Sources: LINQ terminal-method idiom (`ToListAsync`, Marten `ToPagedListAsync`, MR.Keyset `KeysetPaginateQuery`); the `Microsoft.Extensions` single-entry-service convention (`AddDataProtection` → `IDataProtectionProvider`); the project's ponytail rule against interfaces with one implementation.

**Rules out.** A static reader with the protector passed per call. Pager-first methods (`pager.ReadAsync(query, …)`) for the LINQ family, because they break the `Where` chain.

**Affects the brief.** "Core Abstractions" (`PageReader` static class) and the `PagingLimits` overload story: factories without `limits` resolve them from `Pager` in the AspNetCore binding, not from a static `PagingLimits.Default` alone.

## Decision 4: The package builds the token scope

**Settled.** `PageScope.For(readName, params object?[] values)` is the only way a consumer binds a token to its read. The read name is an explicit string. The values are serialised with `System.Text.Json` in a fixed, culture-free form; the adapter appends the declared order chain (member or column names plus directions) automatically; the result is hashed with SHA-256 so the token length does not grow with the arguments. Page size is never part of the scope, per the spec's "Changed size" scenario.

**Why.** Hand-rolled fingerprints (`$"{teamId}|{status}"`) are culture-dependent, forget arguments, and almost never include the ordering even though the spec names it as result-defining. The failure is silent: rows from the wrong result set behind a valid-looking token. Sources: SeekKit signs the serialised query descriptor; Google AIP-158 requires page tokens to be bound to the request parameters including ordering.

**Rules out.** Two consumer-supplied strings (Feng today and the brief's `Protect(readName, fingerprint, position)`). Deriving the read name from `[CallerMemberName]`: renames would invalidate live tokens and generic names such as `Handle` collide. Moving the fingerprint into the Data Protection purpose chain: same outcome as the tested payload comparison, not worth reopening.

**Affects the brief.** "Token Protection" payload stays `{ "q": fingerprint, "p": [values] }` but `q` is now the package-computed hash; `IPageTokenProtector` takes a `PageScope` instead of two strings; the Marten and Npgsql ports drop their `readName`/`fingerprint` parameters.

## Standing delegation

On 2026-10-20 the developer delegated every implementation-level question ("stop asking me those super detailed implementation questions ... I can rather answer the direction"). Decisions 5 onward marked *settled by standing delegation* were made by the agent under that instruction, each with a stated rationale, and are open to revision by the developer at any time.

## Decision 5: LINQ adapters supply a two-method executor; in-memory rides the same builder

**Settled by standing delegation.** No provider-neutral way exists to run an `IQueryable<T>` asynchronously: `ToListAsync` and `CountAsync` are provider-bound extension methods in EF Core, Marten and the MongoDB driver. So a LINQ-family adapter implements:

```csharp
public interface IQueryExecutor
{
    Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct);
    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct);
}
```

Its `ToPageAsync` extension method binds the executor, so the consumer selects the provider by the `using` they import. Three implementations ship (EF Core, Marten, in-memory), which justifies an interface.

The in-memory source is `collection.AsQueryable()` through the shared expression builder with a synchronous executor wrapped in `Task.FromResult`. It is not a separate implementation of the kinds as in Feng today. The contract-suite oracle therefore exercises the exact expression tree EF Core receives.

**Rules out.** Core calling `ToList()` itself (sync over async I/O). Runtime provider detection (`IAsyncEnumerable<T>` covers enumeration but nothing neutral covers count). Sources: SeekKit's per-provider packages own execution; MR.Keyset takes EF Core as a hard dependency because this seam is missing.

## Decision 6: The floor interface

**Settled by standing delegation.** Final shape of the floor, folding the brief's open "offset is a source concern" note into one method:

```csharp
public interface IPageSource<TItem>
{
    string OrderSignature { get; }                                                     // folded into the token binding; "" for a native-cursor source (added 2026-10-20 by the paging-core design)
    Task<int> CountAsync(CancellationToken ct);                                        // counted only
    Task<IReadOnlyList<TItem>> ReadAsync(int offset, int limit, CancellationToken ct); // counted (offset>0), capped and first page (offset 0)
    Task<IReadOnlyList<TItem>> ReadAfterAsync(PagePosition position, int limit, CancellationToken ct);
    PagePosition PositionOf(TItem item);
    PagePosition ParsePosition(IReadOnlyList<JsonElement> payload);                     // throws InvalidPageTokenException on shape mismatch
}
```

A native-cursor store (Dapr state query, DynamoDB, Elasticsearch) returns its own continuation as a one-value `PagePosition` from `PositionOf` and may throw `NotSupportedException` from `CountAsync` and from `ReadAsync` with `offset > 0`; `Pager` turns that into a `PagingException` saying the counted kind is not available on this read. Rules out a separate `SkipTakeAsync`.

## Decision 7: SQL-text builder behind a dialect

**Settled by standing delegation.** The text builder emits the ORDER BY fragment, the expanded seek predicate and the parameter list from a `SqlOrder` chain through an `ISqlDialect` that owns: identifier quoting, parameter placeholder syntax, nulls-last form (`NULLS LAST` on PostgreSQL; a `CASE WHEN x IS NULL` sort key on SQL Server, which lacks it), and the limit/offset form (`LIMIT … OFFSET` versus `OFFSET … FETCH NEXT`). The first release ships the PostgreSQL dialect only; the seam exists so SQL Server and SQLite are dialect files, not new builders. Generated parameter and alias names carry the prefix `__ant_` (`@__ant_p0`, `__ant_row`), following EF Core's `__p_0` convention, so they cannot collide with the caller's statement (brief, "What Must Change", item 7). The Npgsql adapter is the PostgreSQL dialect plus an `NpgsqlCommand` runner; a Dapper adapter would be the same dialect over `IDbConnection`.

## Decision 8: Packages, protector placement, test protector

**Settled by standing delegation.** Packages: `Zooper.Ant` (core, depends on `Microsoft.AspNetCore.DataProtection.Abstractions` and `Microsoft.Extensions.Options`), `Zooper.Ant.AspNetCore`, `Zooper.Ant.EntityFrameworkCore`, `Zooper.Ant.Marten`, `Zooper.Ant.Npgsql`. `DataProtectionPageTokenProtector` stays in core and is what `AddAnt()` registers; a fifth "protector" package buys nothing because the abstractions package is tiny and works in generic-host workers through `AddDataProtection()`. The HMAC protector remains a later addition behind `IPageTokenProtector`. The unencrypted `PlainTextPageTokenProtector` exists only inside `Zooper.Ant.Tests`; nothing unencrypted ships (decision 11). Resolves the brief's deferred "four or five packages" question.

## Decision 9: `PageToken` is a plain struct

**Settled by standing delegation.** `readonly record struct PageToken` with the existing validation (non-blank, base64url alphabet), a `System.Text.Json` converter, a `TypeConverter` for query-string binding and `IParsable<PageToken>` for Minimal API binding. Vogen is not a dependency of any package: the brief's reason (a consumer should not inherit Vogen's runtime and exception type from a paging package) plus the easy-to-use constraint. Resolves the brief's deferred "plain struct or Vogen" question.

## Decision 10: Exceptions and the ProblemDetails type

**Settled by standing delegation.** `PagingException` (refused argument) and `InvalidPageTokenException : PagingException` as in the brief. The AspNetCore package's opt-in `IExceptionHandler` answers 400 with RFC 9457 `ProblemDetails`; `type` is `urn:zooper:ant:invalid-paging` or `urn:zooper:ant:invalid-page-token` (fixed; "configurable" superseded by `ant-aspnetcore-companion.md` decision 5). URNs because they are stable without binding the package to a domain the owner must keep serving. Resolves the brief's deferred "`ProblemDetails` type URI" question.

## Decision 11: No shipped contract suite (superseded 2026-10-20)

**Settled by Daniel.** No `Zooper.Ant.Testing` package. Adapter authors test their own adapters. The in-memory source is still the oracle, verified by conformance tests inside `Zooper.Ant.Tests`. Features are shown in a runnable `Zooper.Ant.Example`, and README code only showcases usage.

**Rules out.** A shipped test package, a shipped unencrypted test protector, compiled README snippets.

## Decision 12: Registration and options

**Settled by standing delegation.** `services.AddAnt(options => { options.Limits = new PagingLimits(…); options.PurposeRoot = "…"; })` registers `Pager`, the Data Protection protector and validated `PagingLimits` (startup validation: `1 <= DefaultSize <= MaximumSize`, `MaximumDepth >= MaximumSize`). The AspNetCore package ships the request binding with fixed query names, the OpenAPI `oneOf` transformer and the opt-in exception handler (umbrella call and configurable names superseded by `ant-aspnetcore-companion.md` decisions 3 and 6). Adapter packages need no registration beyond their `using`. The traversal helper is `pager.TraverseAsync(token => readPage(token), ct)` as in the brief.

## Decision 13: First-release adapters

**Settled** by the developer. The first release ships in-memory, EF Core, Marten and Npgsql. EF Core moves into the first release because it is the headline adapter for outside users, the expression builder exists anyway for Marten and in-memory, and SQLite tests prove provider independence without infrastructure.

Dapr gets no shipped package. The README carries it as the worked example of implementing the floor (`IPageSource<T>`) by hand for a native-cursor store, labelled continuable-only: Dapr's state query API is alpha, sorts only on keys and offers no arbitrary seek, so a shipped adapter would promise the counted kind and keyset guarantees it cannot deliver. Dapper is not in the first release; it is a dialect reuse over `IDbConnection` when someone needs it.

**Rules out.** The brief's milestone order that puts EF Core after the first release. A `Zooper.Ant.Dapr` package.

**Affects the brief.** "Milestones" items 3 and 4 merge for the first release; "Package Split" gains nothing new (EF Core was already listed).

## Decision 14: Host bindings in the first release

**Settled** by the developer. `Zooper.Ant.AspNetCore` binds both MVC controllers (`[FromQuery] PagingRequest` plus `ToPaging`) and Minimal APIs (`PagingRequest` as an endpoint parameter through `BindAsync`; `PageToken` through `IParsable`). One request type, one foreign-argument refusal, one OpenAPI transformer for both. Minimal APIs are the default .NET 10 template, so leaving them to milestone 6 would make the most common new consumer re-implement query parsing on day one.

GraphQL (Relay `first`/`after` onto the continuable kind) and gRPC stay as documented recipes through the `Paging` factories, because each needs its own framework dependency and package.

**Rules out.** Controllers-only first release. A `Zooper.Ant.GraphQL` package now.

**Affects the brief.** "Package Split" row for AspNetCore gains the Minimal API binding; "Milestones" item 6 loses it.

## Decision 15: Forward-only continuation in the first release

**Settled** by the developer. The continuable page carries `nextPageToken` only. The token payload reserves room for a direction flag (payload version field) so backwards paging can be added later without invalidating issued tokens. The three named uses (infinite scroll, load-more, full traversal) never go backwards; Google AIP-158 is forward-only.

**Rules out.** `previousPageToken`, the reversed-chain read path and the second token per response in the first release.

## Consolidated: what changes in `paging-package-brief.md`

The brief stays the input document. These sections are superseded by this record and must be rewritten before an OpenSpec change is drafted from them:

| Brief section | Change |
| --- | --- |
| Core Abstractions | `IPageSource<T>` becomes the floor per decision 6; add the two builders (decisions 1, 5, 7); `PageReader` static class becomes injected `Pager` (decision 3) |
| The Ordering Chain And The Position | Replace `OrderKey<T>`/`OrderChain<T>` with the two chain flavours (decision 2); keep one `PagePosition` |
| Token Protection | `IPageTokenProtector` takes `PageScope` (decision 4); `q` is the package-computed hash; payload gains a version field (decision 15) |
| `PageToken` | Plain struct settled (decision 9) |
| Package Split | Six packages settled, protector stays in core, no test package (decision 11); AspNetCore row gains Minimal API binding (decision 14) |
| Extensibility Contract: Adding A Store | Step 3 becomes "pick a builder, or implement the floor"; add the Dapr floor example (decisions 1, 13) |
| Tests | No shipped suite (decision 11); in-memory is the oracle through the shared builder (decision 5); add `Zooper.Ant.Example` |
| Milestones | EF Core joins the first release (decision 13); Minimal API leaves item 6 (decision 14); backwards paging stays deferred (decision 15) |
| Decisions, item 6 | All four deferred questions are now settled: packages (8), `PageToken` (9), `ProblemDetails` type (10), dialect seam (7) |
