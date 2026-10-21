using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using Zooper.Ant.AspNetCore;

namespace Zooper.Ant.Tests.AspNetCore;

/// <summary>
/// Verifies that <see cref="PagingRequest"/> binds the five query parameters identically in a Minimal API endpoint and
/// an MVC action, beside a host filter, and that malformed values are refused before the endpoint runs (spec
/// <c>http-paging-binding</c>).
/// </summary>
public class BindingTests
{
	/// <summary>Both hosts, so every binding test runs against each.</summary>
	public static TheoryData<string> HostList => ["minimal", "mvc"];

	/// <summary>Starts the named host.</summary>
	private static Task<TestHost> StartAsync(string host) =>
		host == "mvc" ? TestHost.StartMvcAsync() : TestHost.StartMinimalAsync();

	/// <summary>Sends <paramref name="query"/> to <c>/items</c> and reads back what was bound.</summary>
	private static async Task<BoundPaging> BindAsync(TestHost host, string query)
	{
		var response = await host.Client.GetAsync("/items" + query);
		response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
		return (await response.Content.ReadFromJsonAsync<BoundPaging>())!;
	}

	/// <summary>A continuable request binds its size and token, and the filter binds beside it.</summary>
	[Theory]
	[MemberData(nameof(HostList))]
	public async Task Continuable_Binds(string kind)
	{
		await using var host = await StartAsync(kind);

		var bound = await BindAsync(host, "?paging=continuable&size=20&pageToken=abc&team=red");

		bound.ShouldBe(new BoundPaging("continuable", 20, null, null, "abc", "red"));
	}

	/// <summary>A counted request binds its page and size.</summary>
	[Theory]
	[MemberData(nameof(HostList))]
	public async Task Counted_Binds(string kind)
	{
		await using var host = await StartAsync(kind);

		(await BindAsync(host, "?paging=counted&page=2&size=10")).ShouldBe(new BoundPaging("counted", 10, 2, null, null, null));
	}

	/// <summary>A capped request binds its cap.</summary>
	[Theory]
	[MemberData(nameof(HostList))]
	public async Task Capped_Binds(string kind)
	{
		await using var host = await StartAsync(kind);

		(await BindAsync(host, "?paging=capped&cap=5")).ShouldBe(new BoundPaging("capped", null, null, 5, null, null));
	}

	/// <summary>No <c>paging</c> is continuable, and the host's default size applies.</summary>
	[Theory]
	[MemberData(nameof(HostList))]
	public async Task OmittedKind_IsContinuable(string kind)
	{
		await using var host = await StartAsync(kind);

		(await BindAsync(host, "")).ShouldBe(new BoundPaging("continuable", TestHost.Limits.DefaultSize, null, null, null, null));
	}

	/// <summary>The kind is matched without regard to case.</summary>
	[Theory]
	[MemberData(nameof(HostList))]
	public async Task Kind_BindsCaseInsensitively(string kind)
	{
		await using var host = await StartAsync(kind);

		(await BindAsync(host, "?paging=Counted&page=1")).Kind.ShouldBe("counted");
	}

	/// <summary>A size above the host's maximum is clamped to it, not to the built-in 50.</summary>
	[Theory]
	[MemberData(nameof(HostList))]
	public async Task Size_IsClampedByTheHostLimits(string kind)
	{
		await using var host = await StartAsync(kind);

		(await BindAsync(host, "?size=500")).Size.ShouldBe(TestHost.Limits.MaximumSize);
	}

	/// <summary>A value that is not a kind, and a token with padding, are 400 and the endpoint never runs.</summary>
	[Theory]
	[InlineData("minimal", "?paging=infinite")]
	[InlineData("minimal", "?pageToken=abc==")]
	[InlineData("mvc", "?paging=infinite")]
	[InlineData("mvc", "?pageToken=abc==")]
	public async Task MalformedValue_Is400BeforeTheEndpoint(string kind, string query)
	{
		await using var host = await StartAsync(kind);

		var response = await host.Client.GetAsync("/items" + query);

		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
		host.Hits.Count.ShouldBe(0);
	}
}
