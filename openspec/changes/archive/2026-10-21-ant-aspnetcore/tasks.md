# Tasks

## 1. Package scaffold

- [x] 1.1 Create `Zooper.Ant.AspNetCore/Zooper.Ant.AspNetCore.csproj` (`Microsoft.NET.Sdk`, `GenerateDocumentationFile`, `IsPackable`, `FrameworkReference Microsoft.AspNetCore.App`, `ProjectReference Zooper.Ant`, `PackageReference Microsoft.AspNetCore.OpenApi` 10.0.x) and add it to `Zooper.Ant.sln`; verify `dotnet build` succeeds and `dotnet pack Zooper.Ant.AspNetCore` lists `Zooper.Ant` and `Microsoft.AspNetCore.OpenApi` as its only package dependencies.
- [x] 1.2 Add to `Zooper.Ant.Tests` a project reference to `Zooper.Ant.AspNetCore` and a `PackageReference Microsoft.AspNetCore.TestHost` 10.0.x; verify `dotnet test` still runs every existing test green.

## 2. Query binding (spec `http-paging-binding`)

- [x] 2.1 Add `PagingKind` (`Continuable = 1, Counted = 2, Capped = 3`, `[JsonConverter(typeof(JsonStringEnumConverter<PagingKind>))]`) and `PagingRequest` (public, non-sealed class; `init` properties `Paging`, `Page`, `Size`, `Cap`, `PageToken`, each with `[FromQuery(Name = "...")]` carrying the wire name) in namespace `Zooper.Ant.AspNetCore`, with full XML docs; verify the project builds with no documentation warnings.
- [x] 2.2 Add `PagingRequestExtensions.ToPaging(this PagingRequest, PagingLimits)` from the brief's Appendix A, threading `limits` into `Paging.Continuable`, `Paging.Counted` and `Paging.Capped`, refusing the first foreign argument with `Paging argument '<name>' does not belong to <kind> paging.`; verify the ported `PagingRequestExtensionsTests` pass (four kind tests, the eight foreign-argument theory cases, no depth-bound test) plus a new case where `size` above a custom `MaximumSize` comes back clamped to that custom value and one where an omitted `size` takes a custom `DefaultSize`.
- [x] 2.3 Add a Minimal API test host in `Zooper.Ant.Tests` (`WebApplication.CreateBuilder` + `UseTestServer`, `AddAnt`, one `/items` endpoint taking `[AsParameters] PagingRequest` and a second `[AsParameters]` filter type, returning the bound `Paging` as JSON); verify tests cover: each kind binds, omitted `paging` is continuable, `paging=Counted` binds case-insensitively, `paging=infinite` is 400 before the endpoint, `pageToken=abc==` is 400 before the endpoint, and a structurally valid token binds.
- [x] 2.4 Add an MVC test host (`AddControllers().AddApplicationPart(test assembly)`, one `[ApiController]` controller with `[FromQuery] PagingRequest paging` beside a filter parameter); verify tests cover: each kind binds, omitted `paging` is continuable, `paging=infinite` and `pageToken=abc==` are 400 before the action.
- [x] 2.5 Verify through the Minimal API host's `/openapi/v1.json` (with `AddOpenApi` and `MapOpenApi`) that the list operation documents exactly the five optional query parameters `paging`, `pageToken`, `page`, `size`, `cap`, and that `paging` lists the enum values `continuable`, `counted`, `capped`.

## 3. Exception handler (spec `http-paging-errors`)

- [x] 3.1 Add `PagingExceptionHandler : IExceptionHandler` (public constants `InvalidPagingType = "urn:zooper:ant:invalid-paging"` and `InvalidPageTokenType = "urn:zooper:ant:invalid-page-token"`; 400; `Type`, `Title`, `Detail` as in the design; `false` for any non-`PagingException`) and `ServiceCollectionExtensions.AddAntExceptionHandler()` (`AddProblemDetails()` + `AddExceptionHandler<PagingExceptionHandler>()`); verify a direct unit test shows `TryHandleAsync` returns `false` for an `InvalidOperationException` and does not touch the response.
- [x] 3.2 Register `AddAntExceptionHandler()` and `UseExceptionHandler()` in the Minimal API test host; verify tests cover: a foreign-argument request returns 400 with `type` `urn:zooper:ant:invalid-paging`, title `Invalid paging` and a detail naming the argument; a counted page beyond a custom depth returns 400 with the core's depth message; a tampered token returns 400 with `type` `urn:zooper:ant:invalid-page-token` and title `Invalid page token`; and a host built without `AddAntExceptionHandler()` answers the same foreign-argument request with 500.

## 4. OpenAPI transformer (spec `openapi-page-description`)

- [x] 4.1 Add `PageResponseTransformer : IOpenApiOperationTransformer` from the brief's Appendix A (200 response typed `Page<>` becomes `oneOf` of `ContinuablePage<T>`, `CountedPage<T>`, `CappedPage<T>` through `GetOrCreateSchemaAsync`; everything else untouched) and `OpenApiOptionsExtensions.AddAntPageResponses()`; verify the project builds with full XML docs.
- [x] 4.2 Add to the Minimal API test host endpoints returning `Task<Page<Item>>`, `ContinuablePage<Item>`, `Item`, and a paged endpoint with a declared 400 problem response; verify tests on `/openapi/v1.json` show the `Page<Item>` 200 schema is a `oneOf` of three component references, two `Page<Item>` endpoints reference the same components, and the other three responses are unchanged.

## 5. Example and documentation

- [x] 5.1 Reference `Zooper.Ant.AspNetCore` from `Zooper.Ant.Example`, delete `PagingQuery.cs` and `PagingExceptionHandler.cs`, bind `[AsParameters] PagingRequest` on `/players` and `/activity` with `paging.ToPaging(pager.Limits)`, replace `AddExceptionHandler<PagingExceptionHandler>()` with `AddAntExceptionHandler()`, add `AddOpenApi(o => o.AddAntPageResponses())` and `MapOpenApi()`, and rename every `kind=`/`token=` in `Api.cs` and `Tour.cs` to `paging=`/`pageToken=`; verify `dotnet run --project Zooper.Ant.Example` in tour mode completes and in HTTP mode `GET /players?paging=capped&cap=3&page=1` returns 400 with the `invalid-paging` type and `GET /openapi/v1.json` shows the `oneOf`.
- [x] 5.2 Update `README.md`: replace the hand-written `PagingExceptionHandler` sample with the package's three one-liners plus the `UseExceptionHandler()` note in a "Hosting in ASP.NET Core" section, document the five query parameters as the wire contract and that a host wanting other names writes its own binding, update the registration table, and rename `kind`/`token` wherever they appear; verify no `kind=` or `token=` remains in `README.md` or the example and the package dependency line still reads correctly.
- [x] 5.3 Reword the `Paging.cs` XML doc example that shows `request.Kind`/`request.Token` to point at `PagingRequest.ToPaging` from the companion package, and add a `CHANGELOG.md` entry for `Zooper.Ant.AspNetCore`; verify `dotnet build` emits no documentation warnings.

## 6. Integration check

- [x] 6.1 Run `dotnet test` and `dotnet pack` for the whole solution; verify every test is green, `Zooper.Ant` still lists only `Microsoft.AspNetCore.DataProtection.Abstractions` and `Microsoft.Extensions.Options`, and `Zooper.Ant.AspNetCore` packs with the README and icon.

## Workflow follow-up

- Archive the change after review; `paging-core` should be archived first so both sets of specs land in `openspec/specs`.
- On the Feng branch afterwards: delete the five files listed in `docs/briefs/ant-aspnetcore-companion.md` "What Feng Removes Afterwards" and pass `pager.Limits` to `ToPaging`.
