using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Zooper.Ant.AspNetCore;

/// <summary>
/// The kind of page a caller asks for in the <c>paging</c> query parameter: <c>continuable</c>, <c>counted</c> or
/// <c>capped</c>. Absent means continuable.
/// </summary>
/// <remarks>
/// <para>
/// Each kind selects one <see cref="Paging"/> factory in <see cref="PagingRequestExtensions.ToPaging"/> and therefore
/// one page shape in the response:
/// <list type="table">
/// <listheader><term>Kind</term><description>Arguments → response</description></listheader>
/// <item><term><see cref="Continuable"/></term><description><c>pageToken</c>, <c>size</c> →
/// <see cref="ContinuablePage{TItem}"/></description></item>
/// <item><term><see cref="Counted"/></term><description><c>page</c>, <c>size</c> →
/// <see cref="CountedPage{TItem}"/></description></item>
/// <item><term><see cref="Capped"/></term><description><c>cap</c> → <see cref="CappedPage{TItem}"/></description></item>
/// </list>
/// </para>
/// <para>
/// Why a struct and not an enum: Minimal APIs parse an enum query value case-sensitively against the member names, so
/// the documented wire value <c>counted</c> would not bind to a member named <c>Counted</c>. A type with its own
/// <see cref="TryParse(string?, IFormatProvider?, out PagingKind)"/> binds in Minimal APIs through
/// <see cref="IParsable{TSelf}"/> and in MVC through its <see cref="TypeConverter"/>, both case-insensitively, and an
/// unknown value fails binding with a 400 before the endpoint runs in either host. The three values are listed in the
/// OpenAPI document by <see cref="PageResponseTransformer"/>'s registration,
/// <see cref="DependencyInjection.OpenApiOptionsExtensions.AddAntPageResponses"/>.
/// </para>
/// <para>
/// Only the three static instances exist; the constructor is private. <c>default(PagingKind)</c> has a
/// <see langword="null"/> <see cref="Name"/>, is not a kind, and is refused by <see cref="PagingRequestExtensions.ToPaging"/>.
/// An absent parameter is a <see langword="null"/> <see cref="PagingRequest.Paging"/>, not a default value.
/// </para>
/// </remarks>
[TypeConverter(typeof(PagingKindTypeConverter))]
public readonly record struct PagingKind : IParsable<PagingKind>
{
	/// <summary>Stores the wire name; only the three static instances call it.</summary>
	/// <param name="name">The lower-case wire name.</param>
	private PagingKind(string name) => Name = name;

	/// <summary>A page continued by token; the default kind. Answered with a <see cref="ContinuablePage{TItem}"/>.</summary>
	public static PagingKind Continuable { get; } = new("continuable");

	/// <summary>A numbered page with a total count, within the depth bound. Answered with a <see cref="CountedPage{TItem}"/>.</summary>
	public static PagingKind Counted { get; } = new("counted");

	/// <summary>One screen of at most a cap of items, with no continuation. Answered with a <see cref="CappedPage{TItem}"/>.</summary>
	public static PagingKind Capped { get; } = new("capped");

	/// <summary>The three kinds, in documentation order.</summary>
	public static PagingKind[] All { get; } = [Continuable, Counted, Capped];

	/// <summary>The lower-case wire name: <c>continuable</c>, <c>counted</c> or <c>capped</c>.</summary>
	public string Name { get; }

	/// <summary>Parses a wire name, ignoring case.</summary>
	/// <param name="s">The query value.</param>
	/// <param name="provider">Ignored; the names are culture-invariant.</param>
	/// <returns>The kind.</returns>
	/// <exception cref="FormatException"><paramref name="s"/> is not one of the three names.</exception>
	public static PagingKind Parse(string s, IFormatProvider? provider) =>
		TryParse(s, provider, out var kind)
			? kind
			: throw new FormatException($"Paging kind '{s}' is not one of continuable, counted, capped.");

	/// <summary>Parses a wire name, ignoring case.</summary>
	/// <param name="s">The query value.</param>
	/// <param name="provider">Ignored; the names are culture-invariant.</param>
	/// <param name="result">The kind, or <see langword="default"/> when <paramref name="s"/> is not a kind.</param>
	/// <returns>Whether <paramref name="s"/> is one of the three names.</returns>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PagingKind result)
	{
		foreach (var kind in All)
		{
			if (string.Equals(s, kind.Name, StringComparison.OrdinalIgnoreCase))
			{
				result = kind;
				return true;
			}
		}

		result = default;
		return false;
	}

	/// <inheritdoc />
	/// <remarks>The wire name.</remarks>
	public override string ToString() => Name;
}

/// <summary>Converts a <see cref="PagingKind"/> from and to its wire name, for MVC query-string binding.</summary>
/// <remarks>
/// Applied to <see cref="PagingKind"/> by attribute. An unknown name throws a <see cref="FormatException"/>, which MVC
/// records as a model-state error; <c>[ApiController]</c> then answers 400 before the action runs.
/// </remarks>
public sealed class PagingKindTypeConverter : TypeConverter
{
	/// <inheritdoc />
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	/// <inheritdoc />
	/// <exception cref="FormatException">The string is not one of the three names.</exception>
	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
		value is string text ? PagingKind.Parse(text, culture) : base.ConvertFrom(context, culture, value);

	/// <inheritdoc />
	public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
		value is PagingKind kind && destinationType == typeof(string) ? kind.Name : base.ConvertTo(context, culture, value, destinationType);
}
