using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Zooper.Ant.AspNetCore;

/// <summary>
/// Describes a response declared as the abstract <see cref="Page{TItem}"/> as <c>oneOf</c> the three concrete page
/// shapes, which an OpenAPI generator cannot infer on its own.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Page{TItem}"/> is serialised as its runtime kind, so the wire carries a <see cref="ContinuablePage{TItem}"/>,
/// <see cref="CountedPage{TItem}"/> or <see cref="CappedPage{TItem}"/>, but the generator only sees the abstract type
/// and would describe just its <c>itemList</c>. For an operation whose 200 response is declared as
/// <c>Page&lt;T&gt;</c> (a Minimal API handler returning <c>Task&lt;Page&lt;T&gt;&gt;</c>, an action returning
/// <c>ActionResult&lt;Page&lt;T&gt;&gt;</c>), this transformer sets the schema of every media type of that response to
/// <c>oneOf</c> the three concrete page schemas for the same <c>T</c>.
/// </para>
/// <para>
/// The three schemas come from <see cref="OpenApiOperationTransformerContext.GetOrCreateSchemaAsync"/>, so they are
/// generated exactly as the generator would generate them anywhere else. That method returns them inline, so each is
/// registered once as a component of the document under the generator's own schema id (<c>ContinuablePageOfPlayer</c>,
/// …) and the <c>oneOf</c> holds references: every operation returning a page of the same item type points at the same
/// three components. Everything else is left as generated: an operation returning a concrete page type or any
/// other type, and every non-200 response of a paged operation.
/// </para>
/// <para>
/// Registered by <see cref="DependencyInjection.OpenApiOptionsExtensions.AddAntPageResponses"/> inside
/// <c>AddOpenApi(options =&gt; …)</c>.
/// </para>
/// </remarks>
public sealed class PageResponseTransformer : IOpenApiOperationTransformer
{
	/// <summary>The concrete page kinds, in the order they appear in the <c>oneOf</c>.</summary>
	private static readonly System.Type[] PageKindList = [typeof(ContinuablePage<>), typeof(CountedPage<>), typeof(CappedPage<>)];

	/// <summary>Rewrites the 200 response schema of <paramref name="operation"/> when it is declared as <see cref="Page{TItem}"/>.</summary>
	/// <param name="operation">The operation being generated.</param>
	/// <param name="context">The operation's API description and the schema generator.</param>
	/// <param name="cancellationToken">Cancels the document generation.</param>
	/// <returns>A task that completes when the operation is transformed or left alone.</returns>
	public async Task TransformAsync(
		OpenApiOperation operation,
		OpenApiOperationTransformerContext context,
		CancellationToken cancellationToken)
	{
		// The declared type of the 200 response; Task<> and ActionResult<> are already unwrapped by the API explorer.
		var pageType = context.Description.SupportedResponseTypes
			.FirstOrDefault(responseType => responseType.StatusCode == StatusCodes.Status200OK)?.Type;

		if (pageType is not { IsGenericType: true } || pageType.GetGenericTypeDefinition() != typeof(Page<>)
			|| operation.Responses?.TryGetValue("200", out var response) != true || response?.Content is null)
			return;

		var itemType = pageType.GetGenericArguments()[0];
		var schemaList = new List<IOpenApiSchema>();

		foreach (var kind in PageKindList)
			schemaList.Add(await GetOrAddComponentAsync(kind.MakeGenericType(itemType), context, cancellationToken));

		foreach (var mediaType in response.Content.Values)
			mediaType.Schema = new OpenApiSchema { OneOf = schemaList };
	}

	/// <summary>Generates the schema of <paramref name="type"/>, registers it as a component once, and returns a reference to it.</summary>
	/// <param name="type">A concrete page type, such as <c>ContinuablePage&lt;Player&gt;</c>.</param>
	/// <param name="context">The operation's context, holding the document.</param>
	/// <param name="cancellationToken">Cancels the document generation.</param>
	/// <returns>
	/// A reference to the component, or the inline schema when the generator gave it no id or there is no document to
	/// register it in; the description is correct either way, only not shared.
	/// </returns>
	private static async Task<IOpenApiSchema> GetOrAddComponentAsync(
		System.Type type,
		OpenApiOperationTransformerContext context,
		CancellationToken cancellationToken)
	{
		var schema = await context.GetOrCreateSchemaAsync(type, null, cancellationToken);

		// The generator records the id it would give the type (honouring a host's CreateSchemaReferenceId) as metadata.
		if (context.Document is not { } document || schema.Metadata?.TryGetValue("x-schema-id", out var value) != true
			|| value is not string { Length: > 0 } id)
			return schema;

		document.Components ??= new OpenApiComponents();
		document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

		// Another operation returning a page of the same item type registered it already.
		if (!document.Components.Schemas.ContainsKey(id))
			document.AddComponent(id, schema);

		return new OpenApiSchemaReference(id, document);
	}
}
