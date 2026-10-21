using System;
using System.Linq;
using Shouldly;
using Xunit;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests;

/// <summary>
/// Verifies how an <see cref="Order{T}"/> chain is declared and described (spec <c>keyset-ordering</c>). Its
/// <see cref="Order{T}.OrderSignature"/> goes into every token binding, so it must change whenever the ordering
/// changes and stay identical otherwise; <see cref="Order{T}.Reverse"/> must flip the whole chain consistently.
/// </summary>
public class OrderTests
{
	/// <summary>An item with a timestamp, a nullable score, a non-null name and a unique id, enough for every key kind.</summary>
	private sealed record Item(int Id, DateTime CreatedAt, double? Score, string Name);

	/// <summary>
	/// Two chains built separately, with different lambda parameter names, have the same signature. Otherwise a
	/// harmless rename in code, or a second process, would refuse every token in flight.
	/// </summary>
	[Fact]
	public void OrderSignature_IsTheSameForSeparatelyBuiltChains_WhateverTheParameterName()
	{
		// Arrange
		var first = Order<Item>.By(x => x.CreatedAt).ThenByDescending(x => x.Score).ThenBy(x => x.Id);
		var second = Order<Item>.By(item => item.CreatedAt).ThenByDescending(i => i.Score).ThenBy(row => row.Id);

		// Act & Assert
		second.OrderSignature.ShouldBe(first.OrderSignature);
	}

	/// <summary>
	/// The signature changes with a key's direction, with its nulls placement, with another key and with an added key.
	/// Each of those changes the sequence, so a token issued under the old ordering must be refused.
	/// </summary>
	[Fact]
	public void OrderSignature_ChangesOnDirection_Nulls_AndKeys()
	{
		// Arrange
		var sut = Order<Item>.By(x => x.Score).ThenBy(x => x.Id).OrderSignature;

		// Act & Assert
		Order<Item>.ByDescending(x => x.Score).ThenBy(x => x.Id).OrderSignature.ShouldNotBe(sut);
		Order<Item>.By(x => x.Score, nullsFirst: true).ThenBy(x => x.Id).OrderSignature.ShouldNotBe(sut);
		Order<Item>.By(x => x.Name).ThenBy(x => x.Id).OrderSignature.ShouldNotBe(sut);
		Order<Item>.By(x => x.Score).ThenBy(x => x.Name).ThenBy(x => x.Id).OrderSignature.ShouldNotBe(sut);
	}

	/// <summary>
	/// Reversing flips every key's direction and nulls placement together and keeps the keys; reversing twice gives
	/// the original chain back. Flipping only the directions would leave nulls on the wrong end.
	/// </summary>
	[Fact]
	public void Reverse_FlipsEveryDirectionAndNullsPlacement()
	{
		// Arrange
		var sut = Order<Item>.By(x => x.CreatedAt).ThenByDescending(x => x.Score, nullsFirst: true).ThenBy(x => x.Id);

		// Act
		var reversed = sut.Reverse();

		// Assert
		reversed.Keys.Select(k => k.Descending).ShouldBe([true, false, true]);
		reversed.Keys.Select(k => k.NullsFirst).ShouldBe([true, false, true]);
		reversed.Keys.Select(k => k.Key).ShouldBe(sut.Keys.Select(k => k.Key));
		reversed.Reverse().OrderSignature.ShouldBe(sut.OrderSignature);
	}

	/// <summary>
	/// A key's type and nullability are inferred from the lambda, with no string name or type restated, and its
	/// compiled getter reads the value. Only <c>double?</c> is nullable here: <c>Name</c> is a non-nullable string.
	/// </summary>
	[Fact]
	public void Keys_InferTypeAndNullability()
	{
		// Arrange
		var sut = Order<Item>.By(x => x.Score).ThenBy(x => x.Name).ThenBy(x => x.Id);

		// Act & Assert
		sut.Keys.Select(k => k.KeyType).ShouldBe([typeof(double?), typeof(string), typeof(int)]);
		sut.Keys.Select(k => k.IsNullable).ShouldBe([true, false, false]);
		sut.Keys[2].GetValue(new Item(7, default, null, "a")).ShouldBe(7);
	}
}
