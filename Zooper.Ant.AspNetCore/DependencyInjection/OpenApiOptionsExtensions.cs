using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Zooper.Ant.AspNetCore.DependencyInjection;

/// <summary>Adds the Zooper.Ant descriptions to an OpenAPI document.</summary>
public static class OpenApiOptionsExtensions
{
	/// <summary>
	/// Adds <see cref="PageResponseTransformer"/>, so every response declared as <see cref="Page{TItem}"/> is described as
	/// <c>oneOf</c> the three concrete page shapes, and lists the three <see cref="PagingKind"/> names as the allowed
	/// values of the <c>paging</c> parameter.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="PagingKind"/> binds as a parsable string, but the generator describes it by its JSON contract, an object
	/// with a <c>name</c>; the schema transformer added here replaces that with a <c>string</c> whose <c>enum</c> is the
	/// three wire names, so a client sees what it may send.
	/// </para>
	/// <para>
	/// Independent of <see cref="ServiceCollectionExtensions.AddAntExceptionHandler"/>: a host can take the description
	/// without the handler, or the other way round. Call it once per document that lists paged endpoints.
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// builder.Services.AddOpenApi(options =&gt; options.AddAntPageResponses());
	/// // …
	/// app.MapOpenApi();
	/// </code>
	/// </example>
	/// <param name="options">The options of one OpenAPI document.</param>
	/// <returns>The same options, for chaining.</returns>
	public static OpenApiOptions AddAntPageResponses(this OpenApiOptions options) =>
		options
			.AddOperationTransformer<PageResponseTransformer>()
			.AddSchemaTransformer((schema, context, _) =>
			{
				// The parameter is PagingKind?, so the schema may be generated for the nullable form.
				var type = context.JsonTypeInfo.Type;

				if ((Nullable.GetUnderlyingType(type) ?? type) == typeof(PagingKind))
				{
					schema.Type = JsonSchemaType.String;
					schema.Properties?.Clear();
					schema.Required?.Clear();
					schema.Enum = PagingKind.All.Select(kind => (JsonNode)JsonValue.Create(kind.Name)).ToList();
				}

				return Task.CompletedTask;
			});
}
