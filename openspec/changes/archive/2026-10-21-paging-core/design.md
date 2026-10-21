# Design

## Context

See `proposal.md` for motivation. The rationale for the architecture (layered store seam, two chain flavours, injected `Pager`, package-built scope, package split) is already recorded with alternatives in `docs/briefs/extensibility-design.md`, decisions 1 to 12; this document does not repeat those arguments. It states how this change realises them in `Zooper.Ant`, `Zooper.Ant.Tests` and `Zooper.Ant.Example`.

Constraints that shape the approach:

- `net10.0` only; xUnit, Shouldly, NSubstitute for the unit tests; flat repository layout with project folders at the root and `Zooper.Ant.sln` (brief, "Decisions").
- Core may depend on `Microsoft.AspNetCore.DataProtection.Abstractions` and `Microsoft.Extensions.Options` and nothing else. The second is required by `AddAnt` (`IServiceCollection`, `IOptions<T>`, `ValidateOnStart`), which the Data Protection abstractions do not carry on `net10.0`; it is part of every ASP.NET Core and generic host already. The LINQ builder uses only `System.Linq.Expressions`.
- The working Feng code (brief, Appendix A) is the starting point for `Paging`, the `Page<T>` kinds, `PageToken` validation and the protector; the kind switch and the in-memory extension are rewritten onto the new seam.
- The specs in this change are host-neutral: every refusal is an exception; no HTTP type appears in core.

## Goals / Non-Goals

**Goals:**

- One implementation of the kind switch (`Pager`) over the floor interface, with the in-memory source riding the same LINQ builder a database provider will receive, so the in-memory tests exercise the real seek expression.
- Every public type a later adapter or host package needs already present and stable: `IPageSource<T>`, `PagePosition`, `Order<T>`, `IQueryExecutor`, `PageScope`, `IPageTokenProtector`, the exceptions.
- `services.AddAnt()` plus one `ToPageAsync` call is a complete in-memory consumer.

**Non-Goals:**

- The SQL-text builder, `ISqlDialect`, and every database adapter (later changes).
- `PagingRequest`, OpenAPI, `ProblemDetails`, Minimal API binding (AspNetCore change).
- Backwards paging; only the payload version field that leaves room for it.
- Proving EF Core or Marten translation of the seek expression. That proof belongs to the adapter changes; this change makes the expression shape as translatable as possible and documents the risk.

## Decisions

### 1. Projects and namespaces

Three projects at the repository root: `Zooper.Ant` (the only package), `Zooper.Ant.Tests` (unit tests) and `Zooper.Ant.Example` (runnable web example), neither of the last two packed. `Directory.Build.props` carries the shared metadata and `<Version>`; `Properties/AssemblyInfo.cs` holds `InternalsVisibleTo("Zooper.Ant.Tests")`.

Namespaces inside `Zooper.Ant`:

| Namespace | Types |
| --- | --- |
| `Zooper.Ant` | `Paging`, `ContinuablePaging`, `CountedPaging`, `CappedPaging`, `PagingLimits`, `Page<T>`, `ContinuablePage<T>`, `CountedPage<T>`, `CappedPage<T>`, `PageToken`, `PageScope`, `Pager`, `PagingException`, `InvalidPageTokenException` |
| `Zooper.Ant.Sources` | `IPageSource<T>`, `PagePosition`, `Order<T>`, `OrderKey<T>`, `IQueryExecutor`, `QueryablePageSource<T>`, `InMemoryQueryExecutor`, `QueryableExtensions.ToPageAsync` for in-memory |
| `Zooper.Ant.Tokens` | `IPageTokenProtector`, `DataProtectionPageTokenProtector` |
| `Zooper.Ant.DependencyInjection` | `AntOptions`, `ServiceCollectionExtensions.AddAnt` |

Alternative considered: one flat namespace. Rejected because adapter packages will add their own `ToPageAsync` overloads and the consumer should see only `Zooper.Ant` plus one adapter namespace.

### 2. `Paging` and `PagingLimits`

Port Feng's `Paging` (brief, Appendix A) with the constants replaced by a `PagingLimits` argument: `Paging.Continuable(PageToken? token, int? size, PagingLimits limits)`, `Paging.Counted(int? page, int? size, PagingLimits limits)`, `Paging.Capped(int? cap, PagingLimits limits)`. Overloads without `limits` use `PagingLimits.Default` (20, 50, 1000). Refusals throw `PagingException` with the message naming the bound. `PagingLimits` is a positional record with a `Validate()` method used both by `AddAnt` (startup) and by the factories (defensive, cheap). `ToEmptyPage<T>()` is kept; the traversal and hosts use it.

### 3. The floor: `IPageSource<T>` and `PagePosition`

As recorded in decision 6 of the design record, with one addition made here: a read-only `string OrderSignature { get; }` member. `Pager` folds it into the token binding so that a changed ordering refuses the token (spec `continuation-tokens`, "Changed ordering") for every source, not only the LINQ one. A native-cursor source returns an empty string. The design record is amended accordingly.

```csharp
public interface IPageSource<TItem>
{
    string OrderSignature { get; }
    Task<int> CountAsync(CancellationToken ct);
    Task<IReadOnlyList<TItem>> ReadAsync(int offset, int limit, CancellationToken ct);
    Task<IReadOnlyList<TItem>> ReadAfterAsync(PagePosition position, int limit, CancellationToken ct);
    PagePosition PositionOf(TItem item);
    PagePosition ParsePosition(IReadOnlyList<JsonElement> payload);
}
public sealed record PagePosition(IReadOnlyList<object?> Values);
```

A source that cannot count or offset throws `NotSupportedException` from `CountAsync` or from `ReadAsync` with `offset > 0`; `Pager` catches exactly that type on those two calls and rethrows `PagingException("The counted kind is not available on this read.")`. Alternative considered: a capabilities flag on the interface. Rejected: one more member for every source to answer, where an exception from the two methods that cannot work says the same thing.

### 4. `Pager.ReadAsync`: the kind switch, written once

```
ReadAsync<T>(IPageSource<T> source, Paging paging, PageScope scope, CancellationToken ct)
  counted  : total = CountAsync; items = ReadAsync((page-1)*size, size)            → CountedPage
  capped   : items = ReadAsync(0, cap+1); truncated = items.Count > cap             → CappedPage(items[..cap])
  continuable:
      position = token is null ? null : source.ParsePosition(protector.Unprotect(scope.Bind(source.OrderSignature), token))
      items    = position is null ? ReadAsync(0, size+1) : ReadAfterAsync(position, size+1)
      next     = items.Count > size ? protector.Protect(boundScope, source.PositionOf(items[size-1])) : null
      → ContinuablePage(items[..size], next)
```

`Pager` is a sealed class with constructor `(IPageTokenProtector protector, IOptions<AntOptions> options)` and exposes `Limits` so hosts build `Paging` from the configured bounds. `TraverseAsync` lives on it as an instance method for the single-entry-point rule, although it uses no state: `async IAsyncEnumerable<T> TraverseAsync<T>(Func<PageToken?, CancellationToken, Task<Page<T>>> readPage, [EnumeratorCancellation] CancellationToken ct)`, which loops `readPage(token)` and yields items; a non-continuable page throws `InvalidOperationException`; any other exception propagates unchanged.

### 5. `PageScope` and the fingerprint

`PageScope.For(string readName, params object?[] values)` stores the name and values. `Bind(orderSignature)` returns a `BoundScope(ReadName, Fingerprint)` where the fingerprint is `Base64Url(SHA256(UTF8(json)))` and `json` is the `System.Text.Json` serialisation of `{ "a": values, "o": orderSignature }` with default `JsonSerializerOptions` (invariant culture by construction; `DateTime` round-trips ISO 8601). Hashing keeps the token size independent of argument size. The fingerprint is computed per read call, which is one small JSON serialisation and one hash; it is not cached.

Values that serialise to the same JSON bind alike. A consumer who passes an object with unstable serialisation (a dictionary with insertion-order keys) gets a surprising refusal; the README says to pass scalars and records.

### 6. Token payload and the Data Protection protector

Port Feng's `DataProtectionPageTokenProtector` with these changes: the interface takes `BoundScope`; the payload is `{ "v": 1, "q": fingerprint, "p": [values] }` with `v` reserved for the direction flag of a future backwards-paging change; the purpose chain is `CreateProtector(options.PurposeRoot, scope.ReadName)` with default root `"Zooper.Ant.PageToken"`; every failure (cryptographic, format, JSON, missing fields, fingerprint mismatch, `v` above 1) throws `InvalidPageTokenException` with one fixed message, so the error does not leak which check failed. `Base64Url` from `System.Buffers.Text` does the encoding.

### 7. `PageToken`

`readonly record struct PageToken` wrapping a `string Value`. `PageToken.Parse(string)` validates non-blank and base64url alphabet (`A–Z a–z 0–9 - _`, no padding) and throws `PagingException`; `TryParse` returns false. It implements `IParsable<PageToken>` (Minimal API binding later), carries a `JsonConverter` that writes and reads the raw string, and a `TypeConverter` for MVC query binding later. Absence is `null` on `PageToken?`, never an empty token.

### 8. `Order<T>` and the LINQ builder

`Order<T>.By(Expression<Func<T, TKey>> key)` and `ThenBy`, `ByDescending`, `ThenByDescending`, each with an optional `nullsFirst: true`; `Reverse()` returns a chain with every direction and nulls placement flipped. An `OrderKey<T>` holds the lambda, the key `Type`, the direction, the nulls placement, and a lazily compiled getter. `OrderSignature` is the keys' `ToString()` plus direction and nulls flags joined, which changes when the ordering changes and is stable across processes.

The builder, `SeekExpressionBuilder`, produces two things from a chain and a position:

- **Ordering.** For each key in order: when the key type is nullable, first `OrderBy(x => key(x) == null ? 1 : 0)` (or `0 : 1` for nulls-first), then `OrderBy(key)` or `OrderByDescending(key)`, using `Queryable.OrderBy`/`ThenBy` via `Expression.Call`. The null-rank key is the portable way to force null placement in LINQ; it translates in LINQ-to-objects and in EF Core, and it is what both EF Core issue threads and the Marten docs recommend.
- **Seek predicate.** The expanded form from the spec: `OR` over `i` of (`AND` over `j < i` of `eq(key_j, v_j)`) `AND beyond(key_i, v_i)`. `beyond` for a non-null bookmark is `>` (ascending) or `<` (descending), plus `OR key == null` when the key is nullable and nulls sort last on that direction's far side. For a null bookmark under nulls-last there is no `beyond` arm, and `eq` is `key == null`. Comparison uses `Expression.GreaterThan`/`LessThan` when the key type defines the operator (numeric types, `DateTime`, `DateTimeOffset`, `TimeSpan`, enums via their underlying type), otherwise `key.CompareTo(v) > 0` through the type's `IComparable<TKey>` (`string`, `Guid`). Both forms translate in EF Core and run in LINQ-to-objects.
- **Parameterised constants.** Bookmark values are not emitted as `Expression.Constant`. Each is wrapped as `Expression.Property(Expression.Constant(new Box<TKey>(v)), "Value")`, which LINQ providers recognise as a parameter rather than a literal, so EF Core produces one cached plan per chain shape instead of one per bookmark.

`PositionOf(item)` reads each getter; `DateTime` and `DateTimeOffset` values are truncated to microseconds (spec `keyset-ordering`, timestamp truncation) and `DateTime` is normalised to UTC. `ParsePosition(payload)` checks the count equals the key count, then `payload[i].Deserialize(keyType)`; a count mismatch or deserialisation failure throws `InvalidPageTokenException`.

Alternative considered: `Comparer<T>.Default.Compare` for every type. Rejected because EF Core does not translate it.

### 9. `QueryablePageSource<T>` and `IQueryExecutor`

`QueryablePageSource<T>(IQueryable<T> query, Order<T> order, IQueryExecutor executor)` implements the floor: `CountAsync` is `executor.CountAsync(query)`; `ReadAsync` is `executor.ToListAsync(Ordered(query).Skip(offset).Take(limit))`; `ReadAfterAsync` is the same over `Ordered(query.Where(seek))`. The in-memory executor runs `query.ToList()` and `query.Count()` and returns completed tasks. The constructor takes an optional `timestampPrecision` (default one microsecond) that `PositionOf` truncates to: the truncation rule exists because a store keeps less precision than memory, so the in-memory entry point passes one tick, since there the stored precision is the in-memory precision and truncating would place an item before its own bookmark and serve it twice. `QueryableExtensions.ToPageAsync(this IQueryable<T>, Paging, Order<T>, PageScope, Pager, CancellationToken)` in `Zooper.Ant.Sources` is the in-memory entry point; adapter packages will add overloads bound to their executors in their own namespaces, and an `IEnumerable<T>` overload calls `AsQueryable()`.

### 10. Registration

`services.AddAnt(Action<AntOptions>? configure = null)`: `AddOptions<AntOptions>().Configure(configure).Validate(o => o.Limits.IsValid, "...").ValidateOnStart()`; `TryAddSingleton<IPageTokenProtector, DataProtectionPageTokenProtector>()` so a host's earlier registration wins; `AddSingleton<Pager>()`. `AntOptions { PagingLimits Limits = PagingLimits.Default; string PurposeRoot = "Zooper.Ant.PageToken"; }`. `AddAnt` does not call `AddDataProtection()`; the host owns the key ring, and `IDataProtectionProvider` missing at first use is a clear resolution error naming the fix in the README.

### 11. In-memory conformance tests and the example

No test package ships. Adapter authors test their own adapters (decided by Daniel, 2026-10-20). Inside `Zooper.Ant.Tests`, under `Contract/`, the in-memory source runs one conformance suite: a deterministic `ContractRows.Standard` set (ties on `CreatedAt`, null scores, duplicate names, a sub-microsecond pair), order descriptors mapped to `Order<ContractRow>`, one case per scenario of spec `page-sources` "The in-memory source is the reference implementation", read through a test-only `PlainTextPageTokenProtector` so token cases can read and alter tokens. A second fixture whose seek ignores the tie-breaker proves the full-traversal case catches a seek that disagrees with the order.

`Zooper.Ant.Example` is a minimal API over in-memory players and a cursor-based activity log, showing every feature and driven by `Zooper.Ant.Example.http`. README code only showcases usage; it is not compiled or tested.

### 12. Unit tests

`Zooper.Ant.Tests` ports Appendix B (`PagingTests`, `PageTests`, `PageTokenTests`, `DataProtectionPageTokenProtectorTests`) onto the new types, and adds: `PageScopeTests` (culture independence, order folding, size exclusion), `SeekExpressionBuilderTests` (predicate shape per direction and nulls case, evaluated in memory), `PagerTests` against an NSubstitute `IPageSource<T>` (kind switch call pattern, `NotSupportedException` mapping, token issued from the size-th item), `TraverseAsyncTests` (Appendix C scenarios), and the in-memory conformance suite. Conventions: `_sut`, Arrange/Act/Assert, Shouldly.

## Risks / Trade-offs

- [The seek expression may not translate in a given LINQ provider (Feng saw this with Marten and Vogen types)] → The builder uses only operators and `CompareTo`, which EF Core translates for every primitive it maps; the null-rank ordering key is a known-translatable idiom. Each adapter change proves translation with its own integration tests; a provider that fails may implement the floor directly.
- [Nullable `DateTime` round trip through JSON loses `Kind`] → `PositionOf` normalises to UTC before truncation; `ParsePosition` reads back and re-marks UTC. Tested in the in-memory conformance suite's timestamp case.
- [`Expression.Constant` of a boxed value is still inlined by some providers] → Documented as a plan-cache cost only; correctness is unaffected. Checked in the EF Core change with `ToQueryString()`.
- [Null-rank key doubles the ORDER BY length on nullable keys and may defeat an index] → Only emitted for nullable keys; the README recommends non-null keys for the leading position and `EXPLAIN` on the adapter.
- [The fingerprint depends on `System.Text.Json` default serialisation staying stable across package versions] → Pin the payload and fingerprint JSON options explicitly in one place (`CanonicalJson`) rather than relying on defaults; note in CHANGELOG that changing them invalidates tokens in flight.
- [Multi-instance hosts need a shared Data Protection key ring] → Documented, not provisioned; the README points at the platform's own guidance.
- [A consumer forgets a result-defining value in `PageScope.For`] → Cannot be detected by the package. Mitigated by the README's rule and the example's scope: every filter argument of the read goes in the scope.

## Migration Plan

Greenfield. Publish `Zooper.Ant` at the version in `Directory.Build.props`. Nothing in Feng changes in this change; Feng adopts the packages in the adapter changes.

## Open Questions

- Whether `OrderKey<T>` should accept a custom `IComparer` for key types without operators or `IComparable<T>`. Deferrable: no first-release adapter needs it, and adding it later is additive.
