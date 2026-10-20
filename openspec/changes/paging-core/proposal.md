# Proposal

## Why

Every Zooper .NET service that exposes a list endpoint re-implements paging, and the one working implementation (Feng's) is tied to Feng's constants, exceptions and three store-specific copies of the same kind switch. `Zooper.Ant` turns it into a store-neutral core so that a service gets caller-selected paging, keyset continuation behind protected tokens and a documented way to plug any store, without copying code. This change is the first slice: the core package and its in-memory source, publishable on its own.

## What Changes

- New package `Zooper.Ant` (core): the `Paging` instruction with its three kinds (continuable, counted, capped) bounded by configurable `PagingLimits`; the `Page<T>` results with `Map`; `PageToken` as a plain struct; `PageScope` that binds a token to a read name and its result-defining arguments; `IPageTokenProtector` with the Data Protection implementation; `PagingException` and `InvalidPageTokenException`.
- The store seam: `IPageSource<T>` as the floor any store implements, `PagePosition` as the one position model, and `Pager` as the single injected service that runs the kind switch, issues tokens and refuses foreign ones, so a store implements the floor once and gets every kind for free.
- The LINQ expression builder: an order chain declared as typed lambdas (`Order<T>.By(...).ThenBy(...)`) turned into ORDER BY and the expanded seek predicate as one expression tree, behind a two-method `IQueryExecutor` seam. The in-memory source is this builder over `AsQueryable()` with a synchronous executor.
- The traversal helper `Pager.TraverseAsync` that follows tokens to the end as an `IAsyncEnumerable<T>`.
- `services.AddAnt(...)` registration with validated limits and a configurable Data Protection purpose root.
- The in-memory source is the oracle: the package's own tests run every ordering and token case against it. No test package is shipped; adapter authors test their own adapters.
- `Zooper.Ant.Example`: a runnable minimal API that shows every feature over in-memory data, plus a cursor-based store.
- Not in this change: the SQL-text builder and dialect, the EF Core, Marten and Npgsql adapters, the AspNetCore binding, OpenAPI and the `ProblemDetails` handler. Each is a later change on top of these capabilities.

No breaking changes: nothing exists yet.

## Capabilities

### New Capabilities

- `paged-reads`: the three page kinds a caller can ask for, the bounds the service imposes (default size, maximum size, maximum depth), kind-bound arguments, and the total order every paged read must impose. Host- and store-neutral: a refusal is a `PagingException`; the HTTP mapping belongs to the AspNetCore change.
- `continuation-tokens`: a continuable read is navigated by an opaque token that is a bookmark in the ordering over live data, protected against reading and tampering, bound to the issuing read and its result-defining arguments including the ordering, validated structurally, and never expiring by age.
- `keyset-ordering`: how an order chain is declared and what seeking over it guarantees: the last key is the unique tie-breaker, mixed directions, nulls last with a null bookmark, computed keys, the timestamp microsecond-truncation rule, and ORDER BY and the seek predicate generated from the same declaration.
- `page-sources`: the contract between the core and a store: what a source must implement, what it gets for free (every kind, token issuance, token refusal), how a native-cursor store that cannot count or offset behaves, and the in-memory source as the reference the package's own tests verify.
- `paged-traversal`: consuming every item of a paged read through one helper: incremental delivery, live data without a snapshot, a failed page ends the traversal with its own error, no automatic retry, and what a changed ordering value may do to the sequence.

### Modified Capabilities

None. `openspec/specs` is empty.

## Impact

- New projects: `Zooper.Ant` (the only package), `Zooper.Ant.Tests` (unit), `Zooper.Ant.Example` (not packed), per the repository layout in `paging-package-brief.md` "Decisions" (flat layout, `net10.0`, xUnit, Shouldly, NSubstitute).
- Dependencies: core references `Microsoft.AspNetCore.DataProtection.Abstractions` and `Microsoft.Extensions.Options` only. No Vogen, no Marten, no Npgsql, no MVC.
- Design source of truth: `docs/briefs/extensibility-design.md` (decisions 1 to 15) overrides `paging-package-brief.md` where they disagree; the behaviour specs are ported from the brief's Appendix C with Feng's entity names made generic and every HTTP-edge statement moved out to the AspNetCore change.
- Downstream: Feng replaces its `Paging.cs`, `Page*.cs`, `PageToken.cs`, `IPageTokenProtector`, `DataProtectionPageTokenProtector` and `InMemoryPagingExtensions` with this package once the adapter changes land; nothing in Feng changes within this change.
