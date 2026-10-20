using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Zooper.Ant.Tokens;

namespace Zooper.Ant.DependencyInjection;

/// <summary>Registers Zooper.Ant in an <see cref="IServiceCollection"/>.</summary>
/// <remarks>
/// One call, <see cref="AddAnt"/>, is the whole registration: the <see cref="Pager"/> a consumer injects, the
/// <see cref="IPageTokenProtector"/> it uses, and the validated <see cref="AntOptions"/>.
/// </remarks>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="Pager"/> and the Data Protection token protector, with limits validated at startup. A
	/// protector registered earlier is kept. The host owns Data Protection: call <c>AddDataProtection()</c> and share its
	/// key ring between instances.
	/// </summary>
	/// <remarks>
	/// <para>What it registers:</para>
	/// <list type="bullet">
	/// <item><description><see cref="AntOptions"/>, configured by <paramref name="configure"/>, validated by a private <c>IValidateOptions</c> and checked with <c>ValidateOnStart</c>, so an invalid configuration fails when the host starts.</description></item>
	/// <item><description><see cref="IPageTokenProtector"/> as <see cref="DataProtectionPageTokenProtector"/>, singleton, with <c>TryAdd</c>: an implementation registered before this call wins.</description></item>
	/// <item><description><see cref="Pager"/>, singleton, with <c>TryAdd</c>.</description></item>
	/// </list>
	/// <para>
	/// Calling it twice registers nothing twice; the second <paramref name="configure"/> is applied after the first.
	/// It does not call <c>AddDataProtection()</c>: the host decides where keys live. Without an
	/// <c>IDataProtectionProvider</c>, resolving <see cref="Pager"/> fails with a resolution error naming it.
	/// </para>
	/// <para>
	/// <c>ValidateOnStart</c> runs when a generic or web host starts. In a bare <c>ServiceProvider</c> without a host,
	/// validation happens on first resolution of the options instead.
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// builder.Services.AddDataProtection();
	/// builder.Services.AddAnt(options => options.Limits = new PagingLimits(DefaultSize: 20, MaximumSize: 50, MaximumDepth: 1000));
	/// </code>
	/// </example>
	/// <param name="services">The service collection.</param>
	/// <param name="configure">Configures <see cref="AntOptions.Limits"/> and <see cref="AntOptions.PurposeRoot"/>; optional.</param>
	/// <returns>The same service collection, for chaining.</returns>
	public static IServiceCollection AddAnt(this IServiceCollection services, Action<AntOptions>? configure = null)
	{
		var options = services.AddOptions<AntOptions>();

		if (configure is not null)
			options.Configure(configure);

		options.ValidateOnStart();
		// A validator class rather than Validate(predicate, message): it reports which bound is wrong, not a fixed text.
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AntOptions>, AntOptionsValidator>());
		services.TryAddSingleton<IPageTokenProtector, DataProtectionPageTokenProtector>();
		services.TryAddSingleton<Pager>();

		return services;
	}

	/// <summary>Validates <see cref="AntOptions"/>, naming the first invalid value.</summary>
	private sealed class AntOptionsValidator : IValidateOptions<AntOptions>
	{
		/// <summary>Checks the limits (<see cref="PagingLimits.IsValid"/>) and that the purpose root is not blank.</summary>
		/// <param name="name">The options name; Zooper.Ant uses only the default, unnamed options.</param>
		/// <param name="options">The configured options.</param>
		/// <returns>Success, or a failure whose message names the invalid value.</returns>
		public ValidateOptionsResult Validate(string? name, AntOptions options) =>
			options.Limits.Problem is { } problem ? ValidateOptionsResult.Fail(problem)
			: string.IsNullOrWhiteSpace(options.PurposeRoot) ? ValidateOptionsResult.Fail("AntOptions.PurposeRoot must not be blank.")
			: ValidateOptionsResult.Success;
	}
}
