# Design

## Context

See `proposal.md` for motivation. The direction decisions (both hosts, the five fixed wire names, one package, fixed problem `type` URNs) and the delegated implementation choices (separate registration calls, concrete request class, explicit limits, tests in `Zooper.Ant.Tests`) are recorded with rationale in `docs/briefs/ant-aspnetcore-companion.md`, "Decisions" 1 to 7. This document states how this change realises them.

Constraints that shape the approach:

- `net10.0` only; flat repository layout; one test project (`Zooper.Ant.Tests`) and one example project (`Zooper.Ant.Example`) per repository, as in Bee.
- The core API does not change. The binding calls the existing three-argument factories `Paging.Continuable(token, size, limits)`, `Paging.Counted(page, size, limits)` and `Paging.Capped(cap, limits)`; `PageToken` already binds through `IParsable<PageToken>` (Minimal APIs) and its `TypeConverter` (MVC); `InvalidPageTokenException` already derives from `PagingException`.
- `Microsoft.AspNetCore.App` is a `FrameworkReference`, inherited by every project that references the package. That is why the package is separate from the core (`docs/briefs/ant-aspnetcore-companion.md`, "Why a Companion Package and Not the Core").
- The working Feng sources (brief, Appendix A and B) are the starting point for `ToPaging`, the transformer and the tests; the example's `PagingQuery.cs` and `PagingExceptionHandler.cs` are the starting point for the Minimal API shape and the handler.

## Goals / Non-Goals

**Goals:**

- A Minimal API endpoint is `[AsParameters] PagingRequest paging` plus `paging.ToPaging(pager.Limits)`; a controller action is `[FromQuery] PagingRequest paging` plus the same call. Nothing else to write or register for the binding.
- Two one-line registrations, each independent: `services.AddAntExceptionHandler()` and `options.AddAntPageResponses()` inside `AddOpenApi`.
- The example is the showcase: it takes the package, keeps no copy of the three pieces, and its tour and README use the five wire names.

**Non-Goals:**

- Renaming parameters, a custom `IModelBinder`, a `BindAsync` on the request type, a `Paging` bound directly as an endpoint parameter.
- An `errorCode` field or configurable URNs on the problem response.
- GraphQL or gRPC bindings; a separate OpenAPI package.
- Any change in Feng (listed in the brief as downstream work).

## Decisions

### 1. One concrete request class, bound by attributes in both hosts

`PagingRequest` is a public, non-sealed, non-abstract class with five `init` properties, each carrying `[FromQuery(Name = "...")]` with the wire name, plus a `PagingKind` (`Continuable`, `Counted`, `Capped`; an enum in this plan, a parsable struct as built, see decision 8).

- Minimal APIs bind it with `[AsParameters]`: every property is bound as if it were a parameter, `IFromQueryMetadata` on the property fixes the documented name, `PageToken?` binds through `IParsable`, and `PagingKind?` binds through the framework's case-insensitive enum parsing. An unparsable value is a 400 before the endpoint runs.
- MVC binds it with `[FromQuery]` on the action parameter: the complex-type binder reads each property from the query by its attribute name. An unparsable value is a model-state error, which `[ApiController]` answers with 400 before the action runs.
- Non-sealed so a host may still derive from it and add its own filter properties, which is what Feng's controllers do today; the example instead declares its filter as a second `[AsParameters]` parameter, which is the recommended shape because it keeps the paging arguments out of the host's types.

The attributes are needed for the API explorer, not for binding: query binding is case-insensitive in both hosts, but without the attribute the Minimal API explorer would document `PageToken` instead of `pageToken`.

The OpenAPI document lists `continuable`, `counted`, `capped` as the allowed values of `paging` (planned through a `JsonStringEnumConverter` on an enum; built as a schema transformer on the parsable struct, see decision 8).

*Alternatives:* an abstract base class (brief, "Types"), rejected because `[AsParameters]` must construct the type; a `BindAsync` method (decision 14 wording), rejected because the attributes already bind in both hosts and `BindAsync` would bypass the API explorer; a `Paging` model binder (brief, "Alternative considered"), rejected because `Paging` lives in the core and cannot carry host binding.

### 2. `ToPaging(PagingLimits)` refuses foreign arguments and delegates everything else

`PagingRequestExtensions.ToPaging(this PagingRequest request, PagingLimits limits)` is Feng's switch with the limits threaded through: it refuses the first foreign argument it finds with `PagingException("Paging argument 'x' does not belong to <kind> paging.")`, then calls the matching three-argument factory. It never defaults, clamps or depth-checks; the core's factories do that under the host's limits, which is the bug the Feng copy has (it calls the two-argument factories and ignores `AntOptions.Limits`).

The limits are explicit, not resolved from DI: the endpoint already injects `Pager` to run the read, and `pager.Limits` is one expression. A `ToPaging()` overload without limits is not offered, because it would silently reintroduce the Feng bug.

*Alternative:* resolve limits inside binding from `HttpContext.RequestServices`. Rejected: needs `BindAsync`, hides the dependency, and gains nothing at a call site that holds the `Pager` anyway.

### 3. The handler is the example's, promoted, with the `type` URNs added

`PagingExceptionHandler : IExceptionHandler` takes `IProblemDetailsService`, returns `false` for anything that is not a `PagingException`, otherwise sets 400 and writes a problem with `Type` (`urn:zooper:ant:invalid-page-token` for `InvalidPageTokenException`, else `urn:zooper:ant:invalid-paging`), `Title` (`Invalid page token` / `Invalid paging`) and `Detail` (the exception message). The URNs are public constants on the handler so a client library or a test can reference them.

`services.AddAntExceptionHandler()` calls `AddProblemDetails()` (idempotent, `TryAdd` inside) and `AddExceptionHandler<PagingExceptionHandler>()`. The host still calls `app.UseExceptionHandler()`, as with every `IExceptionHandler`; the README says so.

### 4. The transformer is Feng's, as an `IOpenApiOperationTransformer`

`PageResponseTransformer` is Feng's `DescribePageResponseAsync` as a class: find the 200 response type in `context.Description.SupportedResponseTypes`; if it is `Page<>` of some item type, build the three concrete schemas through `context.GetOrCreateSchemaAsync` (so they are shared component schemas, not inlined per operation) and set every media type's schema to a `oneOf` of them; otherwise return. `AddAntPageResponses(this OpenApiOptions options)` is `options.AddOperationTransformer<PageResponseTransformer>()`.

The transformer sees the declared return type, so a Minimal API handler returning `Task<Page<T>>` and a controller action returning `ActionResult<Page<T>>` are both rewritten; a handler returning `ContinuablePage<T>` is left alone, per the spec.

### 5. Project and package shape

```text
Zooper.Ant.AspNetCore/
  Zooper.Ant.AspNetCore.csproj      Microsoft.NET.Sdk; FrameworkReference Microsoft.AspNetCore.App;
                                    ProjectReference Zooper.Ant; PackageReference Microsoft.AspNetCore.OpenApi 10.0.x;
                                    GenerateDocumentationFile, IsPackable (same props as the core)
  PagingKind.cs
  PagingRequest.cs
  PagingRequestExtensions.cs
  PagingExceptionHandler.cs
  PageResponseTransformer.cs
  DependencyInjection/ServiceCollectionExtensions.cs    AddAntExceptionHandler
  DependencyInjection/OpenApiOptionsExtensions.cs       AddAntPageResponses
```

Namespace `Zooper.Ant.AspNetCore` for everything (no `Requests` sub-namespace: six types do not need one, and a host then needs one `using`). Version, authors, icon and README come from `Directory.Build.props` like the core; the package carries the same `README.md`.

### 6. Tests in `Zooper.Ant.Tests`, two host fixtures

`Zooper.Ant.Tests` gains a project reference to `Zooper.Ant.AspNetCore` (and so the framework reference) and `Microsoft.AspNetCore.TestHost`, the lightest way to run a real pipeline in-process. Two tiny hosts are built inside the tests with `WebApplication.CreateBuilder` plus `UseTestServer()`:

- a Minimal API host with one list endpoint using `[AsParameters] PagingRequest`, `AddAnt`, `AddAntExceptionHandler`, `AddOpenApi(o => o.AddAntPageResponses())`;
- an MVC host with one `[ApiController]` controller from the test assembly (`AddControllers().AddApplicationPart(...)`) using `[FromQuery] PagingRequest`.

Pure unit tests (Feng's `PagingRequestExtensionsTests`, extended with the custom-limits case) need no host. The handler is tested through the Minimal API host (400, problem body, `type` URN) and directly (`TryHandleAsync` returns `false` for a non-paging exception). The transformer is tested by requesting `/openapi/v1.json` from the Minimal API host and reading the operation's 200 schema.

### 7. The example takes the package

`Zooper.Ant.Example` references the package, deletes `PagingQuery.cs` and `PagingExceptionHandler.cs`, replaces `AddExceptionHandler<PagingExceptionHandler>()` with `AddAntExceptionHandler()`, adds `AddOpenApi(o => o.AddAntPageResponses())` and `MapOpenApi()` so the tour can show the document, binds `[AsParameters] PagingRequest` on `/players` and `/activity`, and renames every `kind=` and `token=` in `Api.cs`, `Tour.cs` and `README.md` to `paging=` and `pageToken=`. The README gains a "Hosting in ASP.NET Core" section with the three one-liners and the `UseExceptionHandler` note, and its hand-written handler sample is replaced by the package call. The doc example in `Paging.cs` that shows `request.Kind` and `request.Token` is reworded to the package's `ToPaging`.

### 8. Corrections found during implementation

Three assumptions above did not hold on ASP.NET Core 10; the behaviour in the specs is unchanged.

- **`PagingKind` is an `IParsable` struct, not an enum.** Minimal APIs parse an enum query value case-sensitively against the member names, so `paging=counted` was a 400 against a member named `Counted`. `PagingKind` is a `readonly record struct` with three static instances, a case-insensitive `TryParse` (Minimal APIs) and a `TypeConverter` (MVC); `default(PagingKind)` is refused by `ToPaging`. The generator describes the struct by its JSON contract, so `AddAntPageResponses()` also adds a schema transformer that rewrites it to `string` with `enum` `continuable`, `counted`, `capped`.
- **`PagingRequest` carries `[Bind(Prefix = "")]`.** MVC uses the parameter name as a model prefix whenever a query key equals it, so `[FromQuery] PagingRequest paging` beside `?paging=counted` looked up `paging.page` and bound nothing. The empty prefix fixes it whatever the parameter is called; Minimal APIs ignore the attribute.
- **The transformer registers components itself.** `GetOrCreateSchemaAsync` returns inline schemas, so the three page schemas are added once to the document under the generator's `x-schema-id` and the `oneOf` holds `$ref`s, which is what "Item schema is shared" requires.

Build note: `Microsoft.AspNetCore.OpenApi`'s source generator flows through a `ProjectReference` without the package targets that enable its interceptors, so `Zooper.Ant.Tests` and `Zooper.Ant.Example` set `InterceptorsNamespaces`. NuGet consumers get neither (`exclude="Build,Analyzers"`).

## Risks / Trade-offs

- [MVC without `[ApiController]` runs the action with a null `Paging` or `PageToken` after a failed parse, so an unknown kind would silently bind as continuable] → documented: the package expects `[ApiController]` for controllers, which every API template applies; the spec scenario is tested on an `[ApiController]`.
- [`Microsoft.AspNetCore.OpenApi` reaches hosts that never generate OpenAPI] → accepted by decision 4; inert unless `AddOpenApi` is called; a later split keeps the registration call name.
- [`GetOrCreateSchemaAsync` and the `oneOf` schema API are from `Microsoft.AspNetCore.OpenApi` 10 and `Microsoft.OpenApi` 2] → pin the 10.0.x package; Feng already runs this code on the same versions.
- [The test project now carries the ASP.NET Core framework reference] → harmless for the existing core tests; it is the cost of one test project per repository.
- [`AddAntExceptionHandler` calling `AddProblemDetails` could surprise a host that customises problem details] → `AddProblemDetails` is `TryAdd`-based, so a host's earlier `AddProblemDetails(configure)` wins; a later one still applies its options because options are additive.

## Migration Plan

Nothing published changes. The example's query string renames `kind` to `paging` and `token` to `pageToken`; the example has no external clients. Feng's follow-up is listed in the brief ("What Feng Removes Afterwards") and happens on the Feng branch after the package ships.
