# Zooper.Ant.AspNetCore — Companion Package Draft

Status: draft for the developer to implement in the `Zooper.Ant` repository. Written 2026-10-21 from the Feng backend branch `feature/caller-selected-paging`, where the backend already runs on `Zooper.Ant` and still carries the three pieces this draft moves out.

## Purpose

`Zooper.Ant` answers a paged read: page kinds, limits, keyset continuation and protected tokens. It stops before the HTTP edge. Every ASP.NET Core host that uses it then writes the same three things again:

1. the query-string binding that turns `?paging=&page=&size=&cap=&pageToken=` into a `Paging`,
2. the mapping of a `PagingException` to a `400` problem response,
3. the OpenAPI description of a `Page<T>` response, which the generator cannot infer from the abstract type.

Feng has one copy, the Ant example project has a second copy of the first two, and the copies already disagree (Feng binds the token as `pageToken`, the example as `token`; Feng ignores the configured limits). This draft moves the three pieces into one companion package so a host writes none of them.

## Scope

In scope: a new package `Zooper.Ant.AspNetCore` in the Ant repository, its tests, and the list of what Feng deletes once it ships.

Out of scope: any change to the `Zooper.Ant` core API, and Feng's own error-code convention (`errorCode: request.invalid_value`), which stays in Feng's middleware.

## Why a Companion Package and Not the Core

The binding needs `[FromQuery]`, the handler needs `IExceptionHandler` and `IProblemDetailsService`, the transformer needs `Microsoft.AspNetCore.OpenApi`. The first two live in the `Microsoft.AspNetCore.App` shared framework. A `FrameworkReference` on a library is inherited by every project that references it, directly or transitively, with no opt-out.

In Feng, `Zooper.Ant` is referenced from `Feng.Backend.Core.Application`, because `Page<T>`, `Paging` and `PageToken` sit on queries and ports. Those types flow into the projects that have no framework reference today and must not get one:

- the four `Modules.*.Contracts.Api` projects, where the paged queries are declared,
- the four `Modules.*.Application` projects, with the use cases and ports,
- `Feng.Backend.Core.Application` itself.

The infrastructure and presentation projects already carry the framework reference, so they can take the companion package. The split keeps the application layer free of the delivery mechanism, which is the hexagonal rule the backend is built on.

The named pattern is the host-integration companion: `FluentValidation.AspNetCore` next to `FluentValidation`, `Hangfire.AspNetCore` next to `Hangfire.Core`.

## Package Layout

```text
Zooper.Ant.AspNetCore/
  Zooper.Ant.AspNetCore.csproj      Microsoft.NET.Sdk, FrameworkReference Microsoft.AspNetCore.App,
                                    ProjectReference Zooper.Ant, PackageReference Microsoft.AspNetCore.OpenApi
  Requests/PagingKind.cs
  Requests/PagingRequest.cs
  Requests/PagingRequestExtensions.cs
  PagingExceptionHandler.cs
  OpenApi/PageResponseTransformer.cs
  DependencyInjection/ServiceCollectionExtensions.cs
Zooper.Ant.AspNetCore.Tests/
```

`Microsoft.AspNetCore.OpenApi` is a NuGet package, not part of the shared framework, so it is a `PackageReference`. If the OpenAPI transformer should not force that package on every host, it becomes a third package, `Zooper.Ant.OpenApi`. Decide before publishing; one package is the simpler default.

The example project then references `Zooper.Ant.AspNetCore` and deletes its own `PagingQuery` and `PagingExceptionHandler`.

## Component 1: Query Binding

### Contract

Five query parameters, shared by every list endpoint of every host:

| Parameter | Type | Kinds that accept it |
| --- | --- | --- |
| `paging` | `continuable`, `counted` or `capped`; omitted means `continuable` | all |
| `size` | int | continuable, counted |
| `pageToken` | `PageToken` | continuable |
| `page` | int, one-based | counted |
| `cap` | int | capped |

An argument that belongs to another kind is refused with a `PagingException` naming the argument and the kind. The parameter names are the package's wire contract: changing one later breaks every client.

Open decision: `pageToken` (Feng, documented in Feng's Bruno collection) or `token` (Ant example). This draft assumes `pageToken`; it says what it is and cannot be confused with an auth token.

### Types

```csharp
namespace Zooper.Ant.AspNetCore.Requests;

public enum PagingKind
{
	Continuable = 1,
	Counted = 2,
	Capped = 3
}

/// <summary>The paging arguments every list request extends. Bound from the query string.</summary>
public abstract class PagingRequest
{
	[FromQuery(Name = "paging")] public PagingKind? Paging { get; init; }
	[FromQuery(Name = "page")] public int? Page { get; init; }
	[FromQuery(Name = "size")] public int? Size { get; init; }
	[FromQuery(Name = "cap")] public int? Cap { get; init; }
	[FromQuery(Name = "pageToken")] public PageToken? PageToken { get; init; }
}

public static class PagingRequestExtensions
{
	/// <summary>Builds the instruction the request asks for, bounded by <paramref name="limits"/>.</summary>
	/// <exception cref="PagingException">An argument of another kind is present, or the factories refuse the values.</exception>
	public static Paging ToPaging(this PagingRequest request, PagingLimits limits);
}
```

Rules the implementation keeps from the Feng copy (Appendix A):

- No kind named means continuable.
- `continuable` refuses `page` and `cap`; `counted` refuses `pageToken` and `cap`; `capped` refuses `page`, `size` and `pageToken`.
- The refusal names the first foreign argument found: `Paging argument 'size' does not belong to capped paging.`
- Everything else (defaulting, clamping, the depth bound) is the core's job: the method calls `Paging.Continuable(token, size, limits)` and so on, and never re-checks a bound.

What changes against the Feng copy: `ToPaging` takes `PagingLimits` and passes them to the factories. The Feng copy calls the two-argument factories, so a host that configures `AntOptions.Limits` still binds with the defaults. A controller passes `pager.Limits` from the injected `Pager`.

`PageToken` binds from the query string through its `TypeConverter`; nothing is needed for it. A token that is not base64url fails model binding with a `400` before the action runs, the same as today.

### Alternative considered: a model binder for `Paging`

A registered `IModelBinder` could bind `Paging` directly as an action parameter and read the limits from DI, so a controller never calls `ToPaging`. It is the nicer API but changes how every host declares a list request (`[FromQuery] Paging paging` beside the request type instead of a base class). Not for the first version. The base class plus `ToPaging` is what both existing copies do and what Feng's controllers already call.

## Component 2: Exception Handler

The example's `PagingExceptionHandler` promoted into the package, unchanged in behaviour:

```csharp
namespace Zooper.Ant.AspNetCore;

public sealed class PagingExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
	{
		if (exception is not PagingException paging)
			return false;

		httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

		return await problemDetails.TryWriteAsync(new ProblemDetailsContext
		{
			HttpContext = httpContext,
			Exception = exception,
			ProblemDetails =
			{
				Status = StatusCodes.Status400BadRequest,
				Title = paging is InvalidPageTokenException ? "Invalid page token" : "Invalid paging",
				Detail = paging.Message
			}
		});
	}
}
```

Registration is a host's choice, never automatic: a host with its own global exception middleware, like Feng, must be able to take the binding and the OpenAPI transformer without a second handler competing for the same exception. Hence a separate call:

```csharp
builder.Services.AddExceptionHandler<PagingExceptionHandler>();   // the host's own line, or:
builder.Services.AddAntExceptionHandler();                         // the package's one-liner
```

The handler sets no machine-readable error code. A host that needs one (Feng uses `errorCode: request.invalid_value`) keeps its own handler. If the package should offer a code, make it an option on `AddAntExceptionHandler` rather than a fixed string.

## Component 3: OpenAPI Transformer

Feng's `DescribePageResponseAsync` (Appendix A) moved as an `IOpenApiOperationTransformer`. It rewrites a `200` response declared as `Page<T>` into a `oneOf` of `ContinuablePage<T>`, `CountedPage<T>` and `CappedPage<T>`, which the generator cannot infer from the abstract type.

```csharp
builder.Services.AddOpenApi(options => options.AddAntPageResponses());
```

The paging query parameters need no transformer: they come from the `PagingRequest` base class and the API explorer already lists them.

## Registration Surface

| Call | Registers |
| --- | --- |
| `services.AddAnt(...)` | unchanged, from the core |
| `services.AddAntExceptionHandler()` | `PagingExceptionHandler` through `AddExceptionHandler<T>()` |
| `openApiOptions.AddAntPageResponses()` | the `Page<T>` operation transformer |

No `AddAntAspNetCore()` umbrella: the two things a host may want are independent, and Feng wants only one of them.

## Tests

Move Feng's `PagingRequestExtensionsTests` (Appendix B) into `Zooper.Ant.AspNetCore.Tests` and extend it:

- every kind builds its `Paging`, and omitted `paging` builds continuable (four tests, as today),
- every foreign-argument combination throws `PagingException` (the eight theory cases, as today),
- `ToPaging` passes the given `PagingLimits` to the factories: a `size` above a custom `MaximumSize` comes back clamped to that custom value, not to 50,
- `PageToken` binds from a query string through MVC model binding (one `WebApplicationFactory` or minimal-host test),
- the handler answers a `PagingException` with `400` and a problem body, and returns `false` for any other exception,
- the transformer rewrites a `Page<T>` response and leaves every other response alone.

Drop the depth-bound test from the Feng file: it only re-tests `Paging.Counted`, which the core's `PagingTests` already cover.

## What Feng Removes Afterwards

On the Feng branch, once the package exists:

| Delete | Replaced by |
| --- | --- |
| `src/core/Feng.Backend.Core.Presentation/Requests/PagingKind.cs` | `Zooper.Ant.AspNetCore.Requests.PagingKind` |
| `src/core/Feng.Backend.Core.Presentation/Requests/PagingRequest.cs` | `Zooper.Ant.AspNetCore.Requests.PagingRequest` |
| `src/core/Feng.Backend.Core.Presentation/Requests/PagingRequestExtensions.cs` | `ToPaging(pager.Limits)` |
| `src/core/tests/Feng.Backend.Core.Presentation.UnitTests/Requests/PagingRequestExtensionsTests.cs` | the package's tests |
| `DescribePageResponseAsync` in `src/core/Feng.Backend.Core.Infrastructure/Extensions/ApiDocumentationServiceExtensions.cs` | `options.AddAntPageResponses()` |

Feng keeps its `GlobalExceptionHandlingMiddleware` clause for `PagingException` and does not register the package's handler. Its controllers gain a `Pager` dependency for the limits, or read `IOptions<AntOptions>`; either is one constructor parameter.

## Open Decisions

1. Token parameter name: `pageToken` (assumed here) or `token`.
2. One package or two: does the OpenAPI transformer, with its `Microsoft.AspNetCore.OpenApi` dependency, ship in `Zooper.Ant.AspNetCore` or in its own `Zooper.Ant.OpenApi`?
3. Whether `AddAntExceptionHandler` takes an optional error code for the problem body, or stays code-free.

## Appendix A: Feng Sources To Move

`src/core/Feng.Backend.Core.Presentation/Requests/PagingRequestExtensions.cs` on `feature/caller-selected-paging`:

```csharp
public static class PagingRequestExtensions
{
	public static Paging ToPaging(this PagingRequest request) =>
		request.Paging switch
		{
			null or PagingKind.Continuable => Refuse(request, "continuable", page: true, cap: true)
				?? Paging.Continuable(request.PageToken, request.Size),
			PagingKind.Counted => Refuse(request, "counted", pageToken: true, cap: true)
				?? Paging.Counted(request.Page, request.Size),
			PagingKind.Capped => Refuse(request, "capped", page: true, size: true, pageToken: true)
				?? Paging.Capped(request.Cap),
			_ => throw new PagingException($"Paging kind '{request.Paging}' is not known.")
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
			: throw new PagingException($"Paging argument '{intruder}' does not belong to {kind} paging.");
	}
}
```

`DescribePageResponseAsync` from `src/core/Feng.Backend.Core.Infrastructure/Extensions/ApiDocumentationServiceExtensions.cs`:

```csharp
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
```

Registered today with `options.AddOperationTransformer(DescribePageResponseAsync)` inside `AddOpenApi`.

## Appendix B: Feng Test To Move

`src/core/tests/Feng.Backend.Core.Presentation.UnitTests/Requests/PagingRequestExtensionsTests.cs` (xUnit, no Shouldly):

```csharp
public class PagingRequestExtensionsTests
{
	private sealed class TestPagingRequest : PagingRequest;

	private static readonly PageToken Token = PageToken.Parse("token");

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

		Assert.Throws<PagingException>(() => request.ToPaging());
	}

	public sealed record TestPagingRequestCase(
		PagingKind? Kind,
		int? Page = null,
		int? Size = null,
		int? Cap = null,
		bool PageToken = false);
}
```

Every `ToPaging()` call gains the `limits` argument when moved.

## Decisions (design interview, 2026-10-21)

Where a decision below disagrees with the sections above, the decision wins.

### Decision 1: The package binds both MVC controllers and Minimal APIs

**Settled by Daniel.** Reaffirms decision 14 of `extensibility-design.md`. One request type binds in a controller action (`[FromQuery]`) and as a Minimal API endpoint parameter; one foreign-argument refusal; one OpenAPI transformer serves both hosts. How the type binds in each host is an implementation detail.

**Why.** `Zooper.Ant.Example` is a Minimal API host (`Zooper.Ant.Example/Api.cs` binds `[AsParameters] PagingQuery`); a controllers-only package would leave it with its own copy of the binding, the duplication this package exists to remove. Minimal APIs are the default .NET 10 template.

**Rules out.** The controllers-only reading of "Component 1" and the MVC-only base class as the whole binding story.

**Affects.** "Component 1: Query Binding" (types and "Alternative considered"): the request type must bind in Minimal APIs too. "Tests": one binding test per host, not only the MVC one.

### Decision 2: Wire parameter names

**Settled by Daniel.** `paging` (`continuable` | `counted` | `capped`, omitted means `continuable`), `pageToken`, `page`, `size`, `cap`. These names are the package's wire contract and the default of every host.

**Why.** `pageToken` mirrors the `nextPageToken` the continuable page returns, the Google AIP-158 pairing, and cannot be mistaken for an auth token. `paging` names what it selects; `kind` is a word a domain filter is likely to claim. `size` is what both existing copies already use. Settled now because the names cannot change after publishing without breaking every client of every host.

**Rules out.** The example's `kind` and `token`. `pageSize`.

**Affects.** `Zooper.Ant.Example/PagingQuery.cs` and `Api.cs` comments, `README.md` and the `Paging.cs` doc example (`request.Token`, `request.Kind`): rename to the five names when the example takes the package. Resolves open decision 1 above.

### Decision 3: The parameter names are fixed

**Settled by Daniel.** The package offers no way to rename the five parameters. The binding types are helpers: a consumer who wants other names or other parameters writes its own binding and calls the `Paging` factories, as `Zooper.Ant.Example/PagingQuery.cs` does today.

**Why.** Every Ant host then speaks the same five parameters, which clients and the OpenAPI description can rely on. Renaming would need a custom binder in both hosts plus an options type for a case no host has.

**Rules out.** The "configurable query names" clause of decision 12 in `extensibility-design.md`, which is superseded.

**Affects.** `docs/briefs/extensibility-design.md` decision 12: strike "with configurable query names".

### Decision 4: One package

**Settled by Daniel.** The OpenAPI transformer ships inside `Zooper.Ant.AspNetCore`, which takes a `PackageReference` on `Microsoft.AspNetCore.OpenApi`. No `Zooper.Ant.OpenApi`.

**Why.** The .NET 9 and 10 templates reference `Microsoft.AspNetCore.OpenApi` by default, so the common host already has it; for the others it is inert unless `AddOpenApi` is called. Precedent: `FluentValidation.AspNetCore` bundles its host integrations. A later split would not break callers, because the registration call keeps its name.

**Rules out.** A separate OpenAPI package in the first release. Resolves open decision 2 above.

### Decision 5: The problem response identifies the refusal by a fixed `type` URN

**Settled by Daniel.** The handler answers 400 with RFC 9457 problem details whose `type` is `urn:zooper:ant:invalid-page-token` for `InvalidPageTokenException` and `urn:zooper:ant:invalid-paging` for every other `PagingException`; title and detail as in "Component 2". No `errorCode` extension, no option to change the URNs.

**Why.** `type` is the identifier RFC 9457 reserves for clients to branch on (stale token: restart; bad size: fix the request), so a custom code would be a second identifier doing the same job. The handler is a helper: a host with its own error convention (Feng's `errorCode: request.invalid_value`) keeps its own handler, as Feng already does.

**Rules out.** The brief's code-free handler; an `errorCode` option; the "configurable" clause of decision 10 in `extensibility-design.md`. Resolves open decision 3 above.

**Affects.** "Component 2" handler code: add the `Type` assignment. `docs/briefs/extensibility-design.md` decision 10: strike "configurable". `Zooper.Ant.Example/PagingExceptionHandler.cs` is deleted when the example takes the package.

### Decision 6: Registration surface

**Settled by standing delegation.** As the brief's "Registration Surface" table: `services.AddAntExceptionHandler()` and `openApiOptions.AddAntPageResponses()`, no `AddAntAspNetCore()` umbrella. The binding needs no registration at all (attributes and binding methods on the request type), and the transformer can only be registered inside `AddOpenApi(options => …)`, so an `IServiceCollection` umbrella would register the handler alone while pretending to do more. Feng takes the binding and the transformer without the handler, which the separate calls give for free.

**Rules out.** The `services.AddAntAspNetCore(...)` call of decision 12 in `extensibility-design.md`, superseded.

### Decision 7: Binding shape, limits, example, tests

**Settled by standing delegation.**

- `PagingRequest` is a concrete, non-sealed class with the five `[FromQuery(Name = …)]` properties. It binds as a standalone parameter in both hosts: `[FromQuery] PagingRequest paging` in a controller action, `[AsParameters] PagingRequest paging` in a Minimal API endpoint, beside the host's own filter type (as `Zooper.Ant.Example/Api.cs` already does). A host may still derive from it, which is what Feng's controllers do today. No custom model binder, no `BindAsync`: the attributes serve both hosts, so decision 14's `BindAsync` wording is an implementation note that is no longer needed.
- `ToPaging(PagingLimits limits)` takes the limits explicitly, `pager.Limits` at the call site; the endpoint injects `Pager` anyway to run the read. The binding never re-checks a bound; defaulting, clamping and depth are the core's factories' job.
- `Zooper.Ant.Example` takes the package and deletes `PagingQuery.cs` and `PagingExceptionHandler.cs`; its endpoints, README and the `Paging.cs` doc example switch to the five wire names. Every Zooper library's example shows all features of all its packages.
- Tests live in the existing `Zooper.Ant.Tests` project, following the Bee layout (one test project per repository); the test list under "Tests" stands, minus the depth-bound test, plus one binding test per host. No `Zooper.Ant.AspNetCore.Tests` project.
- `Zooper.Ant.AspNetCore` versions in lockstep with the core through `Directory.Build.props`.

**Affects.** "Package Layout" (keep `Requests/PagingKind.cs`: the enum binds case-insensitively from `paging=counted` in both hosts and an unknown value is a 400 before the action runs; drop `Zooper.Ant.AspNetCore.Tests/`), "Alternative considered: a model binder" (stays rejected), "Tests".

*Implementation note (2026-10-21):* the enum assumption did not hold. Minimal APIs match enum query values case-sensitively, so `PagingKind` shipped as a parsable struct and `PagingRequest` carries `[Bind(Prefix = "")]` for MVC; see `openspec/changes/ant-aspnetcore/design.md`, decision 8.
