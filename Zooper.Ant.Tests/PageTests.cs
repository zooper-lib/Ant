using System.Collections.Generic;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies the page kinds (spec <c>paged-reads</c>, "Mapping keeps the kind"): each concrete <see cref="Page{TItem}"/>
/// keeps its kind and page-level values through <see cref="Page{TItem}.Map{TOut}"/>, and serialises its own fields when
/// held as <see cref="object"/>, which is how a web host writes it.
/// </summary>
public class PageTests
{
	/// <summary>The items every page in these tests carries.</summary>
	private static readonly IReadOnlyList<int> ItemList = [1, 2, 3];

	/// <summary>Mapping a continuable page keeps its size and its next token, so the caller can still continue.</summary>
	[Fact]
	public void Map_OnContinuablePage_KeepsSizeAndNextPageToken()
	{
		// Arrange
		var token = PageToken.Parse("next");
		Page<int> sut = new ContinuablePage<int>(3, token, ItemList);

		// Act
		var mapped = sut.Map(item => item.ToString()).ShouldBeOfType<ContinuablePage<string>>();

		// Assert
		mapped.Size.ShouldBe(3);
		mapped.NextPageToken.ShouldBe(token);
		mapped.ItemList.ShouldBe(["1", "2", "3"]);
	}

	/// <summary>Mapping a counted page keeps its page number, size and total count.</summary>
	[Fact]
	public void Map_OnCountedPage_KeepsPageSizeAndTotalCount()
	{
		// Arrange
		Page<int> sut = new CountedPage<int>(2, 3, 62, ItemList);

		// Act
		var mapped = sut.Map(item => item.ToString()).ShouldBeOfType<CountedPage<string>>();

		// Assert
		mapped.Page.ShouldBe(2);
		mapped.Size.ShouldBe(3);
		mapped.TotalCount.ShouldBe(62);
		mapped.ItemList.ShouldBe(["1", "2", "3"]);
	}

	/// <summary>Mapping a capped page keeps its cap and whether it was truncated.</summary>
	[Fact]
	public void Map_OnCappedPage_KeepsCapAndTruncated()
	{
		// Arrange
		Page<int> sut = new CappedPage<int>(3, true, ItemList);

		// Act
		var mapped = sut.Map(item => item.ToString()).ShouldBeOfType<CappedPage<string>>();

		// Assert
		mapped.Cap.ShouldBe(3);
		mapped.Truncated.ShouldBeTrue();
		mapped.ItemList.ShouldBe(["1", "2", "3"]);
	}

	/// <summary>
	/// A continuable page held as <see cref="object"/> writes its own fields, with the token as a plain string and no
	/// type discriminator.
	/// </summary>
	[Fact]
	public void Serialize_ContinuablePageAsObject_WritesItsOwnFieldsWithoutTypeDiscriminator()
	{
		// Arrange
		object sut = new ContinuablePage<int>(3, PageToken.Parse("next"), ItemList);

		// Act & Assert
		JsonSerializer.Serialize(sut, JsonSerializerOptions.Web)
			.ShouldBe("""{"size":3,"nextPageToken":"next","itemList":[1,2,3]}""");
	}

	/// <summary>A counted page held as <see cref="object"/> writes its own fields without a type discriminator.</summary>
	[Fact]
	public void Serialize_CountedPageAsObject_WritesItsOwnFieldsWithoutTypeDiscriminator()
	{
		// Arrange
		object sut = new CountedPage<int>(2, 3, 62, ItemList);

		// Act & Assert
		JsonSerializer.Serialize(sut, JsonSerializerOptions.Web)
			.ShouldBe("""{"page":2,"size":3,"totalCount":62,"itemList":[1,2,3]}""");
	}

	/// <summary>A capped page held as <see cref="object"/> writes its own fields without a type discriminator.</summary>
	[Fact]
	public void Serialize_CappedPageAsObject_WritesItsOwnFieldsWithoutTypeDiscriminator()
	{
		// Arrange
		object sut = new CappedPage<int>(3, true, ItemList);

		// Act & Assert
		JsonSerializer.Serialize(sut, JsonSerializerOptions.Web)
			.ShouldBe("""{"cap":3,"truncated":true,"itemList":[1,2,3]}""");
	}
}
