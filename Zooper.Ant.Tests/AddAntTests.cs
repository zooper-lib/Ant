using System;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;
using Zooper.Ant.DependencyInjection;
using Zooper.Ant.Tokens;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies <see cref="ServiceCollectionExtensions.AddAnt"/>: limits are checked when the host starts, a host's own
/// token protector wins, and <see cref="Pager"/> resolves once Data Protection is registered (spec <c>paged-reads</c>,
/// "Bounds are configuration validated at startup"; spec <c>continuation-tokens</c>, "A host swaps the protection
/// mechanism").
/// </summary>
public class AddAntTests
{
	/// <summary>
	/// A default size above the maximum fails startup validation with a message naming the bound, so a misconfigured
	/// host never starts instead of failing on its first request.
	/// </summary>
	[Fact]
	public void AddAnt_WithInvalidLimits_FailsAtStartupNamingTheBound()
	{
		// Arrange
		using var provider = new ServiceCollection()
			.AddAnt(o => o.Limits = new PagingLimits(60, 50, 1000))
			.BuildServiceProvider();

		// Act: IStartupValidator runs what ValidateOnStart registered, as the host does when it starts.
		var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

		// Assert
		exception.Message.ShouldContain("DefaultSize");
	}

	/// <summary>Valid custom limits pass startup validation and are what <see cref="Pager.Limits"/> exposes to hosts.</summary>
	[Fact]
	public void AddAnt_WithValidCustomLimits_ExposesThemOnThePager()
	{
		// Arrange
		using var provider = Services().AddAnt(o => o.Limits = new PagingLimits(25, 100, 5000)).BuildServiceProvider();

		// Act
		provider.GetRequiredService<IStartupValidator>().Validate();

		// Assert
		provider.GetRequiredService<Pager>().Limits.ShouldBe(new PagingLimits(25, 100, 5000));
	}

	/// <summary>A protector the host registered before <c>AddAnt</c> is kept, so replacing token protection needs no change to reads.</summary>
	[Fact]
	public void AddAnt_KeepsAProtectorRegisteredEarlier()
	{
		// Arrange
		var protector = Substitute.For<IPageTokenProtector>();

		// Act
		using var provider = Services().AddSingleton(protector).AddAnt().BuildServiceProvider();

		// Assert
		provider.GetRequiredService<IPageTokenProtector>().ShouldBeSameAs(protector);
	}

	/// <summary>With Data Protection registered, <see cref="Pager"/> resolves as a singleton over the Data Protection protector.</summary>
	[Fact]
	public void AddAnt_ResolvesThePagerWithTheDataProtectionProtector()
	{
		// Arrange
		using var provider = Services().AddAnt().BuildServiceProvider();

		// Act & Assert
		provider.GetRequiredService<Pager>().ShouldNotBeNull();
		provider.GetRequiredService<IPageTokenProtector>().ShouldBeOfType<DataProtectionPageTokenProtector>();
		provider.GetRequiredService<Pager>().ShouldBeSameAs(provider.GetRequiredService<Pager>());
	}

	/// <summary>
	/// <c>AddAnt</c> does not register Data Protection itself (the host owns the key ring), so forgetting it fails at
	/// resolution with an error that names the missing service.
	/// </summary>
	[Fact]
	public void AddAnt_WithoutDataProtection_FailsAtResolution()
	{
		// Arrange
		using var provider = new ServiceCollection().AddAnt().BuildServiceProvider();

		// Act & Assert
		Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<Pager>()).Message.ShouldContain(nameof(IDataProtectionProvider));
	}

	// A container with an in-memory Data Protection provider, as a host would have after AddDataProtection().
	private static IServiceCollection Services() =>
		new ServiceCollection().AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
}
