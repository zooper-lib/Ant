using Zooper.Ant;
using Zooper.Ant.AspNetCore.DependencyInjection;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Example;

// The entry point of the Zooper.Ant example. It has two modes that share one set of registrations, so the tour and the
// HTTP API run exactly the same queries against exactly the same configuration:
//
//   `dotnet run`            plays the narrated tour in the console (Tour.cs). Nothing listens on a port.
//   `dotnet run -- --serve` serves the same reads over HTTP (Api.cs); try them with Zooper.Ant.Example.http.

var serve = args.Contains("--serve");

// `--serve` is our own switch, not an ASP.NET Core setting. The host's command-line configuration reads `--key value`
// pairs, so a bare `--serve` would swallow the argument after it (for example `--serve --urls http://...` would turn
// `--urls` into the value of `serve`). Strip it before the host sees the arguments.
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--serve").ToArray());

// Zooper.Ant encrypts continuation tokens with ASP.NET Core Data Protection; AddAnt deliberately does not register it,
// because the host owns the key ring. Here the default key ring is used (a folder in the user profile). A real service
// with several instances must persist the keys somewhere shared (PersistKeysToFileSystem on a shared volume, a
// database, Redis), or a token issued by one instance is refused by another.
builder.Services.AddDataProtection();

// The one Zooper.Ant registration. It adds:
//   - Pager, the single service a query injects (see PlayerQueries);
//   - DataProtectionPageTokenProtector as IPageTokenProtector, unless the host registered its own earlier;
//   - AntOptions, validated when the app starts: inconsistent limits stop the host instead of failing a request later.
builder.Services.AddAnt(options =>
{
	// Small limits so the tour can show every bound with twelve players: a request without a size gets 5 items, no page
	// holds more than 10, and a counted page may not reach beyond row 30. Library defaults are 20, 50 and 1000.
	options.Limits = new PagingLimits(DefaultSize: 5, MaximumSize: 10, MaximumDepth: 30);

	// Tokens are encrypted under (PurposeRoot, read name). Giving the example its own root keeps its tokens from ever
	// being accepted by another application sharing the same key ring. Changing it invalidates every token in flight.
	options.PurposeRoot = "Zooper.Ant.Example.PageToken";
});

// The example's own services. Singletons because the stores are in-memory tables that must survive between requests;
// a real application would register its DbContext or client with the lifetime that store needs.
builder.Services.AddSingleton<PlayerStore>();
builder.Services.AddSingleton<PlayerQueries>();
builder.Services.AddSingleton<ActivityLog>();
builder.Services.AddSingleton<Tour>();

// Zooper.Ant.AspNetCore, two independent one-liners (the query binding itself needs no registration):
//   - every paging refusal (PagingException, including InvalidPageTokenException) becomes a 400 problem response whose
//     type is urn:zooper:ant:invalid-paging or urn:zooper:ant:invalid-page-token. Only the HTTP mode uses this; the
//     tour catches the exceptions itself to print them;
//   - the OpenAPI document describes a Page<T> response as oneOf the three page shapes and lists the paging values.
builder.Services.AddAntExceptionHandler();
builder.Services.AddOpenApi(options => options.AddAntPageResponses());

// The tour writes to the console, so host logging stays quiet unless the HTTP API is running.
builder.Logging.SetMinimumLevel(serve ? LogLevel.Information : LogLevel.Warning);

var app = builder.Build();

if (!serve)
{
	// Tour mode: resolve the tour from the container (so it gets the same Pager and queries the API would) and exit.
	await app.Services.GetRequiredService<Tour>().RunAsync();
	return;
}

// HTTP mode: route exceptions to the paging handler, serve /openapi/v1.json, map the endpoints and listen on the URL
// from appsettings.json.
app.UseExceptionHandler();
app.MapOpenApi();
app.MapPlayerApi();
app.Run();
