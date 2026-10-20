using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using Zooper.Ant.Sources;

namespace Zooper.Ant.Tests.Contract;

/// <summary>
/// Proves the conformance suite catches a real bug (spec <c>page-sources</c>, "Seek and order disagree"). It runs every
/// case against a source that orders rows correctly but seeks without the tie-breaker, the classic keyset mistake that
/// skips rows sharing a value at a page boundary. Exactly the full-traversal case must fail, and its message must name
/// the skipped row. A suite that passed here would be useless as a safety net.
/// </summary>
public class BrokenFixtureTests
{
	/// <summary>
	/// Every case passes except the full traversal by duplicated names, which reports a row as skipped. The other cases
	/// stay green because the standard set has no ties they would cross (see <see cref="ContractRowsTests"/>).
	/// </summary>
	[Fact]
	public async Task ASeekIgnoringTheTieBreaker_FailsOnlyTheFullTraversal_NamingTheRow()
	{
		// Arrange
		var suite = new BrokenContractTests(new TieBreakerIgnoringFixture());
		var failures = new Dictionary<string, string>();

		// Act: run each [Fact] of the suite by hand and record the ones that throw.
		foreach (var method in typeof(PageSourceContractTests<TieBreakerIgnoringFixture>).GetMethods()
			.Where(m => m.GetCustomAttribute<FactAttribute>() is not null))
		{
			try
			{
				if (method.Invoke(suite, null) is Task task)
					await task;
			}
			catch (Exception exception)
			{
				failures[method.Name] = (exception is TargetInvocationException { InnerException: { } inner } ? inner : exception).Message;
			}
		}

		// Assert
		failures.Keys.ShouldBe([nameof(PageSourceContractTests<TieBreakerIgnoringFixture>.FullTraversal_SeesEveryRowExactlyOnce)]);
		failures.Values.Single().ShouldContain("was skipped");
		failures.Values.Single().ShouldContain("row ");
	}

	// The suite over the broken fixture. Private, so xUnit does not discover it and run it as a failing test class.
	private sealed class BrokenContractTests(TieBreakerIgnoringFixture fixture) : PageSourceContractTests<TieBreakerIgnoringFixture>(fixture);

	// Builds the correct in-memory source plus a second one over the same order without its last key (the Id), and
	// combines them in SeekWithout.
	private sealed class TieBreakerIgnoringFixture : InMemoryPageSourceFixture
	{
		public override async Task<IPageSource<ContractRow>> CreateAsync(IReadOnlyList<ContractRow> rows, ContractOrder order, CancellationToken ct)
		{
			var withoutTieBreaker = new QueryablePageSource<ContractRow>(
				rows.AsQueryable(), (order with { Keys = order.Keys.SkipLast(1).ToList() }).ToOrder(), InMemoryQueryExecutor.Instance, TimeSpan.FromTicks(1));

			return new SeekWithout(await base.CreateAsync(rows, order, ct), withoutTieBreaker);
		}
	}

	// Delegates everything to the correct source except the seek, which drops the tie-breaker from the position.
	private sealed class SeekWithout(IPageSource<ContractRow> correct, IPageSource<ContractRow> seek) : IPageSource<ContractRow>
	{
		public string OrderSignature => correct.OrderSignature;

		public Task<int> CountAsync(CancellationToken ct) => correct.CountAsync(ct);

		public Task<IReadOnlyList<ContractRow>> ReadAsync(int offset, int limit, CancellationToken ct) => correct.ReadAsync(offset, limit, ct);

		public async Task<IReadOnlyList<ContractRow>> ReadAfterAsync(PagePosition position, int limit, CancellationToken ct)
		{
			// Seek on every key but the tie-breaker, then order correctly: order and seek disagree.
			var after = await seek.ReadAfterAsync(new PagePosition(position.Values.SkipLast(1).ToList()), int.MaxValue, ct);
			var ordered = await correct.ReadAsync(0, int.MaxValue, ct);

			return ordered.Where(row => after.Contains(row)).Take(limit).ToList();
		}

		public PagePosition PositionOf(ContractRow item) => correct.PositionOf(item);

		public PagePosition ParsePosition(IReadOnlyList<JsonElement> payload) => correct.ParsePosition(payload);
	}
}
