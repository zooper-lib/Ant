# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-21

### Added

- **`Zooper.Ant`**, the store-neutral paging core, with a runnable `Zooper.Ant.Example` web project showing every feature.
  - `Paging` with three kinds, `ContinuablePaging`, `CountedPaging` and `CappedPaging`, bounded by `PagingLimits` (default size 20, maximum 50, counted depth 1000). Sizes are clamped; a counted page below one or beyond the depth bound throws `PagingException`.
  - `Page<T>` results, `ContinuablePage<T>`, `CountedPage<T>` and `CappedPage<T>`, with a kind-preserving `Map`.
  - `PageToken`, a base64url struct with `Parse`/`TryParse`, `IParsable<PageToken>`, a JSON converter and a type converter.
  - `PageScope.For(readName, values)`, binding a token to its read, its result-defining arguments and the ordering.
  - `IPageTokenProtector` and `DataProtectionPageTokenProtector`: encrypted, integrity-protected tokens under the purpose `(PurposeRoot, readName)` that never expire.
  - `PagingException` and `InvalidPageTokenException`.
  - `IPageSource<T>` and `PagePosition`, the floor any store implements; `Pager.ReadAsync` serves every kind from it and refuses the counted kind with a `PagingException` when the source cannot count or offset.
  - `Pager.TraverseAsync`, following tokens to the end as an `IAsyncEnumerable<T>`.
  - `Order<T>` and `OrderKey<T>`: a typed key chain with mixed directions, nulls last by default, `Reverse()` and a stable `OrderSignature`.
  - `IQueryExecutor`, `QueryablePageSource<T>` and `InMemoryQueryExecutor`: order and keyset seek built as LINQ expressions with parameterised bookmarks; `ToPageAsync` for in-memory `IQueryable<T>` and `IEnumerable<T>`.
  - `services.AddAnt(...)` with `AntOptions` (limits validated on start, purpose root).
- **`Zooper.Ant.AspNetCore`**, the ASP.NET Core companion package, versioned with the core.
  - `PagingRequest` and `PagingKind`: the five fixed query parameters `paging`, `pageToken`, `page`, `size`, `cap`, bound with `[AsParameters]` in Minimal APIs and `[FromQuery]` in `[ApiController]` controllers. `paging` is case-insensitive and defaults to continuable; an unknown kind or malformed token is a 400 before the endpoint runs.
  - `PagingRequest.ToPaging(limits)`: refuses an argument of another kind with a `PagingException`, otherwise builds the `Paging` under the host's limits.
  - `PagingExceptionHandler` and `services.AddAntExceptionHandler()`: every `PagingException` as a 400 problem response with `type` `urn:zooper:ant:invalid-paging` or `urn:zooper:ant:invalid-page-token`. Opt-in.
  - `PageResponseTransformer` and `openApiOptions.AddAntPageResponses()`: a `Page<T>` response documented as `oneOf` the three page schemas, shared as components; `paging` documented with its three values.
- **Breaking (example only):** `Zooper.Ant.Example` takes the package; its query parameters `kind` and `token` are now `paging` and `pageToken`.
- **Note:** the token payload and fingerprint JSON options are pinned in one place. Changing them, or `AntOptions.PurposeRoot`, invalidates every token in flight.
