using Microsoft.Extensions.DependencyInjection;

namespace Zooper.Ant.AspNetCore.DependencyInjection;

/// <summary>Registers the Zooper.Ant problem handler in an <see cref="IServiceCollection"/>.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="PagingExceptionHandler"/>, which answers every <see cref="PagingException"/> with a 400
	/// problem response. The host must also call <c>app.UseExceptionHandler()</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Calls <c>AddProblemDetails()</c>, which adds its services with <c>TryAdd</c>, so a host's own earlier
	/// <c>AddProblemDetails(configure)</c> is kept, and <c>AddExceptionHandler&lt;PagingExceptionHandler&gt;()</c>.
	/// Exception handlers run in registration order; the paging handler declines everything that is not a paging
	/// refusal, so its position among the host's handlers only matters for paging refusals.
	/// </para>
	/// <para>
	/// This is the only call that adds the handler. A host with its own global error handling leaves it out and still
	/// uses <see cref="PagingRequest"/> and the OpenAPI description.
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// builder.Services.AddAntExceptionHandler();
	/// // …
	/// app.UseExceptionHandler();
	/// </code>
	/// </example>
	/// <param name="services">The service collection.</param>
	/// <returns>The same service collection, for chaining.</returns>
	public static IServiceCollection AddAntExceptionHandler(this IServiceCollection services)
	{
		services.AddProblemDetails();
		services.AddExceptionHandler<PagingExceptionHandler>();

		return services;
	}
}
