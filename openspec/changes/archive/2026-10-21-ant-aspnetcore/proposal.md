# Proposal

## Why

`Zooper.Ant` stops before the HTTP edge, so every ASP.NET Core host writes the same three pieces again: the query-string binding that turns `?paging=&page=&size=&cap=&pageToken=` into a `Paging`, the mapping of a `PagingException` to a 400 problem response, and the OpenAPI description of a `Page<T>` response that the generator cannot infer from the abstract type. Two copies exist today (Feng's and `Zooper.Ant.Example`'s) and already disagree on parameter names and on whether configured limits are honoured. One companion package makes a host write none of them, and settles the wire contract before anything is published.

## What Changes

- New package `Zooper.Ant.AspNetCore`, the host-integration companion of `Zooper.Ant` (the `FluentValidation.AspNetCore` pattern): `FrameworkReference` on `Microsoft.AspNetCore.App`, `PackageReference` on `Microsoft.AspNetCore.OpenApi`, project reference on `Zooper.Ant`. The core API is unchanged.
- Query binding: one request type with the five fixed query parameters `paging` (`continuable` | `counted` | `capped`, omitted means continuable), `pageToken`, `page`, `size`, `cap`, bound in both MVC controllers and Minimal APIs, and `ToPaging(limits)` which refuses an argument of another kind and otherwise defers defaulting, clamping and depth to the core's factories under the host's configured limits. No renaming option: a host that wants other names writes its own binding and calls the `Paging` factories.
- Exception handler: an opt-in `IExceptionHandler` that answers every `PagingException` with a 400 RFC 9457 problem response whose `type` is `urn:zooper:ant:invalid-paging`, or `urn:zooper:ant:invalid-page-token` for `InvalidPageTokenException`. Registered only by an explicit call, so a host with its own global error handling takes the binding and the OpenAPI description without a competing handler.
- OpenAPI: an operation transformer that rewrites a response declared as `Page<T>` into a `oneOf` of the three concrete page types. Registered inside `AddOpenApi(options => …)`.
- Registration: two independent calls, `services.AddAntExceptionHandler()` and `openApiOptions.AddAntPageResponses()`. No umbrella call; the binding needs no registration.
- `Zooper.Ant.Example` takes the package and deletes its own `PagingQuery` and `PagingExceptionHandler`; its endpoints and the README switch to the five wire names. **BREAKING** for the example's query string only (`kind` becomes `paging`, `token` becomes `pageToken`); no published package changes.
- Tests join the existing `Zooper.Ant.Tests` project (one test project per repository, as in Bee). No `Zooper.Ant.AspNetCore.Tests` project.

## Capabilities

### New Capabilities

- `http-paging-binding`: how an HTTP host receives the paging arguments of a list request: the five fixed query parameters, the default kind, structural token validation before the action runs, the refusal of an argument that belongs to another kind, and that the host's configured limits bound every bound request. Same behaviour in MVC controllers and Minimal APIs.
- `http-paging-errors`: how an HTTP host answers a paging refusal: a 400 problem response with a stable `type` identifier per refusal class, a title and the refusal's message as detail; opt-in, so a host's own error handling is never displaced.
- `openapi-page-description`: how a paged response is described to API consumers: a `Page<T>` response is documented as one of the three concrete page shapes, and every other response is left as the generator describes it.

### Modified Capabilities

None. `openspec/specs` is empty: the `paging-core` capabilities (`paged-reads`, `continuation-tokens`, `keyset-ordering`, `page-sources`, `paged-traversal`) still live inside that change and are not modified here; this package consumes them.

## Impact

- New project `Zooper.Ant.AspNetCore` (packable, same version as the core through `Directory.Build.props`), added to `Zooper.Ant.sln`.
- Modified: `Zooper.Ant.Example` (references the package, loses two files, renames query parameters), `Zooper.Ant.Tests` (gains the package's tests and a framework reference through it), `README.md` (host section and wire names), the `Paging.cs` doc example that still shows `request.Kind` and `request.Token`.
- Dependencies of the new package: the `Microsoft.AspNetCore.App` shared framework and `Microsoft.AspNetCore.OpenApi`. A host that generates no OpenAPI carries the latter unused; it is inert unless `AddOpenApi` is called. The core keeps its two dependencies.
- Design source of truth: `docs/briefs/ant-aspnetcore-companion.md`, whose "Decisions" section (1 to 7) overrides its earlier sections; it supersedes the "configurable query names", `AddAntAspNetCore` umbrella and "configurable" problem type clauses of decisions 10 and 12 in `docs/briefs/extensibility-design.md`.
- Downstream: Feng deletes its `PagingKind`, `PagingRequest`, `PagingRequestExtensions`, their tests and `DescribePageResponseAsync` once the package ships, keeps its own exception middleware, and passes `pager.Limits` to `ToPaging`. Nothing in Feng changes within this change.
