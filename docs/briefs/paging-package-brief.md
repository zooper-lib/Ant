# Paging Package Brief

Status: draft handed to the agent that bootstraps the package repository. Written 2026-10-19 from the paging implementation in the Feng backend (branch `feature/basic-tournament`, uncommitted at the time of writing).

## Purpose

This document tells an agent working in a **new, empty repository** what to build: a family of NuGet packages that give any .NET service caller-selected paging with three page kinds, keyset continuation behind protected opaque tokens, and store adapters that a user can extend to any database or query technology.

It contains:

1. the goal and the positioning against existing packages,
2. the target architecture and the extensibility contract,
3. what must change relative to the code that exists today,
4. milestones, tests, repository layout and open decisions,
5. appendices with the existing source, tests, behaviour specifications and call-site examples, verbatim.

The code in the appendices **works and is tested** inside the Feng backend. It is the starting point, not the final shape. Section 5 lists exactly what is tailored to Feng and must be generalised.

## How To Read This Document

- `{Name}` is a placeholder for the package family name. The developer chooses it before any file is created. Every package is `Zooper.{Name}` or `Zooper.{Name}.<Adapter>`.
- Type names proposed in section 4 (`IPageSource`, `PageReader`, `OrderKey`, `PagingLimits`, `PagingException`, …) are **proposals**. Confirm them with the developer before they appear in code. Type names in the appendices are the names the existing code uses.
- "Existing code" means Appendix A. "Existing behaviour" means Appendix C. When this document and the appendices disagree, this document wins: the appendices are the input, this document is the target.
- The developer never wants a deferral. Build the package as its own product with its own tests from the first commit.

## Goal

A service exposes list endpoints. The **caller** decides, per request, how it wants to walk the list, and the service answers the same read in one of three page shapes:

| Kind | Caller sends | Service answers | Use |
| --- | --- | --- | --- |
| Continuable (default) | `pageToken?`, `size?` | items, `size`, `nextPageToken?` | infinite scroll, full traversal, machine clients |
| Counted | `page?`, `size?` | items, `page`, `size`, `totalCount` | classic numbered pager |
| Capped | `cap?` | items, `cap`, `truncated` | "top N, tell me if there is more" |

Rules that hold for every kind, and which the package owns so that no consumer re-implements them:

- One factory family bounds every request: default size, maximum size, maximum offset depth for counted paging. The bounds are **configuration**, not constants.
- One read, one query, one handler, one port method answers all three kinds. The store adapter is the only code that names a kind.
- A continuation token is **opaque, protected and bound** to the read that issued it and to the arguments that defined its result. A token presented to a different read, or with different arguments, is refused.
- Continuation is **keyset** (the "seek method"), never offset: the token carries the position of the last row served, in ordering-chain order, ending with a unique tie-breaker. Seeking works for mixed directions, `NULLS LAST` columns and per-request computed criteria such as relevance or distance.
- A use case maps the items of a page without knowing its kind (`page.Map(item => view)`); a controller returns the page as is.
- A whole-set consumer traverses with one helper that follows tokens until the end.

The behaviour is specified in Appendix C. Read `paged-reads`, `continuation-tokens` and `paged-traversal` before writing any code; they are the acceptance criteria, with Feng's entity names replaced by the package's generic ones.

## Positioning Against Existing Packages

Checked on 2026-10-19. None covers the combination; each covers a slice.

| Package | What it does | Gap |
| --- | --- | --- |
| Marten `ToPagedListAsync` | offset paging with total count over Marten LINQ | one kind only, no keyset, no tokens |
| [SeekKit](https://www.nuget.org/packages/SeekKit.Core/) (`.EntityFramework`, `.MongoDB`) | keyset paging with opaque, optionally HMAC-signed, bidirectional tokens | EF Core and MongoDB only; no caller-selected kinds; no Marten, no raw SQL |
| MR.EntityFrameworkCore.KeysetPagination (+ MR.AspNetCore.Pagination) | keyset paging over EF Core, well known | EF Core only; tokens not bound to the query arguments |
| [OrionPage](https://www.nuget.org/packages/OrionPage) | keyset over EF Core with a compact binary cursor | EF Core only, very young |
| [EricksonLopez.Pagination](https://www.nuget.org/packages/EricksonLopez.Pagination) (`.Dapper`, `.Relay`) | keyset with HMAC cursors, Dapper and Relay flavours | young; no kinds; no Marten |
| Google AIP-158 | the wire convention for `page_token` / `next_page_token` / `page_size` | a convention, not code |

What this package family adds and nobody ships: the three caller-selected kinds behind one read, tokens bound to read name and argument fingerprint through ASP.NET Core Data Protection, and a store-neutral core with adapters for Marten, raw Npgsql, in-memory and (new) EF Core, plus a documented way to add the next store.

## Named Patterns The Design Follows

State the source when you document a decision; the developer rejects unsourced mechanisms.

- **Keyset pagination / the seek method** (Markus Winand, *SQL Performance Explained*, use-the-index-luke.com "Paging through data"): continue after a position, never skip an offset. The expanded predicate `(c1 ▷ v1) OR (c1 = v1 AND c2 ▷ v2) OR …` is the form that supports mixed directions and null handling where row-value comparison does not.
- **Opaque page tokens** (Google AIP-158; Relay Cursor Connections spec): the client treats the token as a black box; the server may change its contents at any time.
- **Token integrity through platform cryptography** (ASP.NET Core Data Protection, purpose strings): one purpose per read keeps a token from one read unreadable by another, and the key ring is the platform's documented multi-instance concern.
- **Ports and adapters** (Cockburn): the core defines the page algorithm against a small source abstraction; each store is an adapter. Adding a database never touches the core.
- **Options pattern** (`Microsoft.Extensions.Options`): bounds are `PagingLimits` options with validated defaults, not constants.

## Target Architecture

### Package Split

Each consumer pulls only the store it uses. Proposed split (confirm with the developer; a five-way split with the protector in its own package is the alternative):

| Package | Depends on | Contains |
| --- | --- | --- |
| `Zooper.{Name}` (core) | `Microsoft.AspNetCore.DataProtection.Abstractions` only | `Paging` and its three kinds, `PagingLimits`, `Page<T>` and its three kinds, `PageToken`, `IPageTokenProtector`, `DataProtectionPageTokenProtector`, the position model, `IPageSource<T>`, `PageReader`, the in-memory source, `PagingException` and `InvalidPageTokenException`, the `IAsyncEnumerable` traversal helper |
| `Zooper.{Name}.AspNetCore` | core, `Microsoft.AspNetCore.App` framework reference | `PagingRequest`, `PagingKind`, `ToPaging` binding with foreign-argument refusal, OpenAPI `oneOf` transformer, `IOptions<PagingLimits>` wiring, an `IExceptionHandler` that maps the package exceptions to a 400 `ProblemDetails` (opt-in) |
| `Zooper.{Name}.EntityFrameworkCore` | core, `Microsoft.EntityFrameworkCore` | `IQueryable<T>` source: LINQ expression builder for the seek predicate from typed key selectors. Provider independent. |
| `Zooper.{Name}.Marten` | core, `Marten` | Marten source. First try the EF Core LINQ builder against Marten's LINQ provider; fall back to `MatchesSql` only where a predicate does not translate (see Appendix A and the Marten risks below). |
| `Zooper.{Name}.Npgsql` | core, `Npgsql` | raw-statement source: `ORDER BY` fragment and expanded seek predicate generated from the same chain, PostgreSQL dialect |

Do not fold everything into one package. Do not make core depend on Vogen, Marten, Npgsql or ASP.NET Core MVC.

### Core Abstractions

The existing code implements the three-kind switch **three times** (Marten, Npgsql, in-memory; Appendix A). The package implements it **once** in core and lets each store supply the primitives. This is the extensibility contract: a new store implements one interface and gets all three kinds, token issuance and token refusal for free.

```csharp
// Proposed. Names to be confirmed by the developer.

/// One store's view of one ordered result set. Implemented per store.
public interface IPageSource<TItem>
{
    /// Total rows of the result set. Only the counted kind calls it.
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// Up to `limit` rows from the start, in the source's order.
    Task<IReadOnlyList<TItem>> TakeAsync(int limit, CancellationToken cancellationToken);

    /// Up to `limit` rows strictly after `position`, in the source's order.
    Task<IReadOnlyList<TItem>> TakeAfterAsync(PagePosition position, int limit, CancellationToken cancellationToken);

    /// The position of one served row: the ordering values in chain order, ending with the tie-breaker.
    PagePosition PositionOf(TItem item);

    /// Reads a position back from the token payload with the chain's types.
    PagePosition ReadPosition(IReadOnlyList<JsonElement> payload);
}

/// The single algorithm. One per page read.
public static class PageReader
{
    public static Task<Page<TItem>> ReadAsync<TItem>(
        IPageSource<TItem> source,
        Paging paging,
        PageTokenScope scope,          // read name + argument fingerprint
        IPageTokenProtector protector,
        CancellationToken cancellationToken);
}
```

`PageReader.ReadAsync` is the existing `switch` from `MartenPagingExtensions.ToPageAsync` with the store calls replaced by the interface: counted = `CountAsync` + `TakeAsync` after an offset (offset is a source concern; add `offset` to `TakeAsync` or a `SkipTakeAsync`), capped = `TakeAsync(cap + 1)`, continuable = `TakeAfterAsync(position, size + 1)` or `TakeAsync(size + 1)`, then `PositionOf(last)` into `Protect`.

The in-memory source lives in core and doubles as the reference implementation for the contract tests.

### The Ordering Chain And The Position

One position model for every store, replacing the three shapes in the existing code (instant-plus-identifier for Marten, free chain for Npgsql, single string key for in-memory):

```csharp
// Proposed.
public sealed record OrderKey<TItem>(
    string Name,                         // column or expression name the store understands
    Func<TItem, object?> Value,          // the value of the served row
    Type ValueType,                      // for reading the token payload back
    bool Ascending,
    bool NullsLast = true);

public sealed record OrderChain<TItem>(IReadOnlyList<OrderKey<TItem>> Keys, OrderKey<TItem> TieBreaker, bool Reversed = false);

public sealed record PagePosition(IReadOnlyList<object?> Values);
```

For LINQ-based stores (EF Core, Marten) the key is an `Expression<Func<TItem, TKey>>`, the value type comes from `TKey`, and the seek predicate is built as an expression tree. For raw SQL the key is a column expression string and the seek predicate is text. Both come from the same chain, so `ORDER BY` and the seek cannot drift. Keep the `NULLS LAST` rule from Appendix C `continuation-tokens`: a null bookmark value under `NULLS LAST` contributes no "greater" arm and matches `IS NULL` on the equality arm.

### Token Protection

Keep the existing design (Appendix A, `DataProtectionPageTokenProtector`): payload `{ "q": fingerprint, "p": [values] }`, protected with `IDataProtectionProvider.CreateProtector(purpose, readName)`, base64url. Changes:

- the purpose root string is configurable with a package default; it is not `Feng.Backend.PageToken`,
- `IPageTokenProtector` stays the seam so a user can plug an HMAC protector (SeekKit does this) for hosts without Data Protection,
- refusals throw `InvalidPageTokenException` (derives from `PagingException`), never Vogen's exception.

### Exceptions

```csharp
public class PagingException : Exception { }                   // an argument the factories refuse
public sealed class InvalidPageTokenException : PagingException { } // tampered, foreign, stale shape, wrong arguments
```

The core throws; the host decides the status. The AspNetCore package offers an opt-in handler that answers 400 with a `ProblemDetails` whose `type` is stable and documented. A consumer with its own middleware (Feng has one) maps the exception itself.

### Limits

```csharp
public sealed record PagingLimits(int DefaultSize = 20, int MaximumSize = 50, int MaximumDepth = 1000)
{
    public static PagingLimits Default { get; } = new();
}
```

`Paging.Continuable(token, size, limits)`, `Paging.Counted(page, size, limits)`, `Paging.Capped(cap, limits)`; overloads without `limits` use `PagingLimits.Default`. The AspNetCore binding reads `IOptions<PagingLimits>`. Validate at startup: `1 <= DefaultSize <= MaximumSize`, `MaximumDepth >= MaximumSize`.

### `PageToken`

The existing type is a Vogen value object. Recommendation: replace it with a plain `readonly record struct PageToken` with the same validation (non-blank, base64url alphabet), a `System.Text.Json` converter, a `TypeConverter` for query-string binding, and `TryParse` for Minimal API binding. Reason: a consumer should not inherit Vogen's runtime and exception type from a paging package. If the developer prefers to keep Vogen, the package must still throw its own exceptions.

### Traversal

Port `QueryAllAsync` (Feng's `QueryBusExtensions`, Appendix D) into core as a bus-agnostic helper:

```csharp
public static IAsyncEnumerable<TItem> TraverseAsync<TItem>(
    Func<PageToken?, CancellationToken, Task<Page<TItem>>> readPage,
    CancellationToken cancellationToken);
```

Feng wraps it with its `Either` result type; the package does not know `Either`. No buffering, no prefetch, no retry; a page that is not continuable is an `InvalidOperationException`.

## What Must Change Relative To The Existing Code

Everything in Appendix A compiles and passes tests inside Feng. The following is Feng-specific and is **not** copied as is:

1. **Constants become `PagingLimits`.** `Paging.DefaultSize`, `MaximumSize`, `MaximumDepth` are Feng policy.
2. **`Vogen.ValueObjectValidationException` is replaced** by `PagingException` / `InvalidPageTokenException` everywhere (factories, request binding, protector, every source).
3. **Three seek models become one** (`OrderChain`). The Marten extension's `(instant?, identifier)` shape, the Npgsql extension's `(expression, ascending)[]` plus `reversed` plus `tieBreakerColumn`, and the in-memory extension's single string key all map onto it.
4. **The three-kind switch is written once** in `PageReader`; stores implement `IPageSource<T>`.
5. **Feng attributes go:** `[SensitiveValueOptOut]` (a Feng logging attribute), `[UsedImplicitly]` (JetBrains annotations; keep only if the package takes that dependency on purpose).
6. **Purpose string** `Feng.Backend.PageToken` becomes a configurable value with a package default.
7. **Npgsql fixed names** `page_row` alias and `@page_limit` / `@page_offset` parameters must not collide with the caller's statement: generate unguessable names or let the caller rename.
8. **Marten `MatchesSql` with raw column strings** (`d.data ->> …`, `d.id`) leaks Marten's storage layout to the caller. First attempt: build the seek predicate as a LINQ expression and let Marten translate it. Only where Marten cannot translate (Feng hit this with Vogen strong types; plain primitives may translate fine) keep a documented `MatchesSql` escape hatch. Note the Postgres timestamptz microsecond truncation in `MartenPagingExtensions`: keep it, document it, and test it.
9. **`[FromQuery]` names** (`paging`, `page`, `size`, `cap`, `pageToken`) stay the defaults but become configurable on the AspNetCore package.
10. **Feng's `Either`** does not enter the package. Sources return plain `Task<Page<T>>` and throw on failure; the consumer wraps.
11. **The in-memory source orders by one string key only.** Generalise to the chain.

## Extensibility Contract

Document these three recipes in the README, each with a complete example and a test:

### Adding A Store

1. Reference `Zooper.{Name}`.
2. Implement `IPageSource<TItem>` over your query object (an `IQueryable`, a command, a collection).
3. Build `TakeAfterAsync` from the `OrderChain` with your technology's predicate form.
4. Run the shared **contract test suite** (`Zooper.{Name}.Testing`, see Tests) against your source: it drives every kind, every chain shape, null bookmarks, reversal, token refusal, and the microsecond-truncation case, with an in-memory fixture of your rows.

### Adding A Token Protector

Implement `IPageTokenProtector` (`Protect(scope, position) → PageToken`, `Unprotect(scope, token) → payload`). The contract test suite has a protector section: round trip, foreign read name refused, different fingerprint refused, tampered byte refused.

### Adding A Host Binding

The AspNetCore package is one binding. A gRPC, GraphQL (Relay `first`/`after` maps directly onto continuable) or CLI binding builds a `Paging` through the same factories and reads the same `Page<T>`.

## Tests

- **Unit tests** for `Paging` factories and limits, `Page<T>` kinds and `Map`, `PageToken` validation, `DataProtectionPageTokenProtector`, the chain-to-predicate builders (text and expression), `PageReader` against the in-memory source, the AspNetCore binding and refusals, the OpenAPI transformer. Port Appendix B as the starting set.
- **Contract test suite** published as `Zooper.{Name}.Testing` so third-party stores can verify themselves. The in-memory source must pass it; it is the oracle.
- **Integration tests** for Npgsql, Marten and EF Core against PostgreSQL via Testcontainers; EF Core additionally against SQLite to prove provider independence. Cover: every kind; a chain with mixed directions; a `NULLS LAST` column with a null bookmark; a computed criterion (a rounded score); reversal; a token reused with different arguments; a token from another read; `page × size` over `MaximumDepth`; a traversal that sees every row exactly once on a static set.
- **Performance check** (not a gate): `EXPLAIN` for the expanded seek predicate on a four-key chain with a matching compound index, recorded in the docs.

Test conventions: xUnit, Shouldly, NSubstitute; `_sut`; Arrange/Act/Assert (decided, see Decisions).

## Repository Layout And CI

Use `Projects/Zooper/Bee` as the template: `Directory.Build.props` with shared package metadata, `Zooper.{Name}.sln`, one project folder per package, `Zooper.{Name}.Tests`, `Zooper.{Name}.IntegrationTests` and `Zooper.{Name}.Testing`, `README.md`, `CHANGELOG.md`, `LICENSE`, `icon.png`, `.github/workflows/pr.yml` (build and test) and `nuget.publish.yml` (publish on tag). Target `net10.0` only. See Decisions for the details.

An `Zooper.{Name}.Example` web project shows one list endpoint answering all three kinds over the in-memory source, and the same endpoint over EF Core with SQLite, so a reader can run it without infrastructure.

## Milestones

1. **Core and in-memory.** `Paging`, `PagingLimits`, `Page<T>`, `PageToken`, protector, `OrderChain`, `PagePosition`, `IPageSource`, `PageReader`, in-memory source, traversal helper, exceptions. Unit tests and the contract suite. Publishable alone.
2. **AspNetCore.** `PagingRequest`, `ToPaging`, options, OpenAPI transformer, opt-in exception handler. Example project over in-memory.
3. **Npgsql and Marten**, ported from Appendix A onto the core abstractions, integration-tested with Testcontainers. Feng switches to the packages at this point and deletes its copies.
4. **EntityFrameworkCore.** Expression-tree seek builder; SQLite and PostgreSQL tests; example endpoint.
5. **Backwards paging.** A `previousPageToken` on the continuable page (SeekKit and Relay offer it). Needs the reversed chain and a direction flag in the payload.
6. **Further protectors and bindings.** HMAC protector for hosts without Data Protection; Minimal API `TryParse`/`BindAsync`; Relay connection mapping for GraphQL.
7. **Further stores**, each by the contract: Dapper (shares the Npgsql text builder behind a dialect), SQL Server dialect for the text builder, MongoDB (filter builders), Elasticsearch `search_after`.
8. **Hardening.** Native AOT friendliness (source-generated JSON context for the payload), a Roslyn analyzer that flags a `Page<T>` endpoint without the OpenAPI transformer, benchmarks.

Milestones 1 to 3 are the first release. Nothing in 4 onwards blocks it.

## Decisions

Decided by the developer on 2026-10-20, after analysing the existing Zooper C# repositories (Bee, Gorilla, tools-marten-apply-enforcer). Where this section and the rest of the document disagree, this section wins.

1. **Family name.** `{Name}` is `Ant`. The repository is `Ant` (`zooper-lib/Ant`); the packages are `Zooper.Ant`, `Zooper.Ant.AspNetCore`, `Zooper.Ant.Npgsql`, `Zooper.Ant.Marten`, `Zooper.Ant.EntityFrameworkCore`, `Zooper.Ant.Testing`.
2. **Repository layout follows the Zooper repositories, not one test project per package.** Flat layout with project folders at the repository root (no `src/`), `Zooper.Ant.sln` at the root. Tests live in two projects: `Zooper.Ant.Tests` (unit tests for every package) and `Zooper.Ant.IntegrationTests` (Testcontainers, needs Docker). `Zooper.Ant.Testing` is the shipped contract suite, not a test project. Example project: `Zooper.Ant.Example`.
3. **Target framework.** `net10.0` only, for every project. No `net8.0`, no `netstandard2.0`. CI installs the .NET SDK `10.x`.
4. **Test libraries.** xUnit, Shouldly, NSubstitute. Not FluentAssertions (v8+ is not free for commercial use) and not Moq.
5. **Versioning.** One version for all packages, set as `<Version>` in `Directory.Build.props` (as Bee and Gorilla do; no MinVer). Release through git-flow: `feature/*` → `develop`, `release/vX.Y.Z` with a version bump, tag `v*` triggers `nuget.publish.yml`, which publishes every packable project as a matrix.
6. **The remaining design questions are deferred**: four or five packages, `PageToken` as a plain struct or Vogen, the `ProblemDetails` `type` URI, and an Npgsql dialect seam. They are decided when the milestone that needs them starts, not before scaffolding.

Carried over from the Zooper repositories without discussion: `Directory.Build.props` with the shared metadata (Daniel Martin, Zooper, MIT, `icon.png` and `README.md` packed, snupkg and source link, `LangVersion latest`, `Nullable enable`, `ImplicitUsings disable`); `InternalsVisibleTo` in `Properties/AssemblyInfo.cs`; README with icon and NuGet/License badges; CHANGELOG in Keep a Changelog format; briefs under `docs/briefs/`; OpenSpec (`openspec/specs`, `openspec/changes`) with the `opsx` commands and skills in `.claude/` and `.github/`.

## Risks Carried Over From Feng

- Marten LINQ may not translate the compound seek predicate; Feng fell back to `MatchesSql` and hit three bugs doing so: a `string[]` passed to a `params object[]` spreads into one parameter per element (wrap as `new object[] { array }`); a raw-SQL `DateTime` parameter binds as `timestamp without time zone`, which Npgsql refuses for a UTC value (bind `DateTimeOffset`); and a duplicated-column identifier must be bound as a primitive.
- Expanded seek predicates on long chains can defeat the planner; measure with `EXPLAIN`.
- An exact `count(*)` is a full evaluation per request; that is the counted kind's documented cost.
- Multi-instance hosts need a shared Data Protection key ring (Redis, database, blob); the package documents it and does not provision it.

## Appendix A: Existing Source

Verbatim from the Feng backend. Namespaces, Vogen, `SensitiveValueOptOut`, `UsedImplicitly` and the constants are Feng-specific; see section 5.

### `src/core/Feng.Backend.Core.Application/Models/Paging.cs`

```csharp
using Vogen;

namespace Feng.Backend.Core.Application.Models;

/// <summary>
/// The instruction a caller gives a paged read: which kind of page it wants and how far into the result it is. The
/// factories are the one place that bounds paging, so every caller — a controller or a module dispatching on the bus —
/// is bounded alike before the store is asked.
/// </summary>
/// <remarks>
/// <para>A missing or non-positive size or cap becomes <see cref="DefaultSize"/>; a larger one is clamped to
/// <see cref="MaximumSize"/> and served, because a smaller page is still a correct answer. A counted page below one, or
/// one whose <c>page × size</c> exceeds <see cref="MaximumDepth"/>, is refused rather than normalized: serving another
/// page under the requested number would be a wrong answer presented as a right one.</para>
/// <para>Refusals throw <see cref="ValueObjectValidationException"/>, which the edge answers as the shared invalid-value
/// error. Only the adapter names the concrete kind; a use case passes the instruction through.</para>
/// </remarks>
public abstract record Paging
{
	/// <summary>The size or cap applied when the caller supplies none.</summary>
	public const int DefaultSize = 20;

	/// <summary>The largest size or cap served; a larger request is clamped to it.</summary>
	public const int MaximumSize = 50;

	/// <summary>The deepest row a counted page may reach, as <c>page × size</c>.</summary>
	public const int MaximumDepth = 1000;

	private protected Paging() { }

	/// <summary>Requests a continuable page, continuing after <paramref name="pageToken"/> when one is given.</summary>
	/// <param name="pageToken">The token of the previous page, or <see langword="null"/> for the first page.</param>
	/// <param name="size">The requested page size; defaulted and clamped.</param>
	/// <returns>The bounded continuable paging instruction.</returns>
	public static ContinuablePaging Continuable(PageToken? pageToken, int? size) =>
		new(pageToken, Bound(size));

	/// <summary>Requests a counted page by number.</summary>
	/// <param name="page">The one-based page number; <see langword="null"/> means the first page.</param>
	/// <param name="size">The requested page size; defaulted and clamped.</param>
	/// <returns>The bounded counted paging instruction.</returns>
	/// <exception cref="ValueObjectValidationException">The page is below one or reaches beyond the depth bound.</exception>
	public static CountedPaging Counted(int? page, int? size)
	{
		var resolvedPage = page ?? 1;
		var resolvedSize = Bound(size);

		if (resolvedPage < 1)
			throw new ValueObjectValidationException("Paging page must be at least 1.");

		if ((long)resolvedPage * resolvedSize > MaximumDepth)
			throw new ValueObjectValidationException(
				$"Paging cannot reach beyond {MaximumDepth} rows; request continuable paging to go further.");

		return new CountedPaging(resolvedPage, resolvedSize);
	}

	/// <summary>Requests one capped screen of best results.</summary>
	/// <param name="cap">The requested cap; defaulted and clamped like a size.</param>
	/// <returns>The bounded capped paging instruction.</returns>
	public static CappedPaging Capped(int? cap) =>
		new(Bound(cap));

	/// <summary>
	/// Builds an empty page of the kind this instruction asks for, for a read whose predicate is known to match nothing
	/// before any store is asked.
	/// </summary>
	/// <typeparam name="TItem">The page's item type.</typeparam>
	/// <returns>The empty page.</returns>
	public Page<TItem> ToEmptyPage<TItem>() =>
		this switch
		{
			CountedPaging counted => new CountedPage<TItem>(counted.Page, counted.Size, 0, []),
			CappedPaging capped => new CappedPage<TItem>(capped.Cap, false, []),
			ContinuablePaging continuable => new ContinuablePage<TItem>(continuable.Size, null, []),
			_ => throw new InvalidOperationException("Paging kind is not known.")
		};

	private static int Bound(int? requested) =>
		requested switch
		{
			null or < 1 => DefaultSize,
			> MaximumSize => MaximumSize,
			_ => requested.Value
		};
}

/// <summary>Continuable paging: a size and the token of the previous page, if any.</summary>
public sealed record ContinuablePaging : Paging
{
	internal ContinuablePaging(PageToken? pageToken, int size) => (PageToken, Size) = (pageToken, size);

	/// <summary>The token to continue after, or <see langword="null"/> for the first page.</summary>
	public PageToken? PageToken { get; }

	/// <summary>The bounded page size.</summary>
	public int Size { get; }
}

/// <summary>Counted paging: a one-based page number and a size, within the depth bound.</summary>
public sealed record CountedPaging : Paging
{
	internal CountedPaging(int page, int size) => (Page, Size) = (page, size);

	/// <summary>The one-based page number.</summary>
	public int Page { get; }

	/// <summary>The bounded page size.</summary>
	public int Size { get; }
}

/// <summary>Capped paging: one screen of at most <see cref="Cap"/> items, with no continuation.</summary>
public sealed record CappedPaging : Paging
{
	internal CappedPaging(int cap) => Cap = cap;

	/// <summary>The bounded cap.</summary>
	public int Cap { get; }
}
```

### `src/core/Feng.Backend.Core.Application/Models/Page.cs`

```csharp
namespace Feng.Backend.Core.Application.Models;

/// <summary>
/// One page of a paged read, running unchanged from the port to the edge. The port returns it over the read model, the
/// use case maps its items with <see cref="Map{TOut}"/> without naming the kind, and the controller returns it as is.
/// </summary>
/// <remarks>
/// A page is not a View: it is the container the read's View items travel in. The concrete kind —
/// <see cref="ContinuablePage{TItem}"/>, <see cref="CountedPage{TItem}"/> or <see cref="CappedPage{TItem}"/> — is
/// chosen by the caller's <see cref="Paging"/> and constructed only by the adapter.
/// </remarks>
/// <typeparam name="TItem">The item type carried on the page.</typeparam>
public abstract class Page<TItem>
{
	private protected Page(IReadOnlyList<TItem> itemList) => ItemList = itemList;

	/// <summary>The items on this page, in the read's order.</summary>
	public IReadOnlyList<TItem> ItemList { get; }

	/// <summary>Maps every item, keeping the page's kind and result-level context.</summary>
	/// <typeparam name="TOut">The mapped item type.</typeparam>
	/// <param name="map">The mapping applied to each item, in order.</param>
	/// <returns>A page of the same kind over the mapped items.</returns>
	public abstract Page<TOut> Map<TOut>(Func<TItem, TOut> map);

	private protected IReadOnlyList<TOut> MapItemList<TOut>(Func<TItem, TOut> map) =>
		ItemList.Select(map).ToList();
}
```

### `src/core/Feng.Backend.Core.Application/Models/ContinuablePage.cs`

```csharp
namespace Feng.Backend.Core.Application.Models;

/// <summary>
/// One page of a read, plus the token of the next page when one exists. Lets a client render "load more" and step
/// forward; it cannot render a total or jump to an arbitrary page, because no total is reported.
/// </summary>
/// <remarks>
/// The next token is answered by fetching one item beyond the page, so the read costs the page plus one item rather
/// than an evaluation of every match. The absence of <see cref="NextPageToken"/> is the only end signal.
/// </remarks>
/// <typeparam name="TItem">The item type carried on the page.</typeparam>
public sealed class ContinuablePage<TItem> : Page<TItem>
{
	/// <summary>Creates a continuable page.</summary>
	/// <param name="size">The page size actually used.</param>
	/// <param name="nextPageToken">The token of the next page, or <see langword="null"/> on the last page.</param>
	/// <param name="itemList">The items on this page, in the read's order.</param>
	public ContinuablePage(int size, PageToken? nextPageToken, IReadOnlyList<TItem> itemList)
		: base(itemList) => (Size, NextPageToken) = (size, nextPageToken);

	/// <summary>The page size actually used, which may be smaller than the size requested.</summary>
	public int Size { get; }

	/// <summary>The token of the next page, or <see langword="null"/> when this is the last page.</summary>
	public PageToken? NextPageToken { get; }

	/// <inheritdoc />
	public override Page<TOut> Map<TOut>(Func<TItem, TOut> map) => new ContinuablePage<TOut>(Size, NextPageToken, MapItemList(map));
}
```

### `src/core/Feng.Backend.Core.Application/Models/CountedPage.cs`

```csharp
namespace Feng.Backend.Core.Application.Models;

/// <summary>
/// One page of a read, carrying the exact number of items that matched. The costliest of the three kinds, and the only
/// one that lets a client render "page 3 of 62" and jump to an arbitrary page.
/// </summary>
/// <remarks>
/// The total cannot be produced without evaluating every matching item on every request; the caller chooses this kind
/// with that cost known.
/// </remarks>
/// <typeparam name="TItem">The item type carried on the page.</typeparam>
public sealed class CountedPage<TItem> : Page<TItem>
{
	/// <summary>Creates a counted page.</summary>
	/// <param name="page">The one-based page number this page answers.</param>
	/// <param name="size">The page size actually used.</param>
	/// <param name="totalCount">The number of items matching the read, across every page.</param>
	/// <param name="itemList">The items on this page, in the read's order; empty past the last page.</param>
	public CountedPage(int page, int size, int totalCount, IReadOnlyList<TItem> itemList)
		: base(itemList) => (Page, Size, TotalCount) = (page, size, totalCount);

	/// <summary>The one-based page number this page answers.</summary>
	public int Page { get; }

	/// <summary>The page size actually used, which may be smaller than the size requested.</summary>
	public int Size { get; }

	/// <summary>The number of items matching the read, across every page.</summary>
	public int TotalCount { get; }

	/// <inheritdoc />
	public override Page<TOut> Map<TOut>(Func<TItem, TOut> map) => new CountedPage<TOut>(Page, Size, TotalCount, MapItemList(map));
}
```

### `src/core/Feng.Backend.Core.Application/Models/CappedPage.cs`

```csharp
namespace Feng.Backend.Core.Application.Models;

/// <summary>
/// A single bounded screen of a read's best results, with no continuation. Lets a client render one screen and say
/// whether there was more behind it; it offers no way to reach that more, by design.
/// </summary>
/// <remarks>The read costs the cap plus one item and nothing beyond it.</remarks>
/// <typeparam name="TItem">The item type carried on the screen.</typeparam>
public sealed class CappedPage<TItem> : Page<TItem>
{
	/// <summary>Creates a capped page.</summary>
	/// <param name="cap">The maximum number of items this read returns.</param>
	/// <param name="truncated">Whether further items matched beyond <paramref name="cap"/>.</param>
	/// <param name="itemList">The items, in the read's order, never more than <paramref name="cap"/> of them.</param>
	public CappedPage(int cap, bool truncated, IReadOnlyList<TItem> itemList)
		: base(itemList) => (Cap, Truncated) = (cap, truncated);

	/// <summary>The maximum number of items this read returns.</summary>
	public int Cap { get; }

	/// <summary>Whether the result was cut off at <see cref="Cap"/> with further items matching.</summary>
	public bool Truncated { get; }

	/// <inheritdoc />
	public override Page<TOut> Map<TOut>(Func<TItem, TOut> map) => new CappedPage<TOut>(Cap, Truncated, MapItemList(map));
}
```

### `src/core/Feng.Backend.Core.Application/Models/PageToken.cs`

```csharp
using Vogen;

namespace Feng.Backend.Core.Application.Models;

/// <summary>
/// The opaque continuation token of a continuable page. A client returns it unchanged to receive the next page; it
/// carries a protected bookmark that only the read which issued it can open.
/// </summary>
/// <remarks>
/// Validation is structural only: a token is non-blank base64url. Whether it was issued by this read, for these query
/// arguments, is checked by the adapter when it opens the token.
/// </remarks>
[ValueObject<string>(conversions: Conversions.SystemTextJson | Conversions.NewtonsoftJson | Conversions.TypeConverter)]
public readonly partial struct PageToken
{
	private static Validation Validate(string value) =>
		value switch
		{
			null => Validation.Invalid("PageToken cannot be null."),
			"" => Validation.Invalid("PageToken cannot be empty."),
			_ when string.IsNullOrWhiteSpace(value) => Validation.Invalid("PageToken cannot be whitespace."),
			_ when !value.All(IsBase64UrlCharacter) => Validation.Invalid("PageToken must be base64url."),
			_ => Validation.Ok
		};

	private static bool IsBase64UrlCharacter(char character) =>
		char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
```

### `src/core/Feng.Backend.Core.Application/Models/InMemoryPagingExtensions.cs`

```csharp
using Feng.Backend.Core.Application.Services;
using Vogen;

namespace Feng.Backend.Core.Application.Models;

/// <summary>
/// Serves any kind of <see cref="Page{TItem}"/> from items held in memory — a code-owned catalogue with no store behind
/// it — ordered by a unique string key.
/// </summary>
/// <remarks>
/// The continuable kind continues after the key of the last served item, protected like every other token, so a
/// caller pages an in-memory read exactly as it pages a stored one.
/// </remarks>
public static class InMemoryPagingExtensions
{
	/// <summary>Orders the items by key and serves the page <paramref name="paging"/> asks for.</summary>
	/// <typeparam name="TItem">The item type.</typeparam>
	/// <param name="itemList">Every item matching the read, in any order.</param>
	/// <param name="paging">The caller's paging instruction.</param>
	/// <param name="key">The unique key the items are ordered by, ordinally.</param>
	/// <param name="pageTokenProtector">Protects and opens the continuation token.</param>
	/// <param name="readName">The read issuing the token.</param>
	/// <param name="fingerprint">The canonical result-defining arguments of the read.</param>
	/// <returns>The page of the requested kind.</returns>
	/// <exception cref="ValueObjectValidationException">The token was not issued by this read for these arguments.</exception>
	public static Page<TItem> ToPage<TItem>(
		this IEnumerable<TItem> itemList,
		Paging paging,
		Func<TItem, string> key,
		IPageTokenProtector pageTokenProtector,
		string readName,
		string fingerprint)
	{
		var orderedList = itemList.OrderBy(key, StringComparer.Ordinal).ToList();

		switch (paging)
		{
			case CountedPaging counted:
				return new CountedPage<TItem>(
					counted.Page,
					counted.Size,
					orderedList.Count,
					[.. orderedList.Skip((counted.Page - 1) * counted.Size).Take(counted.Size)]);

			case CappedPaging capped:
				return new CappedPage<TItem>(capped.Cap, orderedList.Count > capped.Cap, [.. orderedList.Take(capped.Cap)]);

			case ContinuablePaging continuable:
			{
				IEnumerable<TItem> remaining = orderedList;

				if (continuable.PageToken is { } pageToken)
				{
					var position = pageTokenProtector.Unprotect(readName, fingerprint, pageToken);
					var after = position.Count == 1 ? position[0].GetString() : null;

					if (after is null)
						throw new ValueObjectValidationException("PageToken was not issued by this read for these query arguments.");

					remaining = orderedList.Where(item => string.CompareOrdinal(key(item), after) > 0);
				}

				var window = remaining.Take(continuable.Size + 1).ToList();

				return window.Count > continuable.Size
					? new ContinuablePage<TItem>(
						continuable.Size,
						pageTokenProtector.Protect(readName, fingerprint, [key(window[continuable.Size - 1])]),
						[.. window.Take(continuable.Size)])
					: new ContinuablePage<TItem>(continuable.Size, null, window);
			}

			default:
				throw new InvalidOperationException("Paging kind is not known.");
		}
	}
}
```

### `src/core/Feng.Backend.Core.Application/Services/IPageTokenProtector.cs`

```csharp
using System.Text.Json;
using Feng.Backend.Core.Application.Models;
using Feng.Backend.Core.Domain.Core;
using Vogen;

namespace Feng.Backend.Core.Application.Services;

/// <summary>
/// Protects the keyset position of a continuable page as an opaque <see cref="PageToken"/>, and opens it again only
/// for the read that issued it and the same result-defining query arguments.
/// </summary>
/// <remarks>
/// The adapter owns both halves of the payload: it composes the fingerprint from the arguments it binds, and it writes
/// and reads the position values of its own ordering chain by index. This service only protects and compares. A token
/// carries no lifetime; it fails only when it was altered, issued by another read or for other arguments, or its key is
/// gone.
/// </remarks>
public interface IPageTokenProtector : IScopedDependency
{
	/// <summary>Protects a position as a token.</summary>
	/// <param name="readName">The issuing read, isolating its tokens from every other read's.</param>
	/// <param name="fingerprint">The canonical result-defining arguments the page was read with; size excluded.</param>
	/// <param name="position">The ordering values of the last served row, in chain order, ending with its identifier.</param>
	/// <returns>The opaque token.</returns>
	PageToken Protect(string readName, string fingerprint, IReadOnlyList<object?> position);

	/// <summary>Opens a token issued by <paramref name="readName"/> for the same <paramref name="fingerprint"/>.</summary>
	/// <param name="readName">The read opening the token.</param>
	/// <param name="fingerprint">The canonical result-defining arguments of the current request.</param>
	/// <param name="pageToken">The token the caller returned.</param>
	/// <returns>The position values, in the order they were protected.</returns>
	/// <exception cref="ValueObjectValidationException">
	/// The token was altered, issued by another read or for other arguments, or cannot be decrypted.
	/// </exception>
	IReadOnlyList<JsonElement> Unprotect(string readName, string fingerprint, PageToken pageToken);
}
```

### `src/core/Feng.Backend.Core.Infrastructure/Services/DataProtectionPageTokenProtector.cs`

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Feng.Backend.Core.Application.Models;
using Feng.Backend.Core.Application.Services;
using Feng.Backend.Core.Infrastructure.Logging;
using JetBrains.Annotations;
using Microsoft.AspNetCore.DataProtection;
using Vogen;

namespace Feng.Backend.Core.Infrastructure.Services;

/// <summary>
/// <see cref="IPageTokenProtector"/> over ASP.NET Core Data Protection: the payload <c>{ q, p }</c> is encrypted and
/// integrity-protected under a purpose per read, and written as base64url.
/// </summary>
/// <param name="dataProtectionProvider">The application's Data Protection provider, whose key ring is shared by instances.</param>
/// <remarks>
/// Opted out of the sensitive-name redaction scan: the class holds no secret — the keys live in the Data Protection key
/// ring — and the payload is a bookmark of ordering values the token already exposes to its holder only in protected
/// form.
/// </remarks>
[UsedImplicitly]
[SensitiveValueOptOut]
public sealed class DataProtectionPageTokenProtector(IDataProtectionProvider dataProtectionProvider) : IPageTokenProtector
{
	private const string Purpose = "Feng.Backend.PageToken";

	private const string InvalidTokenMessage = "PageToken was not issued by this read for these query arguments.";

	/// <inheritdoc />
	public PageToken Protect(string readName, string fingerprint, IReadOnlyList<object?> position)
	{
		var payload = JsonSerializer.SerializeToUtf8Bytes(new TokenPayload(fingerprint, position));

		return PageToken.From(Base64Url.EncodeToString(CreateProtector(readName).Protect(payload)));
	}

	/// <inheritdoc />
	public IReadOnlyList<JsonElement> Unprotect(string readName, string fingerprint, PageToken pageToken)
	{
		TokenPayloadReadBack? payload;

		try
		{
			var protectedBytes = Base64Url.DecodeFromChars(pageToken.Value);
			payload = JsonSerializer.Deserialize<TokenPayloadReadBack>(CreateProtector(readName).Unprotect(protectedBytes));
		}
		catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
		{
			throw new ValueObjectValidationException(InvalidTokenMessage);
		}

		return payload is { P: not null } && payload.Q == fingerprint
			? payload.P
			: throw new ValueObjectValidationException(InvalidTokenMessage);
	}

	private IDataProtector CreateProtector(string readName) =>
		dataProtectionProvider.CreateProtector(Purpose, readName);

	// Short member names keep the token small; the payload is never read outside this class.
	[SensitiveValueOptOut]
	private sealed record TokenPayload(
		[property: JsonPropertyName("q")] string Q,
		[property: JsonPropertyName("p")] IReadOnlyList<object?> P);

	[SensitiveValueOptOut]
	private sealed record TokenPayloadReadBack(
		[property: JsonPropertyName("q")] string? Q,
		[property: JsonPropertyName("p")] IReadOnlyList<JsonElement>? P);
}
```

### `src/core/Feng.Backend.Core.Infrastructure/Extensions/MartenPagingExtensions.cs`

```csharp
using System.Text.Json;
using Feng.Backend.Core.Application.Models;
using Feng.Backend.Core.Application.Services;
using Marten;
using Marten.Linq.MatchesSql;
using Vogen;

namespace Feng.Backend.Core.Infrastructure.Extensions;

/// <summary>
/// Serves any kind of <see cref="Page{TItem}"/> from a filtered Marten LINQ query ordered by an optional ascending
/// instant column and a unique identifier column — the order every LINQ-paged read of this codebase uses.
/// </summary>
/// <remarks>
/// <para>The continuable kind seeks after the last served row through Marten's <c>MatchesSql</c>, on the duplicated
/// columns the order is read from: a <c>&gt;</c> on a Vogen strong type does not translate in LINQ. The token's
/// position is the row's instant, truncated to the database's microsecond precision so an equal-instant tie is never
/// skipped, and its identifier.</para>
/// <para>The counted kind runs an exact count of the same filtered query; the capped kind reads one row past the cap.</para>
/// </remarks>
public static class MartenPagingExtensions
{
	/// <summary>Serves the page <paramref name="paging"/> asks for.</summary>
	/// <typeparam name="TDocument">The stored document type.</typeparam>
	/// <typeparam name="TId">The primitive the identifier column holds (<see cref="string"/> or <see cref="Guid"/>).</typeparam>
	/// <param name="query">The filtered, unordered query.</param>
	/// <param name="paging">The caller's paging instruction.</param>
	/// <param name="order">Applies the order: the instant ascending when there is one, then the identifier ascending.</param>
	/// <param name="instant">The instant column and its value on a document, or <see langword="null"/> to order by identifier alone.</param>
	/// <param name="identifier">The unique identifier column and its primitive value on a document.</param>
	/// <param name="pageTokenProtector">Protects and opens the continuation token.</param>
	/// <param name="readName">The read issuing the token, isolating it from every other read.</param>
	/// <param name="fingerprint">The canonical result-defining arguments of the read.</param>
	/// <param name="cancellationToken">Propagates request cancellation.</param>
	/// <returns>The page of the requested kind.</returns>
	public static async Task<Page<TDocument>> ToPageAsync<TDocument, TId>(
		this IQueryable<TDocument> query,
		Paging paging,
		Func<IQueryable<TDocument>, IOrderedQueryable<TDocument>> order,
		(string Column, Func<TDocument, DateTime> Value)? instant,
		(string Column, Func<TDocument, TId> Value) identifier,
		IPageTokenProtector pageTokenProtector,
		string readName,
		string fingerprint,
		CancellationToken cancellationToken)
		where TDocument : notnull
		where TId : notnull
	{
		switch (paging)
		{
			case CountedPaging counted:
			{
				var totalCount = await query.CountAsync(cancellationToken);
				var itemList = await order(query)
					.Skip((counted.Page - 1) * counted.Size)
					.Take(counted.Size)
					.ToListAsync(cancellationToken);

				return new CountedPage<TDocument>(counted.Page, counted.Size, totalCount, itemList);
			}

			case CappedPaging capped:
			{
				var itemList = await order(query).Take(capped.Cap + 1).ToListAsync(cancellationToken);

				return new CappedPage<TDocument>(capped.Cap, itemList.Count > capped.Cap, [.. itemList.Take(capped.Cap)]);
			}

			case ContinuablePaging continuable:
			{
				if (continuable.PageToken is { } pageToken)
				{
					var position = pageTokenProtector.Unprotect(readName, fingerprint, pageToken);
					query = Seek<TDocument, TId>(query, instant, identifier.Column, position);
				}

				var itemList = await order(query).Take(continuable.Size + 1).ToListAsync(cancellationToken);

				if (itemList.Count <= continuable.Size)
					return new ContinuablePage<TDocument>(continuable.Size, null, itemList);

				var last = itemList[continuable.Size - 1];
				IReadOnlyList<object?> lastPosition = instant is { } criterion
					? [TruncateToMicroseconds(criterion.Value(last)), identifier.Value(last)]
					: [identifier.Value(last)];

				return new ContinuablePage<TDocument>(
					continuable.Size,
					pageTokenProtector.Protect(readName, fingerprint, lastPosition),
					[.. itemList.Take(continuable.Size)]);
			}

			default:
				throw new ArgumentOutOfRangeException(nameof(paging), paging, "Paging kind is not known.");
		}
	}

	private static IQueryable<TDocument> Seek<TDocument, TId>(
		IQueryable<TDocument> query,
		(string Column, Func<TDocument, DateTime> Value)? instant,
		string identifierColumn,
		IReadOnlyList<JsonElement> position)
		where TDocument : notnull
		where TId : notnull
	{
		try
		{
			if (instant is not { } criterion)
			{
				var afterId = (object)position[0].Deserialize<TId>()!;

				return query.Where(document => document!.MatchesSql($"{identifierColumn} > ?", afterId));
			}

			// A DateTimeOffset, not a UTC DateTime: Marten binds a raw-SQL DateTime parameter as a timestamp without time
			// zone, which Npgsql refuses for a UTC value; the instant columns are timestamptz.
			var afterInstant = (object)new DateTimeOffset(position[0].GetDateTime().ToUniversalTime());
			var afterInstantId = (object)position[1].Deserialize<TId>()!;
			var sql = $"({criterion.Column} > ? OR ({criterion.Column} = ? AND {identifierColumn} > ?))";

			return query.Where(document => document!.MatchesSql(sql, afterInstant, afterInstant, afterInstantId));
		}
		catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
		{
			// A payload that no longer has this read's shape is an invalid token, refused like any other.
			throw new ValueObjectValidationException("PageToken was not issued by this read for these query arguments.");
		}
	}

	private static DateTime TruncateToMicroseconds(DateTime value) =>
		new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
}
```

### `src/core/Feng.Backend.Core.Infrastructure/Extensions/NpgsqlPagingExtensions.cs`

```csharp
using System.Text.Json;
using Feng.Backend.Core.Application.Models;
using Feng.Backend.Core.Application.Services;
using Npgsql;
using Vogen;

namespace Feng.Backend.Core.Infrastructure.Extensions;

/// <summary>
/// Serves any kind of <see cref="Page{TItem}"/> from a raw-SQL read: the adapter supplies its filtered inner statement
/// and its ordering chain, and this class adds the seek, the order, the limit and — for a counted page — the count.
/// </summary>
/// <remarks>
/// <para>The inner statement is wrapped as <c>SELECT * FROM (inner) AS page_row</c>, so the chain, the tie-breaker
/// and the seek name the inner statement's output columns — including values it computes per request, such as a
/// relevance score — and the position written into a token is read from the same columns. The planner inlines the
/// subquery, so the inner statement's indexes still apply.</para>
/// <para>Every column name is chosen by the calling adapter from its own constants; caller values reach the statement
/// only as parameters.</para>
/// </remarks>
public static class NpgsqlPagingExtensions
{
	/// <summary>Reads the page <paramref name="paging"/> asks for.</summary>
	/// <typeparam name="TItem">The item each row is read into.</typeparam>
	/// <param name="connection">The open connection.</param>
	/// <param name="transaction">The transaction the read runs in, or <see langword="null"/>.</param>
	/// <param name="innerStatement">The filtered, unordered statement selecting every chain column and the tie-breaker.</param>
	/// <param name="bindParameterList">Binds the inner statement's parameters on each command.</param>
	/// <param name="chain">The ordering chain, in its default direction, over output columns.</param>
	/// <param name="reversed">Whether the caller asked for the opposite of every criterion's default direction.</param>
	/// <param name="tieBreakerColumn">The unique output column appended ascending.</param>
	/// <param name="readPositionValue">Reads one position value back from a token, by column, into the type it was bound as.</param>
	/// <param name="paging">The caller's paging instruction.</param>
	/// <param name="readItem">Reads one row into an item.</param>
	/// <param name="pageTokenProtector">Protects and opens the continuation token.</param>
	/// <param name="readName">The read issuing the token.</param>
	/// <param name="fingerprint">The canonical result-defining arguments the adapter bound.</param>
	/// <param name="cancellationToken">Propagates request cancellation.</param>
	/// <returns>The page of the requested kind.</returns>
	public static async Task<Page<TItem>> ReadPageAsync<TItem>(
		this NpgsqlConnection connection,
		NpgsqlTransaction? transaction,
		string innerStatement,
		Action<NpgsqlParameterCollection> bindParameterList,
		IReadOnlyList<(string Expression, bool Ascending)> chain,
		bool reversed,
		string tieBreakerColumn,
		Func<string, JsonElement, object?> readPositionValue,
		Paging paging,
		Func<NpgsqlDataReader, TItem> readItem,
		IPageTokenProtector pageTokenProtector,
		string readName,
		string fingerprint,
		CancellationToken cancellationToken)
	{
		var outer = $"SELECT * FROM ({innerStatement}) AS page_row";
		var orderBy = " ORDER BY " + chain.ToOrderByFragment(reversed, tieBreakerColumn);
		var columnList = chain.Select(criterion => criterion.Expression).Append(tieBreakerColumn).ToList();

		switch (paging)
		{
			case CountedPaging counted:
			{
				await using var countCommand = CreateCommand(
					connection, transaction, $"SELECT count(*) FROM ({innerStatement}) AS page_row", bindParameterList);
				var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

				await using var command = CreateCommand(
					connection, transaction, outer + orderBy + " OFFSET @page_offset LIMIT @page_limit", bindParameterList);
				command.Parameters.AddWithValue("page_offset", (counted.Page - 1) * counted.Size);
				command.Parameters.AddWithValue("page_limit", counted.Size);

				var (itemList, _) = await ReadAsync(command, counted.Size, readItem, columnList, cancellationToken);

				return new CountedPage<TItem>(counted.Page, counted.Size, totalCount, itemList);
			}

			case CappedPaging capped:
			{
				await using var command = CreateCommand(
					connection, transaction, outer + orderBy + " LIMIT @page_limit", bindParameterList);
				command.Parameters.AddWithValue("page_limit", capped.Cap + 1);

				var (itemList, more) = await ReadAsync(command, capped.Cap, readItem, columnList, cancellationToken);

				return new CappedPage<TItem>(capped.Cap, more is not null, itemList);
			}

			case ContinuablePaging continuable:
			{
				var where = string.Empty;
				IReadOnlyList<KeyValuePair<string, object>> seekParameterList = [];

				if (continuable.PageToken is { } pageToken)
				{
					var position = ReadPosition(
						pageTokenProtector.Unprotect(readName, fingerprint, pageToken), columnList, readPositionValue);
					(where, seekParameterList) = chain.ToSeekPredicate(reversed, tieBreakerColumn, position);
					where = " WHERE " + where;
				}

				await using var command = CreateCommand(
					connection, transaction, outer + where + orderBy + " LIMIT @page_limit", bindParameterList);
				foreach (var (name, value) in seekParameterList)
					command.Parameters.AddWithValue(name, value);
				command.Parameters.AddWithValue("page_limit", continuable.Size + 1);

				var (itemList, more) = await ReadAsync(command, continuable.Size, readItem, columnList, cancellationToken);

				return new ContinuablePage<TItem>(
					continuable.Size,
					more is null ? null : pageTokenProtector.Protect(readName, fingerprint, more),
					itemList);
			}

			default:
				throw new ArgumentOutOfRangeException(nameof(paging), paging, "Paging kind is not known.");
		}
	}

	private static NpgsqlCommand CreateCommand(
		NpgsqlConnection connection,
		NpgsqlTransaction? transaction,
		string statement,
		Action<NpgsqlParameterCollection> bindParameterList)
	{
		var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = statement;
		bindParameterList(command.Parameters);

		return command;
	}

	// Reads up to `size` items. When a further row exists, returns the position of the last item served — the
	// bookmark the next page continues after — and discards the extra row.
	private static async Task<(IReadOnlyList<TItem> ItemList, IReadOnlyList<object?>? LastPosition)> ReadAsync<TItem>(
		NpgsqlCommand command,
		int size,
		Func<NpgsqlDataReader, TItem> readItem,
		IReadOnlyList<string> columnList,
		CancellationToken cancellationToken)
	{
		var itemList = new List<TItem>(size);
		IReadOnlyList<object?>? lastPosition = null;

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);

		while (await reader.ReadAsync(cancellationToken))
		{
			if (itemList.Count == size)
				return (itemList, lastPosition);

			itemList.Add(readItem(reader));
			lastPosition = [.. columnList.Select(column => reader[column] is DBNull ? null : reader[column])];
		}

		return (itemList, null);
	}

	private static IReadOnlyList<object?> ReadPosition(
		IReadOnlyList<JsonElement> position,
		IReadOnlyList<string> columnList,
		Func<string, JsonElement, object?> readPositionValue)
	{
		try
		{
			return position.Count == columnList.Count
				? [.. columnList.Select((column, index) =>
					position[index].ValueKind == JsonValueKind.Null ? null : readPositionValue(column, position[index]))]
				: throw new FormatException();
		}
		catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
		{
			// A payload that no longer has this read's shape is an invalid token, refused like any other.
			throw new ValueObjectValidationException("PageToken was not issued by this read for these query arguments.");
		}
	}
}
```

### `src/core/Feng.Backend.Core.Infrastructure/Extensions/ApiDocumentationServiceExtensions.cs`

```csharp
using Feng.Backend.Core.Application.Models;
using Feng.Backend.Core.Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Feng.Backend.Core.Infrastructure.Extensions;

/// <summary>
/// Registers the OpenAPI document and its Keycloak OAuth2 security scheme in the service collection.
/// </summary>
public static class ApiDocumentationServiceExtensions
{
	internal const string OAuthSecuritySchemeName = "OAuth2";

	/// <summary>
	/// Adds OpenAPI generation with document metadata and a Keycloak password-flow OAuth2 security scheme.
	/// </summary>
	/// <param name="services">The service collection to register the OpenAPI services on.</param>
	/// <param name="title">The document title advertised in the generated OpenAPI document.</param>
	/// <param name="version">The document version advertised in the generated OpenAPI document.</param>
	/// <returns>The same service collection, to allow chaining.</returns>
	public static IServiceCollection AddApiDocumentation(
		this IServiceCollection services,
		string title = "Feng API",
		string version = "v1")
	{
		services.AddOpenApi(options =>
		{
			options.AddDocumentTransformer((document, _, _) =>
			{
				document.Info = new() { Title = title, Version = version };
				return Task.CompletedTask;
			});

			options.AddDocumentTransformer((document, context, _) =>
			{
				var keycloakOptions = context.ApplicationServices
					.GetRequiredService<IOptions<KeycloakAuthenticationOptions>>().Value;

				var tokenUrl = new Uri(keycloakOptions.TokenUrl);

				document.Components ??= new OpenApiComponents();
				document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
				document.Components.SecuritySchemes[OAuthSecuritySchemeName] = new OpenApiSecurityScheme
				{
					Type = SecuritySchemeType.OAuth2,
					Flows = new OpenApiOAuthFlows
					{
						Password = new OpenApiOAuthFlow
						{
							TokenUrl = tokenUrl,
							Scopes = new Dictionary<string, string>
							{
								["openid"] = "OpenID identity token",
								["profile"] = "User profile information"
							}
						}
					}
				};

				document.Security ??= new List<OpenApiSecurityRequirement>();
				document.Security.Add(new OpenApiSecurityRequirement
				{
					[new OpenApiSecuritySchemeReference(OAuthSecuritySchemeName, document, null)] = new List<string>()
				});

				return Task.CompletedTask;
			});

			options.AddOperationTransformer(DescribePageResponseAsync);
		});

		services.AddEndpointsApiExplorer();

		return services;
	}

	/// <summary>
	/// Rewrites a <c>200</c> response declared as the abstract <see cref="Page{TItem}"/> into a <c>oneOf</c> of the three
	/// concrete page schemas, which the generator cannot infer from the abstract type.
	/// </summary>
	/// <param name="operation">The operation being described.</param>
	/// <param name="context">The operation's API description and schema services.</param>
	/// <param name="cancellationToken">Propagates cancellation.</param>
	/// <returns>A task that completes when the response is described.</returns>
	/// <remarks>
	/// A list endpoint answers whichever kind the client named in <c>paging</c>, so its response is any one of the three
	/// shapes. The paging query parameters need nothing here: they come from the shared request base every list request
	/// extends.
	/// </remarks>
	private static async Task DescribePageResponseAsync(
		OpenApiOperation operation,
		OpenApiOperationTransformerContext context,
		CancellationToken cancellationToken)
	{
		var pageType = context.Description.SupportedResponseTypes
			.FirstOrDefault(responseType => responseType.StatusCode == StatusCodes.Status200OK)?.Type;

		if (pageType is not { IsGenericType: true } || pageType.GetGenericTypeDefinition() != typeof(Page<>)
			|| operation.Responses?.TryGetValue("200", out var response) != true || response?.Content is null)
			return;

		var itemType = pageType.GetGenericArguments()[0];
		var schemaList = new List<IOpenApiSchema>();

		foreach (var kind in new[] { typeof(ContinuablePage<>), typeof(CountedPage<>), typeof(CappedPage<>) })
			schemaList.Add(await context.GetOrCreateSchemaAsync(kind.MakeGenericType(itemType), null, cancellationToken));

		foreach (var mediaType in response.Content.Values)
			mediaType.Schema = new OpenApiSchema { OneOf = schemaList };
	}
}
```

### `src/core/Feng.Backend.Core.Presentation/Requests/PagingRequest.cs`

```csharp
using Feng.Backend.Core.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Feng.Backend.Core.Presentation.Requests;

/// <summary>
/// The paging inputs every list request extends, bound once from the query string. The controller turns them into the
/// read's <see cref="Paging"/> with <see cref="PagingRequestExtensions.ToPaging"/>, inline.
/// </summary>
/// <remarks>
/// The values are passed through unbounded: the <see cref="Paging"/> factories are the one place that defaults, clamps
/// and refuses them, so no <c>[Range]</c> annotation belongs here.
/// </remarks>
public abstract class PagingRequest
{
	/// <summary>The kind of page requested; continuable when absent.</summary>
	[FromQuery(Name = "paging")]
	public PagingKind? Paging { get; init; }

	/// <summary>The one-based page number of a counted page.</summary>
	[FromQuery(Name = "page")]
	public int? Page { get; init; }

	/// <summary>The page size of a continuable or counted page.</summary>
	[FromQuery(Name = "size")]
	public int? Size { get; init; }

	/// <summary>The cap of a capped page.</summary>
	[FromQuery(Name = "cap")]
	public int? Cap { get; init; }

	/// <summary>The token of the previous continuable page, returned unchanged.</summary>
	[FromQuery(Name = "pageToken")]
	public PageToken? PageToken { get; init; }
}
```

### `src/core/Feng.Backend.Core.Presentation/Requests/PagingKind.cs`

```csharp
namespace Feng.Backend.Core.Presentation.Requests;

/// <summary>The kind of page a client asks a list endpoint for, named in the <c>paging</c> query parameter.</summary>
public enum PagingKind
{
	/// <summary>A continuable page navigated by token; the default when no kind is named.</summary>
	Continuable = 1,

	/// <summary>A counted page navigated by number, carrying the exact total.</summary>
	Counted = 2,

	/// <summary>One capped screen with no continuation.</summary>
	Capped = 3
}
```

### `src/core/Feng.Backend.Core.Presentation/Requests/PagingRequestExtensions.cs`

```csharp
using Feng.Backend.Core.Application.Models;
using Vogen;

namespace Feng.Backend.Core.Presentation.Requests;

/// <summary>Builds a read's <see cref="Paging"/> from the paging inputs of a list request.</summary>
public static class PagingRequestExtensions
{
	/// <summary>
	/// Builds the paging instruction of the requested kind, refusing any argument that belongs to another kind — even
	/// one carrying that kind's default value.
	/// </summary>
	/// <param name="request">The bound paging inputs.</param>
	/// <returns>The bounded paging instruction.</returns>
	/// <exception cref="ValueObjectValidationException">
	/// An argument of another kind is present, or the factory refuses a value.
	/// </exception>
	public static Paging ToPaging(this PagingRequest request) =>
		request.Paging switch
		{
			null or PagingKind.Continuable => Refuse(request, "continuable", page: true, cap: true)
				?? Paging.Continuable(request.PageToken, request.Size),
			PagingKind.Counted => Refuse(request, "counted", pageToken: true, cap: true)
				?? Paging.Counted(request.Page, request.Size),
			PagingKind.Capped => Refuse(request, "capped", page: true, size: true, pageToken: true)
				?? Paging.Capped(request.Cap),
			_ => throw new ValueObjectValidationException($"Paging kind '{request.Paging}' is not known.")
		};

	// Returns null when every refused argument is absent; throws naming the first one present otherwise.
	private static Paging? Refuse(
		PagingRequest request,
		string kind,
		bool page = false,
		bool size = false,
		bool cap = false,
		bool pageToken = false)
	{
		var intruder =
			page && request.Page is not null ? "page"
			: size && request.Size is not null ? "size"
			: cap && request.Cap is not null ? "cap"
			: pageToken && request.PageToken is not null ? "pageToken"
			: null;

		return intruder is null
			? null
			: throw new ValueObjectValidationException($"Paging argument '{intruder}' does not belong to {kind} paging.");
	}
}
```

### `src/core/Feng.Backend.Core.Application/QueryBusExtensions.cs`

```csharp
using System.Runtime.CompilerServices;
using Feng.Backend.Core.Application.Models;
using Zooper.Fox;

namespace Feng.Backend.Core.Application;

/// <summary>Traversal of every item of a paged query through the <see cref="IQueryBus"/>.</summary>
public static class QueryBusExtensions
{
	/// <summary>
	/// Dispatches the same query page by page, following each <see cref="ContinuablePage{TItem}.NextPageToken"/> until
	/// none is returned, and yields every item. The query's own error is yielded once as a terminal <c>Left</c>.
	/// </summary>
	/// <remarks>
	/// <para>One dispatch per page, each answering one query result. Traversal is live: it takes no snapshot, so a row
	/// inserted ahead of the bookmark is followed, one deleted is simply not returned, and a row whose ordering value
	/// moves across the bookmark may be omitted or repeated. It keeps no checkpoint and never retries: after a failure,
	/// a new traversal starts from the first page, and a consumer writing per item tolerates receiving an item again.</para>
	/// <para>No buffering or prefetch: the next page is dispatched only when the consumer asks for its first item, so
	/// disposing early dispatches nothing further.</para>
	/// </remarks>
	/// <typeparam name="TError">The query's error type.</typeparam>
	/// <typeparam name="TItem">The page's item type.</typeparam>
	/// <param name="queryBus">The bus the query is dispatched through.</param>
	/// <param name="buildQuery">
	/// Builds the query for a token — <see langword="null"/> for the first page — with continuable paging.
	/// </param>
	/// <param name="cancellationToken">Flows into every dispatch.</param>
	/// <returns>Every item as <c>Right</c>, or the items of the pages read so far followed by one <c>Left</c>.</returns>
	/// <exception cref="InvalidOperationException">A dispatch answered a page that is not continuable.</exception>
	public static async IAsyncEnumerable<Either<TError, TItem>> QueryAllAsync<TError, TItem>(
		this IQueryBus queryBus,
		Func<PageToken?, IQuery<Either<TError, Page<TItem>>>> buildQuery,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		PageToken? pageToken = null;

		do
		{
			var result = await queryBus.QueryAsync(buildQuery(pageToken), cancellationToken);

			if (result.IsLeft)
			{
				yield return Either<TError, TItem>.FromLeft(result.Left);
				yield break;
			}

			if (result.Right is not ContinuablePage<TItem> page)
				throw new InvalidOperationException(
					$"QueryAllAsync traverses continuable pages only; the query answered {result.Right.GetType().Name}.");

			foreach (var item in page.ItemList)
				yield return Either<TError, TItem>.FromRight(item);

			pageToken = page.NextPageToken;
		}
		while (pageToken is not null);
	}
}
```

## Appendix B: Existing Tests

Port these first. `PageResponseDocumentationTests` runs against Feng's host fixture; rewrite it against the example project.

### `src/core/tests/Feng.Backend.Core.Application.UnitTests/Models/PagingTests.cs`

```csharp
using Feng.Backend.Core.Application.Models;
using Vogen;

namespace Feng.Backend.Core.Application.UnitTests.Models;

/// <summary>
/// Verifies that the <see cref="Paging"/> factories default and clamp sizes, refuse a counted page below one or beyond
/// the depth bound, and never bound continuable paging by depth.
/// </summary>
public class PagingTests
{
	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(-3)]
	public void Continuable_WhenSizeIsMissingOrNonPositive_DefaultsToTwenty(int? size)
	{
		var sut = Paging.Continuable(null, size);

		Assert.Equal(Paging.DefaultSize, sut.Size);
	}

	[Fact]
	public void Continuable_WhenSizeExceedsMaximum_ClampsToFifty()
	{
		var sut = Paging.Continuable(null, 500);

		Assert.Equal(Paging.MaximumSize, sut.Size);
	}

	[Fact]
	public void Continuable_WhenTokenIsGiven_KeepsItWithoutDepthCheck()
	{
		var token = PageToken.From("any-token_value");

		var sut = Paging.Continuable(token, 50);

		Assert.Equal(token, sut.PageToken);
		Assert.Equal(50, sut.Size);
	}

	[Fact]
	public void Counted_WhenPageIsMissing_DefaultsToFirstPage()
	{
		var sut = Paging.Counted(null, null);

		Assert.Equal(1, sut.Page);
		Assert.Equal(Paging.DefaultSize, sut.Size);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void Counted_WhenPageIsBelowOne_Throws(int page)
	{
		Assert.Throws<ValueObjectValidationException>(() => Paging.Counted(page, 20));
	}

	[Fact]
	public void Counted_WhenDepthIsAtBound_Serves()
	{
		var sut = Paging.Counted(50, 20);

		Assert.Equal(50, sut.Page);
	}

	[Fact]
	public void Counted_WhenDepthExceedsBound_Throws()
	{
		var exception = Assert.Throws<ValueObjectValidationException>(() => Paging.Counted(51, 20));

		Assert.Contains("1000", exception.Message);
	}

	[Fact]
	public void Counted_WhenPageThousandAtSizeOne_ReachesExactlyTheBoundAndServes()
	{
		var sut = Paging.Counted(1000, 1);

		Assert.Equal(1000, sut.Page);
		Assert.Equal(1, sut.Size);
	}

	[Fact]
	public void Counted_WhenPageThousandAtDefaultSize_Throws()
	{
		Assert.Throws<ValueObjectValidationException>(() => Paging.Counted(1000, null));
	}

	[Theory]
	[InlineData(null, 20)]
	[InlineData(0, 20)]
	[InlineData(10, 10)]
	[InlineData(500, 50)]
	public void Capped_BoundsCapLikeASize(int? cap, int expected)
	{
		var sut = Paging.Capped(cap);

		Assert.Equal(expected, sut.Cap);
	}
}
```

### `src/core/tests/Feng.Backend.Core.Application.UnitTests/Models/PageTests.cs`

```csharp
using System.Text.Json;
using Feng.Backend.Core.Application.Models;

namespace Feng.Backend.Core.Application.UnitTests.Models;

/// <summary>
/// Verifies that each concrete <see cref="Page{TItem}"/> keeps its kind and result-level context through
/// <see cref="Page{TItem}.Map{TOut}"/>, and serializes its own fields through an <see cref="object"/>-typed value.
/// </summary>
public class PageTests
{
	private static readonly IReadOnlyList<int> ItemList = [1, 2, 3];

	[Fact]
	public void Map_OnContinuablePage_KeepsSizeAndNextPageToken()
	{
		var token = PageToken.From("next");
		Page<int> sut = new ContinuablePage<int>(3, token, ItemList);

		var mapped = Assert.IsType<ContinuablePage<string>>(sut.Map(item => item.ToString()));

		Assert.Equal(3, mapped.Size);
		Assert.Equal(token, mapped.NextPageToken);
		Assert.Equal(["1", "2", "3"], mapped.ItemList);
	}

	[Fact]
	public void Map_OnCountedPage_KeepsPageSizeAndTotalCount()
	{
		Page<int> sut = new CountedPage<int>(2, 3, 62, ItemList);

		var mapped = Assert.IsType<CountedPage<string>>(sut.Map(item => item.ToString()));

		Assert.Equal(2, mapped.Page);
		Assert.Equal(3, mapped.Size);
		Assert.Equal(62, mapped.TotalCount);
		Assert.Equal(["1", "2", "3"], mapped.ItemList);
	}

	[Fact]
	public void Map_OnCappedPage_KeepsCapAndTruncated()
	{
		Page<int> sut = new CappedPage<int>(3, true, ItemList);

		var mapped = Assert.IsType<CappedPage<string>>(sut.Map(item => item.ToString()));

		Assert.Equal(3, mapped.Cap);
		Assert.True(mapped.Truncated);
		Assert.Equal(["1", "2", "3"], mapped.ItemList);
	}

	[Fact]
	public void Serialize_ContinuablePageAsObject_WritesItsOwnFieldsWithoutTypeDiscriminator()
	{
		object sut = new ContinuablePage<int>(3, PageToken.From("next"), ItemList);

		var json = JsonSerializer.Serialize(sut, JsonSerializerOptions.Web);

		Assert.Equal("""{"size":3,"nextPageToken":"next","itemList":[1,2,3]}""", json);
	}

	[Fact]
	public void Serialize_CountedPageAsObject_WritesItsOwnFieldsWithoutTypeDiscriminator()
	{
		object sut = new CountedPage<int>(2, 3, 62, ItemList);

		var json = JsonSerializer.Serialize(sut, JsonSerializerOptions.Web);

		Assert.Equal("""{"page":2,"size":3,"totalCount":62,"itemList":[1,2,3]}""", json);
	}

	[Fact]
	public void Serialize_CappedPageAsObject_WritesItsOwnFieldsWithoutTypeDiscriminator()
	{
		object sut = new CappedPage<int>(3, true, ItemList);

		var json = JsonSerializer.Serialize(sut, JsonSerializerOptions.Web);

		Assert.Equal("""{"cap":3,"truncated":true,"itemList":[1,2,3]}""", json);
	}
}
```

### `src/core/tests/Feng.Backend.Core.Application.UnitTests/Models/PageTokenTests.cs`

```csharp
using Feng.Backend.Core.Application.Models;
using Vogen;

namespace Feng.Backend.Core.Application.UnitTests.Models;

/// <summary>
/// Verifies that <see cref="PageToken"/> accepts base64url text and refuses blank or non-base64url input.
/// </summary>
public class PageTokenTests
{
	[Fact]
	public void From_WhenValueIsBase64Url_Succeeds()
	{
		var sut = PageToken.From("CfDJ8Abc-123_xyz");

		Assert.Equal("CfDJ8Abc-123_xyz", sut.Value);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("abc+def")]
	[InlineData("abc/def")]
	[InlineData("abcdef==")]
	public void From_WhenValueIsBlankOrNotBase64Url_Throws(string value)
	{
		Assert.Throws<ValueObjectValidationException>(() => PageToken.From(value));
	}
}
```

### `src/core/tests/Feng.Backend.Core.Infrastructure.UnitTests/Services/DataProtectionPageTokenProtectorTests.cs`

```csharp
using Feng.Backend.Core.Application.Models;
using Feng.Backend.Core.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Shouldly;
using Vogen;

namespace Feng.Backend.Core.Infrastructure.UnitTests.Services;

/// <summary>
/// Verifies that <see cref="DataProtectionPageTokenProtector"/> round-trips a position, and refuses a token that was
/// altered, issued by another read or issued for other query arguments — but never because of its age.
/// </summary>
public class DataProtectionPageTokenProtectorTests
{
	private const string ReadName = "SearchClubRelationshipsAsync";

	private const string Fingerprint = "club=1|term=ada|sort=name|asc";

	private static readonly IReadOnlyList<object?> Position = ["ada", null, 0.75, "3f2b"];

	private readonly IDataProtectionProvider _provider = new EphemeralDataProtectionProvider();

	private readonly DataProtectionPageTokenProtector _sut;

	public DataProtectionPageTokenProtectorTests() => _sut = new DataProtectionPageTokenProtector(_provider);

	[Fact]
	public void Unprotect_WhenTokenIsUnchanged_ReturnsThePosition()
	{
		var token = _sut.Protect(ReadName, Fingerprint, Position);

		var position = _sut.Unprotect(ReadName, Fingerprint, token);

		position.Count.ShouldBe(4);
		position[0].GetString().ShouldBe("ada");
		position[1].ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
		position[2].GetDouble().ShouldBe(0.75);
		position[3].GetString().ShouldBe("3f2b");
	}

	[Fact]
	public void Unprotect_WhenOneCharacterIsAltered_Throws()
	{
		var value = _sut.Protect(ReadName, Fingerprint, Position).Value;
		var middle = value.Length / 2;
		var altered = PageToken.From(value[..middle] + (value[middle] == 'A' ? 'B' : 'A') + value[(middle + 1)..]);

		Should.Throw<ValueObjectValidationException>(() => _sut.Unprotect(ReadName, Fingerprint, altered));
	}

	[Fact]
	public void Unprotect_WhenTokenWasIssuedByAnotherRead_Throws()
	{
		var token = _sut.Protect("SearchClubsAsync", Fingerprint, Position);

		Should.Throw<ValueObjectValidationException>(() => _sut.Unprotect(ReadName, Fingerprint, token));
	}

	[Fact]
	public void Unprotect_WhenFingerprintDiffers_Throws()
	{
		var token = _sut.Protect(ReadName, Fingerprint, Position);

		Should.Throw<ValueObjectValidationException>(
			() => _sut.Unprotect(ReadName, "club=1|term=bob|sort=name|asc", token));
	}

	[Fact]
	public void Unprotect_WhenOnlyTheSizeChanged_Succeeds()
	{
		// Size is not part of the fingerprint: the adapter composes it without the size, so a caller may change the
		// page size between pages and the same fingerprint still opens the token.
		var token = _sut.Protect(ReadName, Fingerprint, Position);

		Should.NotThrow(() => _sut.Unprotect(ReadName, Fingerprint, token));
	}

	[Fact]
	public void Unprotect_WhenTokenWasIssuedByAnEarlierProtector_Succeeds()
	{
		// No time limit: a token outlives the protector instance (and request) that issued it.
		var token = new DataProtectionPageTokenProtector(_provider).Protect(ReadName, Fingerprint, Position);

		Should.NotThrow(() => _sut.Unprotect(ReadName, Fingerprint, token));
	}

	[Fact]
	public void Unprotect_WhenTokenIsNotProtectedData_Throws()
	{
		Should.Throw<ValueObjectValidationException>(
			() => _sut.Unprotect(ReadName, Fingerprint, PageToken.From("bm90LWEtdG9rZW4")));
	}
}
```

### `src/core/tests/Feng.Backend.Core.Presentation.UnitTests/Requests/PagingRequestExtensionsTests.cs`

```csharp
using Feng.Backend.Core.Application.Models;
using Feng.Backend.Core.Presentation.Requests;
using Vogen;

namespace Feng.Backend.Core.Presentation.UnitTests.Requests;

/// <summary>
/// Verifies that <see cref="PagingRequestExtensions.ToPaging"/> builds the requested kind, defaults to continuable, and
/// refuses every argument that belongs to another kind.
/// </summary>
public class PagingRequestExtensionsTests
{
	private sealed class TestPagingRequest : PagingRequest;

	private static readonly PageToken Token = PageToken.From("token");

	[Fact]
	public void ToPaging_WhenNoKindIsNamed_BuildsContinuable()
	{
		var paging = new TestPagingRequest { Size = 10, PageToken = Token }.ToPaging();

		var continuable = Assert.IsType<ContinuablePaging>(paging);
		Assert.Equal(10, continuable.Size);
		Assert.Equal(Token, continuable.PageToken);
	}

	[Fact]
	public void ToPaging_WhenContinuableIsNamed_BuildsContinuable()
	{
		var paging = new TestPagingRequest { Paging = PagingKind.Continuable }.ToPaging();

		Assert.IsType<ContinuablePaging>(paging);
	}

	[Fact]
	public void ToPaging_WhenCountedIsNamed_BuildsCounted()
	{
		var paging = new TestPagingRequest { Paging = PagingKind.Counted, Page = 3, Size = 10 }.ToPaging();

		var counted = Assert.IsType<CountedPaging>(paging);
		Assert.Equal(3, counted.Page);
		Assert.Equal(10, counted.Size);
	}

	[Fact]
	public void ToPaging_WhenCappedIsNamed_BuildsCapped()
	{
		var paging = new TestPagingRequest { Paging = PagingKind.Capped, Cap = 10 }.ToPaging();

		Assert.Equal(10, Assert.IsType<CappedPaging>(paging).Cap);
	}

	public static TheoryData<TestPagingRequestCase> IncompatibleCaseList =>
	[
		new(null, Page: 1),
		new(null, Cap: 10),
		new(PagingKind.Continuable, Page: 2),
		new(PagingKind.Counted, PageToken: true),
		new(PagingKind.Counted, Cap: 10),
		new(PagingKind.Capped, Page: 4),
		new(PagingKind.Capped, Size: 20),
		new(PagingKind.Capped, PageToken: true)
	];

	[Theory]
	[MemberData(nameof(IncompatibleCaseList))]
	public void ToPaging_WhenArgumentBelongsToAnotherKind_Throws(TestPagingRequestCase testCase)
	{
		var request = new TestPagingRequest
		{
			Paging = testCase.Kind,
			Page = testCase.Page,
			Size = testCase.Size,
			Cap = testCase.Cap,
			PageToken = testCase.PageToken ? Token : null
		};

		Assert.Throws<ValueObjectValidationException>(() => request.ToPaging());
	}

	[Fact]
	public void ToPaging_WhenCountedDepthExceedsBound_Throws()
	{
		var request = new TestPagingRequest { Paging = PagingKind.Counted, Page = 51, Size = 20 };

		Assert.Throws<ValueObjectValidationException>(() => request.ToPaging());
	}

	/// <summary>One combination of a named kind and the arguments supplied beside it.</summary>
	public sealed record TestPagingRequestCase(
		PagingKind? Kind,
		int? Page = null,
		int? Size = null,
		int? Cap = null,
		bool PageToken = false);
}
```

### `src/runner/tests/Feng.Backend.IntegrationTests/IntegrationTests/Infrastructure/PageResponseDocumentationTests.cs`

```csharp
using System.Text.Json;
using Shouldly;

namespace Feng.Backend.IntegrationTests.IntegrationTests.Infrastructure;

/// <summary>
/// Verifies the generated OpenAPI document describes a list endpoint's response as any one of the three concrete
/// page shapes, and its paging inputs as query parameters inherited from the shared request base.
/// </summary>
[Collection("Integration")]
public sealed class PageResponseDocumentationTests(IntegrationTestHostFixture fixture)
{
	private const string MemberListPath = "/api/clubs/{clubId}/members";

	private async Task<JsonElement> DocumentAsync()
	{
		var client = fixture.Factory.CreateClient();

		using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

		return document.RootElement.Clone();
	}

	// A schema is either inline or a reference into the components; either way its own property names decide which
	// page shape it is.
	private static IReadOnlyList<string> PropertyNamesOf(JsonElement document, JsonElement schema)
	{
		if (schema.TryGetProperty("$ref", out var reference))
		{
			var name = reference.GetString()!.Split('/')[^1];
			schema = document.GetProperty("components").GetProperty("schemas").GetProperty(name);
		}

		return [.. schema.GetProperty("properties").EnumerateObject().Select(property => property.Name)];
	}

	[Fact]
	public async Task TheMemberList_Answers_OneOfTheThreeConcretePageSchemas()
	{
		var document = await DocumentAsync();

		var oneOf = document.GetProperty("paths").GetProperty(MemberListPath).GetProperty("get")
			.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json")
			.GetProperty("schema").GetProperty("oneOf");

		var shapeList = oneOf.EnumerateArray().Select(schema => PropertyNamesOf(document, schema)).ToList();

		shapeList.Count.ShouldBe(3);
		shapeList.ShouldContain(names => names.Contains("nextPageToken") && names.Contains("itemList"));
		shapeList.ShouldContain(names => names.Contains("totalCount") && names.Contains("page"));
		shapeList.ShouldContain(names => names.Contains("truncated") && names.Contains("cap"));
	}

	[Fact]
	public async Task TheMemberList_TakesEveryPagingInputFromTheQueryString()
	{
		var document = await DocumentAsync();

		var parameterNameList = document.GetProperty("paths").GetProperty(MemberListPath).GetProperty("get")
			.GetProperty("parameters").EnumerateArray()
			.Where(parameter => parameter.GetProperty("in").GetString() == "query")
			.Select(parameter => parameter.GetProperty("name").GetString())
			.ToList();

		parameterNameList.ShouldContain("paging");
		parameterNameList.ShouldContain("page");
		parameterNameList.ShouldContain("size");
		parameterNameList.ShouldContain("cap");
		parameterNameList.ShouldContain("pageToken");
	}
}
```

## Appendix C: Behaviour Specifications

OpenSpec delta specs from Feng's `caller-selected-paging` change. Entity names (club, account, relationship) are Feng's; the rules are the package's acceptance criteria. The design document follows them and explains the choices.

### `openspec/changes/caller-selected-paging/specs/paged-reads/spec.md`

```markdown
# Spec Delta

## MODIFIED Requirements

### Requirement: A read whose result set is not bounded by the caller's own data is paged

A read flow SHALL return a paged result whenever it returns more than one row, unless its rows are defined by the product rather than added through use. A list scoped to one caller or one club — their invitations, their join requests, their ownership offers, a club's blocklist, a club's pending lists — grows for as long as the system runs and SHALL page like a roster or a search. A paged read SHALL NOT offer an unpaged form of the same query as a fallback. A product-defined catalogue, whose rows are fixed by the product and not by use, MAY return a plain list.

#### Scenario: Unbounded read

- **WHEN** a read returns clubs matched across the whole system
- **THEN** it returns a paged result

#### Scenario: Caller-bounded read

- **WHEN** a read returns the invitations addressed to the calling account
- **THEN** it returns a paged result, with a total order, like any other list

#### Scenario: Product-defined read

- **WHEN** a read returns the sports catalogue
- **THEN** it may return a plain list, because its rows are defined by the product

### Requirement: Three paged result contracts are distinguished by what a client can render

The system SHALL offer exactly three paged result contracts, each a generic container over an item type, and the caller SHALL choose one per request by what it can render rather than by what the response happens to contain:

- A **counted page** carries the page number, the page size, the total number of matching items, and the items. It lets a client render a total and jump to an arbitrary page, and it costs an evaluation of every match on every request.
- A **continuable page** carries the page size, the next continuation token when a further page exists, and the items. It lets a client render "load more" and step forward through pages, and it costs the page plus one item.
- A **capped page** carries the cap, whether the result was truncated at that cap, and the items. It lets a client render one screen of best results with no continuation, and it costs the cap.

Each contract SHALL carry result-level context beside its items; none SHALL be a bare wrapper around a list, which the plain list already expresses. One generic page SHALL run from the port to the edge: the port returns it over the read model and the use case maps its items without naming the kind.

#### Scenario: A client needs a total and arbitrary page jumps

- **WHEN** a request asks for a counted page to render "page 3 of 62"
- **THEN** the read returns a counted page and accepts the cost of evaluating every match

#### Scenario: A client needs only "load more"

- **WHEN** a request asks for a continuable page
- **THEN** the read returns a continuable page

#### Scenario: A client needs one screen of best results

- **WHEN** a request asks for a capped page
- **THEN** the read returns a capped page, which states whether it truncated

### Requirement: A continuable page reports whether more exists, not how many matched

A continuable page SHALL report that a further page exists only by carrying a `NextPageToken`, and SHALL NOT report a total count or a separate boolean. Its next token SHALL be determined without evaluating the full matching set.

#### Scenario: Further page exists

- **WHEN** a caller requests a continuable page and more items match than fit on it
- **THEN** the result carries a `NextPageToken` and only the requested page of items

#### Scenario: No further page

- **WHEN** a caller requests the last continuable page
- **THEN** the result carries no `NextPageToken`

### Requirement: The server bounds page size and clamps rather than trusting the client

A paged read SHALL apply a default page size of 20 when the client supplies none or a non-positive value, and SHALL cap page size at 50. A request for a larger page SHALL be silently clamped to the cap and served, because a smaller page than requested is still a correct answer to the question asked. A capped page's cap SHALL be bounded by the same default and maximum. The bounds SHALL be applied once, where the paging instruction is built, and SHALL NOT be resolved by a use case; a bus caller SHALL be subject to the same bounds.

These bounds are starting values, to be tuned against real usage.

#### Scenario: No size supplied

- **WHEN** a caller requests a page without stating a size
- **THEN** the system returns at most 20 items and reports the size it used

#### Scenario: Oversized page requested

- **WHEN** a caller requests a page size of 500
- **THEN** the system returns at most 50 items, reports 50 as the size, and raises no error

#### Scenario: Oversized cap requested

- **WHEN** a caller requests a capped page with a cap of 500
- **THEN** the system returns at most 50 items, reports 50 as the cap, and raises no error

#### Scenario: A module caller is bounded alike

- **WHEN** a module dispatches a paged query with a size of 500
- **THEN** the page is bounded to 50 before the store is asked

### Requirement: Paging depth is bounded and exceeding it is an error

A counted read SHALL reject a request whose page number multiplied by its page size exceeds 1000, and SHALL reject a page number below one. The system SHALL NOT clamp depth or normalize the page silently, because serving a different page under the requested number is a wrong answer presented as a right one. A continuable read has no offset and SHALL NOT be subject to the depth bound; a capped read has no page.

The bound exists for a caller reaching the interface directly: an unbounded offset makes the store fetch and discard an unbounded number of rows to serve one page.

#### Scenario: Depth within the bound

- **WHEN** a caller requests counted page 50 at size 20
- **THEN** the system serves the page

#### Scenario: Depth beyond the bound

- **WHEN** a caller requests counted page 51 at size 20, or page 1000 at any size
- **THEN** the system returns the shared invalid-value error naming the depth bound, and no page

#### Scenario: Page below one

- **WHEN** a caller requests counted page 0 or a negative page
- **THEN** the system returns the shared invalid-value error, and no page is served as page 1

#### Scenario: Continuation beyond the depth

- **WHEN** a caller continues a continuable read past 1000 items by token
- **THEN** the system serves the page

### Requirement: A paged read imposes a total order on its results

A paged read of every kind SHALL order its results by a criterion that is unique across the result set, either alone or as a tie-breaker appended to the client's chosen ordering. Without a total order, the store may return equal items in a different sequence per request, so one item can appear on two pages while another is never returned at all. For a fixed result set, each matching item SHALL be returned exactly once across the sequence of pages; for a result set whose ordering values change during paging, the omissions and repeats of `paged-traversal` are accepted.

#### Scenario: Equal sort values across a page boundary

- **WHEN** a caller pages through results in which many items share the value being sorted on
- **THEN** each matching item is returned exactly once across the sequence of pages

#### Scenario: A capped page is cut by the same order

- **WHEN** a caller requests a capped page of a read with many equal sort values
- **THEN** the items served are the first of the same total order the other kinds use

### Requirement: Paged results are addressed by page number, not by cursor

A paged read SHALL be navigated by the kind the caller requested: a counted page by page number, a continuable page by the continuation token of `continuation-tokens`, a capped page not at all. The token SHALL carry the last served values of the requested ordering, including a value computed per request from the same query arguments — a relevance score or a distance — together with the unique tie-breaker, so a read ordered by such a value is continuable without a stored value to seek on.

#### Scenario: Page requested by number

- **WHEN** a caller requests the third counted page of a read ordered by a per-request computed score
- **THEN** the system serves it from the page number alone, with no token

#### Scenario: Page requested by token

- **WHEN** a caller continues a continuable read ordered by a per-request computed score with the same query arguments
- **THEN** the system serves the items after the last served score and tie-breaker

## ADDED Requirements

### Requirement: The client chooses the kind of page per request

A list endpoint SHALL accept a `paging` query parameter naming `continuable`, `counted` or `capped`, with `continuable` applied when absent, and SHALL answer with exactly that kind. Every paged read SHALL answer every kind; no read SHALL declare a kind of its own or decline one. A counted request SHALL run an exact count over the same predicate as the page.

#### Scenario: No kind named

- **WHEN** a caller requests a list without `paging`
- **THEN** the system answers with a continuable page

#### Scenario: Counted search

- **WHEN** a caller requests a system-wide search with `paging=counted`
- **THEN** the system answers with a counted page carrying the exact total of matches

#### Scenario: Capped roster

- **WHEN** a caller requests a club's members with `paging=capped&cap=10`
- **THEN** the system answers with at most ten rows and whether more existed

### Requirement: Paging arguments must belong to the requested kind

Continuable paging SHALL accept `PageToken` and `size`; counted paging SHALL accept `page` and `size`; capped paging SHALL accept `cap`. A request supplying an argument of another kind SHALL be refused with HTTP 400, even when the value would be that kind's default. The check SHALL be made once at the edge, before the paging instruction is built.

#### Scenario: A page on a capped request

- **WHEN** a caller requests `paging=capped&cap=10&page=4`
- **THEN** the system refuses the request and serves no page

#### Scenario: A token on a counted request

- **WHEN** a caller requests `paging=counted&page=2` with a `PageToken`
- **THEN** the system refuses the request

#### Scenario: A page on the default kind

- **WHEN** a caller supplies `page=1` without naming a kind
- **THEN** the system refuses the request, because the default kind is continuable

### Requirement: A paging refusal is the shared invalid-value error

A paging refusal — a counted page below one, a depth beyond the bound, an invalid token, incompatible arguments — SHALL be answered at the edge as the shared 400 `request.invalid_value`, with a detail naming the bound or argument. No read SHALL declare a paging refusal of its own.

#### Scenario: Depth refused under the shared code

- **WHEN** a caller requests counted page 51 at size 20 of any list
- **THEN** the response is 400 `request.invalid_value`, and no list-specific depth code exists

#### Scenario: The same refusal on every list

- **WHEN** the same invalid paging arguments are sent to two different list endpoints
- **THEN** both answer with the same code and shape
```

### `openspec/changes/caller-selected-paging/specs/continuation-tokens/spec.md`

```markdown
# Spec Delta

## Purpose

Defines the continuation token a continuable read issues and accepts: an opaque, protected bookmark in the read's ordering over live data, valid for the same query, shared by HTTP and inter-module callers, and never expiring by age.

## ADDED Requirements

### Requirement: A continuable read is navigated by an opaque token

A continuable read SHALL take an optional `PageToken` and a size, and SHALL answer with `NextPageToken`, `Size` and the items. A request carrying no token SHALL start the read. A response carrying no next token SHALL mean the read has no further page; that absence SHALL be the only end signal, with no separate boolean and no page number. The caller SHALL pass the token back unchanged and SHALL NOT interpret it.

#### Scenario: First request

- **WHEN** a caller requests a continuable page without a token
- **THEN** the system serves the first page and, if more matches, a `NextPageToken`

#### Scenario: Continuation

- **WHEN** a caller resubmits the same read with the `NextPageToken` of the previous response as its `PageToken`
- **THEN** the system serves the items that follow the last item of the previous page

#### Scenario: End of the read

- **WHEN** a caller receives a page without a `NextPageToken`
- **THEN** no further page exists, regardless of how many items the page carries

#### Scenario: One contract for both callers

- **WHEN** a module dispatches a continuable query through the bus and a client calls the same read over HTTP
- **THEN** both receive and return the same token form, and neither receives a URL or a page number as continuation

### Requirement: A token is a bookmark in the ordering over live data

A token SHALL identify the position of the last served item in the read's requested ordering, including the unique tie-breaker. Continuation SHALL select the items ordered after that position at the time of the request. The system SHALL hold no snapshot, traversal session or server-side state for a token.

#### Scenario: An earlier item is deleted

- **WHEN** a caller holds a token after an identifier-ordered page ending at identifier 30, and identifier 10 is erased before continuation
- **THEN** continuation starts after identifier 30 and no item is skipped

#### Scenario: Continuation beyond the offset depth

- **WHEN** a caller continues a read past 1000 items by token
- **THEN** the system serves the page, because no offset is involved

### Requirement: A token continues the same query only

A token SHALL be valid only with the same result-defining arguments as the read that issued it: filters, identifier sets, search term, ordering and any location used for filtering or sorting. The page size MAY change between continuations within the shared bounds. A continuation whose arguments differ SHALL be refused as an invalid argument, answered as HTTP 400 at the edge, and SHALL NOT be served as a new read.

#### Scenario: Same criteria

- **WHEN** a caller continues with the token and the same term, filters and ordering
- **THEN** the system serves the next page

#### Scenario: Changed criteria

- **WHEN** a caller continues with a token issued for one term and supplies a different term
- **THEN** the system refuses the request with the shared invalid-value error and serves no page

#### Scenario: Changed size

- **WHEN** a caller continues with a token and a different size within the bounds
- **THEN** the system serves the next page at the new size

### Requirement: A token is protected against reading and tampering

The system SHALL encrypt and integrity-protect the payload behind a token before issuing it, and SHALL verify it on return. A token that was modified, was issued for another purpose, or cannot be read with the available keys SHALL be refused as an invalid argument. Protection SHALL remain valid across restarts and between instances that issue and consume the same tokens.

#### Scenario: Round trip

- **WHEN** a caller returns a token exactly as issued
- **THEN** the system recovers the position and serves the next page

#### Scenario: Tampered token

- **WHEN** a caller returns a token whose content was altered
- **THEN** the system refuses the request with the shared invalid-value error and serves no page

#### Scenario: Token issued after a restart

- **WHEN** the service restarts between issuing a token and its return
- **THEN** the token is still readable and continuation succeeds

### Requirement: A token value is validated structurally at the boundary

A supplied token SHALL be non-blank and URL-safe; a blank or malformed value SHALL be refused as an invalid argument before any read runs. Structural validity SHALL NOT imply that the protected content is valid for the request. Absence SHALL be expressed by omitting the token, never by an empty value.

#### Scenario: Blank token

- **WHEN** a caller supplies an empty or whitespace token
- **THEN** the system refuses the request with the shared invalid-value error

#### Scenario: Well-formed but invalid content

- **WHEN** a caller supplies a URL-safe string that is not a token the system issued
- **THEN** the structural check passes and the protected-content check refuses the request

### Requirement: A token does not expire by age

A token SHALL remain usable regardless of the time elapsed since it was issued. The system SHALL apply no lifetime, expiry timestamp or age limit to a token. Every continuation SHALL read live data at the time of the request.

#### Scenario: An old token

- **WHEN** a caller continues with a token issued long before
- **THEN** the system serves the next page from current data, provided the token passes the integrity and same-query checks

#### Scenario: Age is not a refusal reason

- **WHEN** a continuation is refused
- **THEN** the reason is integrity, compatibility or a changed query, never elapsed time
```

### `openspec/changes/caller-selected-paging/specs/paged-traversal/spec.md`

```markdown
# Spec Delta

## Purpose

Defines how a caller that needs every matching item of a paged query consumes it: one shared traversal over the query bus that follows continuation tokens page by page, yields items incrementally with the query's own typed failure, and reads live data without a snapshot or checkpoint.

## ADDED Requirements

### Requirement: Every item of a paged query is consumed through one shared traversal

The system SHALL offer one shared traversal, `QueryAllAsync`, that repeatedly dispatches a given query with continuable paging through the query bus and exposes the items as an asynchronous sequence. The caller SHALL supply how to build the query for a given token; the traversal SHALL start without a token, follow each `NextPageToken` and stop when none is returned. No module SHALL implement a paging loop of its own, and no query SHALL gain a whole-list or streaming form.

#### Scenario: Multi-page consumption

- **WHEN** a consumer traverses a query matching three pages of items
- **THEN** it receives every item of all three pages in order, and the query is dispatched three times

#### Scenario: Empty read

- **WHEN** a consumer traverses a query matching nothing
- **THEN** the sequence ends with no item and one dispatch

#### Scenario: Items arrive incrementally

- **WHEN** a consumer processes the first item
- **THEN** later pages have not yet been fetched, and the full result is never held in memory

#### Scenario: Query arguments are preserved

- **WHEN** the traversal dispatches a later page
- **THEN** the filters, identifier set, term and ordering of the original query are unchanged, and only the token differs

### Requirement: A failed fetch ends the traversal with the query's own error

When a dispatch returns an expected failure, the traversal SHALL yield that error once, as the query's own error type, and end. It SHALL NOT yield a wrapped or renamed error, SHALL NOT continue to a further page, and SHALL NOT present the items yielded so far as a complete result. Cancellation and unexpected exceptions SHALL follow the platform's asynchronous-stream behavior rather than becoming query errors or completion.

#### Scenario: A later page fails

- **WHEN** the second of three pages fails with a storage error
- **THEN** the consumer receives the first page's items, then that storage error once, then nothing, and no third dispatch occurs

#### Scenario: Cancellation

- **WHEN** the consumer cancels during traversal
- **THEN** the pending dispatch observes the cancellation and no further page is requested

#### Scenario: Early disposal

- **WHEN** the consumer stops enumerating before the end
- **THEN** no further page is requested

### Requirement: Traversal reads live data without a snapshot

Each page SHALL be read from the data current at the time of that fetch. The traversal SHALL NOT freeze an inventory, record a starting boundary or hold a session. An item already yielded MAY change before the traversal ends.

#### Scenario: An earlier item is deleted mid-traversal

- **WHEN** an item behind the bookmark is erased between two fetches
- **THEN** the next fetch starts after the bookmark and the next surviving item is not skipped

#### Scenario: A matching row is inserted ahead of the bookmark

- **WHEN** a row matching the criteria is inserted ahead of the current position during traversal
- **THEN** a later fetch may return it

#### Scenario: A matching row is inserted behind the bookmark

- **WHEN** a row matching the criteria is inserted behind the current position during traversal
- **THEN** this traversal does not return it, and a fresh traversal does

#### Scenario: A row outside the criteria never appears

- **WHEN** an identifier-set query is traversed and a row with an identifier outside the set is inserted
- **THEN** the traversal does not return it

### Requirement: A changed ordering value may omit or repeat an item

When the read is ordered by a value that can change, an item whose value moves behind the bookmark during traversal MAY be omitted, and one whose value moves ahead MAY be returned again. The traversal SHALL keep the caller's requested ordering and SHALL NOT substitute another. A consumer that needs neither effect SHALL request an ordering by an immutable identifier.

#### Scenario: Renamed behind the bookmark

- **WHEN** a handle-ordered traversal has passed M and an unread account's handle changes from Zoe to Amy
- **THEN** that account may be omitted from this traversal, and this is not a defect

#### Scenario: Renamed ahead of the bookmark

- **WHEN** an account already returned as Ada is renamed to Zara during a handle-ordered traversal
- **THEN** it may be returned again, and this is not a defect

#### Scenario: Identifier ordering is stable

- **WHEN** a consumer traverses with identifier ordering and handles change meanwhile
- **THEN** no item is omitted or repeated because of the handle change

### Requirement: A failed traversal restarts; consumers tolerate repeats

After a failed traversal, a later attempt SHALL start again without a token. The traversal SHALL keep no processing checkpoint and SHALL NOT undo writes a consumer completed before the failure. A consumer that writes per item SHALL tolerate receiving an item again. The traversal SHALL NOT retry on its own.

#### Scenario: Restart after failure

- **WHEN** a synchronization pass fails on a later page and a new pass starts
- **THEN** the new pass begins at the first page, earlier completed writes remain, and items already processed are processed again without harm

#### Scenario: No automatic retry

- **WHEN** a fetch fails
- **THEN** the traversal does not re-dispatch the failed page on its own
```

### `openspec/changes/caller-selected-paging/design.md`

````markdown
# Design

Motivation: see `proposal.md` — Why. Behavior: see the delta specs under `specs/`, above all `paged-reads`, `continuation-tokens` and `paged-traversal`. Product decisions: `docs/drafts/optional-port-pagination.md`, decisions 1–25. This document settles only what the draft left to implementation research.

## Context

- **Paging today is per use case.** Six use cases carry `ResolveSize`, `EnsurePagingDepthWithinBound`, `DefaultSize`/`MaximumSize`/`MaximumDepth` constants and a `PagingDepthExceeded` error variant; the controller maps that variant to a per-read problem code (`club.members.paging_depth_exceeded`). Ports take `int page, int size` and return the module-specific `ClubRelationshipPageResult(MembershipList, HasMore)`; the mapper rebuilds `ContinuablePageView` from five loose arguments.
- **Two adapter styles exist.** The searches (`MartenSearchClubRelationshipsAdapter`, `MartenSearchClubsAdapter`) build raw SQL from a `(expression, ascending)[]` ordering chain, append the identifier as tie-breaker, use `NULLS LAST`, and page by `OFFSET @offset LIMIT @limit` with one extra row for `HasMore`. The other paged and unpaged reads use Marten LINQ (`OrderBy`, `Skip`, `Take(size + 1)`). Relevance and distance are computed per request from the term or origin and rounded, so ties are decided by the rest of the chain.
- **Queries already see `Core.Application`.** `Contracts.Api` queries reference `Core.Application.Models` for the page views, so a shared `Paging` and `Page<TItem>` in that project are reachable by every query, port and controller without a new reference. `Core.Application` already references Vogen.
- **The strong-type refusal channel exists.** `GlobalExceptionHandlingMiddleware` catches `Vogen.ValueObjectValidationException` and answers 400 `request.invalid_value`; strong types bind from route and query strings through Vogen's `TypeConverter` conversion.
- **The query bus is in-process.** `WolverineQueryBus.QueryAsync` calls `IMessageBus.InvokeAsync<TResult>`; the result crosses no serializer, and exceptions propagate to the caller.
- **The first whole-set consumer already exists.** `RefreshAccountReplicaListHandler` walks its own store in batches of 500 missing ids and resolves each batch through `ResolveAccountIdentityApplicationService.ResolveMapAsync`, which dispatches `GetAccountIdentityListQuery` once per batch. A batch of 500 is ten pages under the new bounds.
- **Deployment.** `k8s/backend/deployment.yaml` runs three replicas, and staging runs several instances. The runner uses Redis for the SignalR backplane when `ConnectionStrings:<redis>` is set (`RealtimeServiceExtensions`); Redis is being provisioned for staging outside this change and is absent in single-instance development. No Data Protection configuration exists; no EF Core `DbContext` exists although the packages are pinned.

## Goals / Non-Goals

**Goals:**

- One place that bounds paging (the `Paging` factories), one refusal shape (the existing middleware clause), one generic page from port to edge, and one traversal helper, so that no module or use case carries paging logic.
- Keyset continuation that works for every ordering the adapters already build, including per-request computed scores, mixed directions and `NULLS LAST` columns, without a snapshot or server-side session.
- Token protection and key sharing that work across the three replicas and across restarts, using the platform's documented providers only.

**Non-Goals:**

- A second query, handler or port method per page kind or for whole-set consumption.
- A durable traversal checkpoint, automatic retry, or any streaming protocol beyond `IAsyncEnumerable`.
- Lower-bound or approximate counts; a declared set of kinds per read.
- Paging `club-account-relationships` (bounded to 50 accounts, out of scope per the proposal) or the gallery.

## Decisions

### 1. Shapes and homes

| Thing | Shape | Home |
| --- | --- | --- |
| `Paging` | `abstract record` with static factories `Continuable(PageToken? pageToken, int? size)`, `Counted(int? page, int? size)`, `Capped(int? cap)`; private constructor so the factories are the only entry | `Core.Application/Models/Paging.cs` |
| `ContinuablePaging(PageToken? PageToken, int Size)`, `CountedPaging(int Page, int Size)`, `CappedPaging(int Cap)` | `sealed record : Paging`, same file; constructors internal so only the factories build them | `Core.Application/Models/Paging.cs` |
| `Page<TItem>` | `abstract class` holding `ItemList` and `abstract Page<TOut> Map<TOut>(Func<TItem, TOut>)` | `Core.Application/Models/Page.cs` |
| `ContinuablePage<TItem>(int Size, PageToken? NextPageToken, IReadOnlyList<TItem> ItemList)`, `CountedPage<TItem>(int Page, int Size, int TotalCount, …)`, `CappedPage<TItem>(int Cap, bool Truncated, …)` | `sealed class : Page<TItem>`, one-line `Map` override each | one file per concrete page, replacing the three `…PageView.cs` files |
| `PageToken` | Vogen `readonly partial struct` over `string`, conversions `SystemTextJson | NewtonsoftJson | TypeConverter`; `Validate` refuses blank and any character outside `A–Z a–z 0–9 - _` (base64url) | `Core.Application/Models/PageToken.cs` |

**Why an abstract class for `Page<TItem>` and not a Gorilla union:** the use case must map items without naming the kind; a union gives a `Match` over three arms in every use case, which is the switch decision 7 forbids. The concrete classes are plain records in behavior; System.Text.Json writes the runtime type's fields because the controller returns the page through `Ok(object)`, so no polymorphism attribute is needed.

**Why `int?` on the factories:** a controller passes the bound value through unchanged (`request.Size`), and the factory, not the controller, decides that `null` and `0` mean 20. The `?? 1` / `?? 0` sentinels in the controllers go with the constants.

### 2. Refusals throw `ValueObjectValidationException`

The `Paging` factories throw `Vogen.ValueObjectValidationException` for a counted page below one and for `page × size > 1000`, with a message naming the bound. The shared presentation switch (decision 3) throws the same for an argument belonging to another kind. The middleware's existing clause answers all of them as 400 `request.invalid_value`; its comment is widened from "while the request body is being deserialized" to "when a submitted value is refused, during binding or when the action builds a paging instruction".

**Alternatives:** an `Either`-returning factory — rejected by the draft (decision 2), it would put a `Match` and problem code in every list controller. `IValidatableObject` on the presentation request — the platform's model validation answers a `ValidationProblemDetails`, a second shape beside `request.invalid_value`, and does not bound a bus caller. A new exception type with its own middleware clause — a second clause for the same answer; the Vogen exception already means "a submitted value was refused".

### 3. One presentation base request, one switch

`PagingRequest` is a `public abstract class` in `Core.Presentation/Requests/` with `[FromQuery]` properties `Paging` (`PagingKind?`), `Page`, `Size`, `Cap` (`int?`) and `PageToken` (`PageToken?`). `PagingKind` is an enum beside it. Every list request (`GetClubMemberListRequest`, `SearchClubsRequest`, the pending-list, joined-club, account-search and the eight caller-scoped list requests) extends it; the `[Range]` annotations on `Page` and `Size` are deleted.

`PagingRequestExtensions.ToPaging(this PagingRequest)` in the same folder performs decision 21's check and calls the matching factory:

| Kind | Allowed | Refused if present |
| --- | --- | --- |
| absent or `continuable` | `PageToken`, `Size` | `Page`, `Cap` |
| `counted` | `Page`, `Size` | `PageToken`, `Cap` |
| `capped` | `Cap` | `Page`, `Size`, `PageToken` |

A controller builds its query with `request.ToPaging()` inline, as the draft's decision 6 requires, and no model binder or action filter is introduced.

**Alternative:** a custom `IModelBinder` for `Paging` — hides the switch in framework plumbing and makes the refusal a `ModelState` error with the other 400 shape.

### 4. Port, adapter and use case

- A paged port method takes `Paging paging` in place of `int page, int size` and returns `Either<…PortError, Page<TReadModel>>`. `ClubRelationshipPageResult` is deleted.
- The adapter is the only code that names a kind: it pattern-matches the `Paging` and runs the matching statement — seek predicate plus `LIMIT size + 1` for continuable, `OFFSET`/`LIMIT size` plus a `count(*)` over the same predicate for counted, `LIMIT cap + 1` for capped — and constructs the concrete page.
- A use case's payload holds `Page<TReadModel>` under a named property (`RelationshipPage`), the selector is `payload.RelationshipPage.Map(row => mapper.ToView(row, …))`, and the query's result type is `Either<…Error, Page<TView>>`. Mappers lose every paging argument. `SelectDisclosedInvitationList` / `SelectDisclosedRequestList` are deleted.
- The use case's error union loses `PagingDepthExceeded`; the controller's `Match` loses that arm and its problem code.

### 5. Keyset continuation, generated from the ordering chain

A continuation token carries the **position**: the values of every ordering criterion of the row last served, in chain order, ending with the tie-breaker identifier. The adapter that built the `ORDER BY` builds the seek predicate from the same chain, so the two cannot drift.

- **Raw-SQL adapters** (the three relationship lists, club search) get one shared statement helper in `Core.Infrastructure` that, given `(expression, ascending)[]` and the reversal flag, emits both the `ORDER BY` fragment they emit today and the expanded seek predicate `(c1 ▷ v1) OR (c1 ≡ v1 AND c2 ▷ v2) OR … OR (c1 ≡ v1 AND … AND id > vid)`, where `▷` is `>` or `<` by effective direction and `≡` is `IS NOT DISTINCT FROM`. A criterion whose bookmark value is `NULL` under `NULLS LAST` contributes no "greater" arm (nothing sorts after null) and matches `IS NULL` on the equality arm, which keeps a missing handle copy correct in both directions. Row-value comparison (`(a, id) > ($1, $2)`) is not used because the chains mix directions and `NULLS LAST` columns.
- **Marten LINQ adapters** (joined clubs, the eight caller-scoped lists, the two identifier-set fact queries) order by one instant or the identifier, ascending, with the identifier as tie-breaker, and apply the seek through Marten's documented `MatchesSql` escape hatch on the `data ->>` expression and `id`, because a `>` on a Vogen strong type does not translate (see memory: only `IsOneOf` translates). They are not reversible, so no expanded form is needed.
- **Computed criteria** (relevance, distance) are seekable because the same query arguments recompute them identically; the token stores the rounded value the row was ordered on. Decision 6 guarantees the arguments are the same.
- **Counted and capped kinds** do not touch the token path.

### 6. Token payload, purpose and same-query check

- **Payload:** System.Text.Json of `{ "q": "<fingerprint>", "p": [ …position values… ] }`, protected by `IDataProtector` and written as base64url. The position values are the primitives the adapter bound (`string`, `long`, `double`, `decimal`, `DateTimeOffset`); the adapter knows its chain's types and reads them back by index.
- **Fingerprint:** a canonical string of the result-defining arguments *as the adapter binds them* — the normalized term, sorted filter lists, sort key, direction, origin, identifier set, scope identifiers (club id, account id). The adapter composes it; the shared protector only compares. A token whose fingerprint differs is refused (decision 17); size is not part of it.
- **Purpose isolation:** `IDataProtectionProvider.CreateProtector("Feng.Backend.PageToken", <read name>)`, the read name being the port method's name. A token issued by one read is unreadable by another. No version number in the purpose (a hand-bumped marker is a rejected mechanism); a payload that no longer deserializes is an invalid token and the client starts a fresh read.
- **Shared service:** `IPageTokenProtector` (interface in `Core.Application/Services/`, implementation `DataProtectionPageTokenProtector` in `Core.Infrastructure`, registered as a scoped dependency) with `Protect(readName, fingerprint, position) → PageToken` and `Unprotect(readName, fingerprint, token) → position`. It is an infrastructure service in the building-block sense: a reusable technical capability behind the boundary, injected into adapters.

### 7. Validation boundary and how an invalid token reaches the client

| Check | Where | Outcome |
| --- | --- | --- |
| Blank or non-base64url | `PageToken.Validate` at binding or construction | `ValueObjectValidationException` → 400 `request.invalid_value` |
| Tampered, foreign purpose, undecodable, unavailable key | `IPageTokenProtector.Unprotect`, inside the adapter | throws `ValueObjectValidationException` ("PageToken was not issued by this read") → same clause |
| Different query arguments | `IPageTokenProtector.Unprotect` fingerprint comparison | same |
| Storage failure | adapter, as today | port error → use case error → 503 |

The protector throws rather than returning a port-error variant because a variant would have to be redeclared on every paged port, use case and controller — the duplication decision 2 removed. The exception travels through the use case railway and `InvokeAsync` to the middleware unchanged. A bus caller never sees it: `QueryAllAsync` forwards tokens as issued, so a throw there is a programming error, as an invalid strong type is.

### 8. `QueryAllAsync`

```csharp
public static async IAsyncEnumerable<Either<TError, TItem>> QueryAllAsync<TError, TItem>(
    this IQueryBus queryBus,
    Func<PageToken?, IQuery<Either<TError, Page<TItem>>>> buildQuery,
    [EnumeratorCancellation] CancellationToken cancellationToken)
````

in `Core.Application/QueryBusExtensions.cs` beside `IQueryBus`. It dispatches `buildQuery(null)`, yields each item as `Right`, reads `NextPageToken` from the `ContinuablePage` and loops until it is null; on `Left` it yields the error once and returns. A page that is not a `ContinuablePage` is a programming error (the factory passed a non-continuable `Paging`) and throws `InvalidOperationException`. Cancellation flows into each dispatch; early disposal simply ends the iterator. No buffering, no prefetch, no retry.

**Alternative:** a channel-backed prefetching reader — not required by any consumer and adds a background loop to reason about.

### 9. Identifier-set fact queries and their consumers

- `GetAccountIdentityListQuery` and `GetSportListByIdSetQuery` gain `Paging` and return `Page<TView>` ordered by identifier. Their adapters keep `IsOneOf` for the set and add the `MatchesSql` seek on `id`.
- `ResolveAccountIdentityApplicationService.ResolveMapAsync` (clubs and tournaments) and the sports lookups behind `EnsureAllSportsExistGuard` always consume `QueryAllAsync` with `Paging.Continuable(token, idSet.Count)`. When the set fits one page that is one dispatch and no token; when it does not, the helper follows tokens. One code path replaces the draft's "establish that the set fits" precondition, which no caller could establish without restating the bound.
- `RefreshAccountReplicaListHandler` keeps its local batch walk over its own store (that is not bus paging) and resolves each batch through the service above, which now traverses. `BatchSize` stays 500.

### 10. The eight caller-scoped lists

Each gains `Paging` on query, port and request, returns `Page<TView>`, and fixes its order in the adapter: the relationship instant (`InvitedAt`, `RequestedAt`, `BlockedAt`, offer instant) ascending with the identifier as tie-breaker, except `GetClubRelationshipListQuery` which orders by club identifier. They use the Marten LINQ seek of decision 5. Their controllers' requests extend `PagingRequest`.

### 11. Data Protection keys

`AddDataProtection().SetApplicationName("Feng.Backend")` in a new runner extension beside `AddRealtimeNotifications`. When the Redis connection string the backplane already uses is configured, keys persist with `PersistKeysToStackExchangeRedis` (package `Microsoft.AspNetCore.DataProtection.StackExchangeRedis`, the documented Redis provider) under the key `Feng.Backend:DataProtection-Keys`; otherwise the default local key ring is used and a startup line states that tokens do not survive a restart and are instance-bound, mirroring the backplane's single-instance message.

**Redis is a precondition, not a deliverable.** Every environment with more than one instance (the three-replica deployment, staging) must supply the Redis connection string before this change ships there. Provisioning Redis is handled outside this change (agreed 2026-10-19); this change adds no Redis manifest, chart or configuration beyond reading the connection string it already reads. The deployment documentation states the requirement.

**Alternatives:** `PersistKeysToDbContext` — needs an EF Core `DbContext` and table creation the Marten codebase does not have, for one table; a file-system key ring on a shared volume — the deployment has none; Marten — no Data Protection provider exists for it.

### 12. OpenAPI

An endpoint returning `Page<TView>` declares `[ProducesResponseType(typeof(Page<TView>), 200)]`. One `IOpenApiOperationTransformer` registered in the runner's OpenAPI setup rewrites that 200 schema to a `oneOf` of the three concrete page schemas, since the generator cannot infer them from the abstract type. The `paging`, `page`, `size`, `cap` and `PageToken` parameters come from `PagingRequest` and need no per-endpoint attributes.

### 13. Renames and rules

`ContinuablePageView`/`CountedPageView`/`CappedPageView` → `ContinuablePage`/`CountedPage`/`CappedPage`. `naming-terms.md` gains `Page<TItem>`, `<Kind>Page<TItem>`, `Paging`, `<Kind>Paging`, `PagingKind`, `PagingRequest`, `PageToken`, `QueryAllAsync`, and the rule that a page is not a View. The glossary gains page, paging and continuation token.

## Risks / Trade-offs

- [Wolverine `InvokeAsync` may apply failure policies to a thrown `ValueObjectValidationException`] → verify in the integration suite that the exception reaches the middleware once and no retry runs; if a policy intervenes, exclude the exception type from it in `MessagingServiceExtensions`.
- [Expanded seek predicates on long chains can defeat the planner] → the relationship lists are scoped to one club and the search chains are at most four criteria; measure with `EXPLAIN` on the seeded database and add the compound index only where measured.
- [Exact `count(*)` on a system-wide search is a full evaluation per request] → accepted by decision 5; measure on seeded data and record the figures in the module docs so the client decides with the cost known.
- [A `NULLS LAST` criterion with many null rows] → the null arm continues by identifier only, which is correct but linear across the null tail; the only such column is the handle copy, whose null tail is the set of accounts without a copy.
- [Token size] → an encrypted JSON of a few values is well under a hundred bytes before base64url; identifier sets are not in the payload, only their fingerprint.
- [Fingerprint false mismatch when a caller normalizes differently] → the fingerprint is built from the values the adapter binds after its own normalization, so two requests the store would treat alike fingerprint alike.
- [Redis absent in an environment with more than one instance] → keys differ per instance and a token issued by one instance is refused by another; the startup log names the condition, the deployment documentation lists the Redis connection string as required for more than one instance, and staging must not test continuable paging across instances before its Redis is live.
- [Breaking continuable clients] → the proposal flags it; there is no compatibility shim, by decision 15.

## Migration Plan

1. No database change: no event, projection or table changes; raw-SQL adapters only change statements.
2. Build order: core types and helper → core presentation → each module's ports, adapters, use cases, requests, controllers → runner (Data Protection, OpenAPI transformer) → docs, rules, skills.
3. Deploy the runner with the Redis connection string present in every multi-instance environment (staging included, once its Redis is provisioned outside this change); the Data Protection key is created on first use.
4. Clients switch continuable navigation to tokens and stop sending `page` without `paging=counted`.
5. Rollback is a redeploy of the previous image; tokens issued by the new build are simply refused by the old one, which does not read them.
```

## Appendix D: Call-Site Examples

How Feng's adapters use the extensions today. They show what a store adapter must supply: the chain, the tie-breaker, the read name and the argument fingerprint.

### Marten LINQ read (`MartenGetClubReadModelAdapter`)

```csharp
	public async Task<Either<GetClubReadModelPortError, Page<ClubReadModel>>> GetPageByOwnershipOfferRecipientAsync(
		AccountId recipient,
		Paging paging,
		CancellationToken cancellationToken)
	{
		try
		{
			await using var session = store.QuerySession();

			return await session.Query<ClubReadModel>()
				.Where(club => club.OpenOwnershipOffer != null && club.OpenOwnershipOffer.Recipient == recipient)
				.ToPageAsync(
					paging,
					ordered => ordered.OrderBy(club => club.OpenOwnershipOffer!.OfferedAt).ThenBy(club => club.Id),
					("d.open_ownership_offer_offered_at", club => club.OpenOwnershipOffer!.OfferedAt.Value),
					("d.id", club => club.Id.Value),
					pageTokenProtector,
					nameof(GetPageByOwnershipOfferRecipientAsync),
					recipient.Value,
					cancellationToken);
		}
```

### Raw Npgsql search with a computed chain (`MartenSearchClubsAdapter`)

```csharp
			// The fingerprint is every result-defining argument as bound here; the caller's standing is named by who the
			// caller is, so a membership gained between pages widens the next page rather than refusing its token.
			var fingerprint = string.Join('|',
				hasTerm ? term! : string.Empty,
				string.Join(',', sportReferenceList.Select(reference => reference.ToKey()).Order(StringComparer.Ordinal)),
				countryCode?.Value ?? string.Empty,
				hasLocation ? FormattableString.Invariant($"{latitude!.Value.Value:R},{longitude!.Value.Value:R}") : string.Empty,
				radiusInMetres?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
				sortKey.ToString(),
				sortOrder.ToString(),
				callerAccountId?.Value ?? string.Empty,
				isModerator.ToString());

			return await connection.ReadPageAsync(
				transaction,
				BuildInnerStatement(hasTerm, hasLocation, sportReferenceList, countryCode, radiusInMetres),
				parameters => BindParameters(
					parameters, term, hasTerm, sportReferenceList, countryCode, latitude, longitude, hasLocation,
					radiusInMetres, callerAccountId, memberClubIdList, managedClubIdList, isModerator),
				Chain(sortKey, hasTerm, hasLocation),
				reversed: sortOrder == ClubSearchSortOrder.Descending,
				"id",
				ReadPositionValue,
				paging,
				reader => ReadResult(reader, hasLocation),
				pageTokenProtector,
				nameof(SearchAsync),
				fingerprint,
				cancellationToken);
		}
		catch (NpgsqlException ex) when (ex is not PostgresException)
```

```csharp
	private static (string Expression, bool Ascending)[] Chain(ClubSearchSortKey sortKey, bool hasTerm, bool hasLocation) =>
		sortKey switch
		{
			ClubSearchSortKey.Relevance =>
			[
				.. hasTerm ? [("relevance", true)] : Array.Empty<(string, bool)>(),
				.. hasLocation ? [("distance", false)] : Array.Empty<(string, bool)>(),
				("member_count", true)
			],
			ClubSearchSortKey.Distance when hasLocation => [("distance", true)],
			ClubSearchSortKey.MemberCount => [("member_count", true)],
			_ => []
		};

	// Reads a bookmark value back into the type the column was bound as.
	private static object? ReadPositionValue(string column, JsonElement value) =>
		column switch
		{
			"relevance" => value.GetDecimal(),
			"distance" => value.GetDouble(),
			"member_count" => value.GetInt64(),
			_ => value.GetString()
		};

```
