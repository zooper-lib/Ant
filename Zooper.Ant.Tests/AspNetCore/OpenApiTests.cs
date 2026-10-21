using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Zooper.Ant.Tests.AspNetCore;

/// <summary>
/// Verifies the generated <c>/openapi/v1.json</c> of the Minimal API host: the paging parameters are the five
/// documented names (spec <c>http-paging-binding</c>, "API explorer lists the parameters"), and only a 200 response
/// declared as <see cref="Page{TItem}"/> becomes a <c>oneOf</c> of the three page shapes (spec
/// <c>openapi-page-description</c>).
/// </summary>
public class OpenApiTests
{
	/// <summary>Fetches the generated document.</summary>
	private static async Task<JsonNode> DocumentAsync()
	{
		await using var host = await TestHost.StartMinimalAsync();
		return (await host.Client.GetFromJsonAsync<JsonNode>("/openapi/v1.json"))!;
	}

	/// <summary>The JSON schema of <paramref name="status"/> on <c>GET <paramref name="path"/></c>.</summary>
	private static JsonNode Schema(JsonNode document, string path, string status = "200") =>
		document["paths"]![path]!["get"]!["responses"]![status]!["content"]!.AsObject().First().Value!["schema"]!;

	/// <summary>The list operation documents exactly the five optional query parameters, and the kind's three values.</summary>
	[Fact]
	public async Task ListOperation_DocumentsTheFiveParameters()
	{
		var document = await DocumentAsync();

		var parameterList = document["paths"]!["/players"]!["get"]!["parameters"]!.AsArray();

		parameterList.Select(p => (string)p!["name"]!).ShouldBe(["paging", "pageToken", "page", "size", "cap"], ignoreOrder: true);
		parameterList.ShouldAllBe(p => (string)p!["in"]! == "query" && p["required"] == null);

		var paging = parameterList.Single(p => (string)p!["name"]! == "paging")!["schema"]!;
		var kindSchema = paging["$ref"] is { } reference
			? document["components"]!["schemas"]![((string)reference!).Split('/').Last()]!
			: paging;
		kindSchema["enum"]!.AsArray().Select(v => (string)v!).ShouldBe(["continuable", "counted", "capped"], ignoreOrder: true);
	}

	/// <summary>A <c>Page&lt;Item&gt;</c> response is <c>oneOf</c> three references, shared by every such endpoint.</summary>
	[Fact]
	public async Task PageResponse_IsOneOfTheThreeSharedPageSchemas()
	{
		var document = await DocumentAsync();

		var oneOf = Schema(document, "/players")["oneOf"]!.AsArray().Select(s => (string)s!["$ref"]!).ToArray();

		oneOf.Length.ShouldBe(3);
		oneOf.ShouldAllBe(reference => reference.StartsWith("#/components/schemas/"));
		Schema(document, "/players/again")["oneOf"]!.AsArray().Select(s => (string)s!["$ref"]!).ShouldBe(oneOf);
		Schema(document, "/players/checked")["oneOf"]!.AsArray().Select(s => (string)s!["$ref"]!).ShouldBe(oneOf);
	}

	/// <summary>A concrete page, a plain object and the 400 of a paged operation keep the generator's schemas.</summary>
	[Fact]
	public async Task OtherResponses_AreUntouched()
	{
		var document = await DocumentAsync();

		Schema(document, "/players/continuable")["oneOf"].ShouldBeNull();
		Schema(document, "/player")["oneOf"].ShouldBeNull();
		((string)Schema(document, "/players/checked", "400")["$ref"]!).ShouldEndWith("/ProblemDetails");
	}
}
