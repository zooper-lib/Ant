# Tasks

## 1. Repository scaffold

- [x] 1.1 Copy `Directory.Build.props`, `LICENSE`, `icon.png`, `.gitignore`, `.editorconfig` and the CHANGELOG skeleton from `Projects/Zooper/Bee`, set `<Version>0.1.0</Version>`, package metadata for `Zooper.Ant`, `net10.0`, `Nullable enable`, `ImplicitUsings disable`; verify `dotnet build` of an empty solution succeeds.
- [x] 1.2 Create `Zooper.Ant.sln` with projects `Zooper.Ant` (packable, references `Microsoft.AspNetCore.DataProtection.Abstractions` and `Microsoft.Extensions.Options` only), `Zooper.Ant.Tests` (xunit.v3, Shouldly, NSubstitute, references core), with `InternalsVisibleTo("Zooper.Ant.Tests")` in `Zooper.Ant/Properties/AssemblyInfo.cs`; verify `dotnet test` runs zero tests green and `dotnet pack Zooper.Ant` lists exactly those two dependencies.
- [x] 1.3 Move `paging-package-brief.md` to `docs/briefs/` and add `.github/workflows/pr.yml` (build and test on PR) and `nuget.publish.yml` (publish packable projects on `v*` tag) adapted from Bee; verify `pr.yml` passes `actionlint` or a dry parse and the brief's relative links still resolve.

## 2. Paging instruction, limits, page kinds, exceptions

- [x] 2.1 Add `PagingException` and `InvalidPageTokenException : PagingException` in `Zooper.Ant`; verify a unit test asserts the inheritance and that both carry a message.
- [x] 2.2 Add `PagingLimits` positional record (`DefaultSize = 20, MaximumSize = 50, MaximumDepth = 1000`) with `Default` and `Validate()` enforcing `1 <= DefaultSize <= MaximumSize` and `MaximumDepth >= MaximumSize`; verify `PagingLimitsTests` cover each invalid combination and the valid default.
- [x] 2.3 Port `Paging`, `ContinuablePaging`, `CountedPaging`, `CappedPaging` from the brief's Appendix A with `PagingLimits` arguments, `PagingLimits.Default` overloads, `PagingException` refusals and `ToEmptyPage<T>()`; verify the ported `PagingTests` pass, extended with a custom-limits case and the spec `paged-reads` depth and clamp scenarios.
- [x] 2.4 Port `Page<T>`, `ContinuablePage<T>`, `CountedPage<T>`, `CappedPage<T>` with kind-preserving `Map`; verify the ported `PageTests` pass including "Mapping keeps the kind" for all three kinds.

## 3. Token, scope and protector

- [x] 3.1 Add `readonly record struct PageToken` with `Parse`/`TryParse` (non-blank, base64url alphabet, no padding), `IParsable<PageToken>`, a `JsonConverter` and a `TypeConverter`; verify `PageTokenTests` cover blank, whitespace, padding, illegal character, JSON round trip and `TypeConverter.ConvertFromString`.
- [x] 3.2 Add `PageScope.For(readName, params object?[])`, `BoundScope(ReadName, Fingerprint)` and `Bind(orderSignature)` with the pinned `CanonicalJson` options and SHA-256 base64url fingerprint; verify `PageScopeTests` show identical fingerprints across two thread cultures, different fingerprints for a changed value and for a changed order signature, and that size is not an input.
- [x] 3.3 Add `IPageTokenProtector` (`Protect(BoundScope, PagePosition)`, `Unprotect(BoundScope, PageToken) → IReadOnlyList<JsonElement>`) and `DataProtectionPageTokenProtector` with payload `{ v, q, p }`, purpose `(options.PurposeRoot, readName)`, one fixed refusal message; verify the ported `DataProtectionPageTokenProtectorTests` pass plus new cases: foreign read name, changed fingerprint, `v` = 2 refused, missing `p` refused, ephemeral provider round trip.
- [x] 3.4 Document the token format, the purpose root option and the multi-instance key-ring requirement in `README.md` under "Tokens".

## 4. Floor interface and the pager

- [x] 4.1 Add `IPageSource<T>` (with `OrderSignature`) and `PagePosition` in `Zooper.Ant.Sources`; verify the contract compiles against an NSubstitute mock in a placeholder test.
- [x] 4.2 Add `AntOptions` and `Pager` with `ReadAsync` implementing the kind switch from design decision 4, including `NotSupportedException` → `PagingException("counted kind not available")`, token issued from the size-th item, `ParsePosition` errors surfacing as `InvalidPageTokenException`; verify `PagerTests` against a mocked source assert the exact call pattern per kind, the empty-set and last-page cases, and the three refusal paths.
- [x] 4.3 Add `Pager.TraverseAsync`; verify `TraverseAsyncTests` cover multi-page, empty, incremental fetch (second page not read until first item consumed), a thrown storage error surfacing once with no third read, cancellation, early disposal, and a counted page raising `InvalidOperationException`.

## 5. Order chain and LINQ builder

- [x] 5.1 Add `Order<T>`, `OrderKey<T>` (`By`, `ByDescending`, `ThenBy`, `ThenByDescending`, `nullsFirst`, `Reverse()`, compiled getters, `OrderSignature`); verify `OrderTests` show the signature changes on direction, nulls and key changes, is stable across two processes' worth of instances, and `Reverse()` flips every flag.
- [x] 5.2 Add `SeekExpressionBuilder.ApplyOrdering` with the null-rank key for nullable keys; verify `SeekExpressionBuilderTests` sort an in-memory set with nulls last and nulls first, ascending and descending, matching an `OrderBy` oracle.
- [x] 5.3 Add `SeekExpressionBuilder.BuildSeek` with the expanded predicate, operator-or-`CompareTo` comparison, boxed parameter values and the null-bookmark rule; verify tests evaluate the predicate in memory for: three-key mixed-direction chain, `string` and `Guid` keys, nullable key with non-null bookmark, nullable key with null bookmark, enum key, and assert the expression tree contains no `ConstantExpression` of the bookmark value type.
- [x] 5.4 Add `PositionOf` (microsecond truncation, UTC normalisation) and `ParsePosition` (count check, typed deserialise, `InvalidPageTokenException`) on the LINQ source side; verify tests cover a `DateTime` with sub-microsecond ticks, an unspecified-kind `DateTime`, a `DateTimeOffset`, a short payload and a wrong-type value.

## 6. Queryable source, in-memory executor, entry point

- [x] 6.1 Add `IQueryExecutor`, `QueryablePageSource<T>` and `InMemoryQueryExecutor`; verify `QueryablePageSourceTests` show `ReadAsync(offset, limit)`, `ReadAfterAsync` and `CountAsync` delegate to the executor with the ordered, filtered query.
- [x] 6.2 Add `QueryableExtensions.ToPageAsync` for `IQueryable<T>` and `IEnumerable<T>` over the in-memory executor; verify an end-to-end test pages a 95-item list through all three kinds with `Pager` and `PlainTextPageTokenProtector`, and a full continuable walk sees every item exactly once.
- [x] 6.3 Add `ServiceCollectionExtensions.AddAnt` (`ValidateOnStart`, `TryAddSingleton` protector, singleton `Pager`); verify `AddAntTests` show invalid limits fail at `ValidateOnStart`, a pre-registered protector is kept, and `Pager` resolves with an ephemeral Data Protection provider.
- [x] 6.4 Write the README "Getting started" (register, declare an order, one `ToPageAsync` call, traverse) and the "Adding a store" recipe including the Dapr continuable-only floor example from the design record. README code only showcases usage; it is not compiled or tested.

## 7. In-memory conformance tests (inside `Zooper.Ant.Tests`, not shipped)

- [x] 7.1 Add `ContractRow`, `ContractRows.Standard` (ties on `CreatedAt`, null scores, duplicate names, sub-microsecond ticks), `ContractOrder` descriptors and `IPageSourceFixture`; verify a unit test asserts the standard set contains each edge case the suite relies on.
- [x] 7.2 Add a test-only `PlainTextPageTokenProtector` (base64url JSON, same refusal rules without cryptography) so tests can read and alter tokens.
- [x] 7.3 Cover round trip, foreign read, changed fingerprint and tampered byte for `DataProtectionPageTokenProtector` in `DataProtectionPageTokenProtectorTests`.
- [x] 7.4 Add `PageSourceContractTests<TFixture>` with one `[Fact]` per case in spec `page-sources` "The in-memory source is the reference implementation" (every kind, mixed directions, null bookmark, reversal, foreign read, changed arguments, tampered token, depth beyond bound, timestamp truncation, full traversal exactly once); verify an intentionally wrong fixture whose seek ignores the tie-breaker fails only the full-traversal case with a message naming the row.
- [x] 7.5 Implement `InMemoryPageSourceFixture` in `Zooper.Ant.Tests` and run the suite against it; verify all cases pass, making the in-memory source the oracle.

## 8. Integration checks

- [x] 8.1 Run `dotnet pack`; verify only `Zooper.Ant` is packed, with `.nupkg` and `.snupkg`, depending only on `Microsoft.AspNetCore.DataProtection.Abstractions` and `Microsoft.Extensions.Options`.
- [x] 8.2 Walk every scenario of the five specs against the test names and mark each as covered in a `docs/spec-coverage.md` table; verify no scenario is left without a test or an explicit "host-level, AspNetCore change" note.
- [x] 8.3 Add `CHANGELOG.md` entry `0.1.0` listing the package and the public surface; verify the entry follows Keep a Changelog headings.

## 9. Example project

- [x] 9.1 Add `Zooper.Ant.Example`: `dotnet run` plays a narrated console tour (one chapter per feature: what it shows, the call, the readable result) over twelve named players, and `--serve` exposes the same reads over HTTP. It shows `AddAnt` with custom limits and purpose root, the three kinds from one read with kind-bound query arguments, filters bound to tokens through `PageScope`, orders with a nullable key and `Reverse()`, `Map` to a view type, `ToEmptyPage`, a streaming export through `TraverseAsync`, live inserts between pages, a cursor-based `IPageSource` that refuses the counted kind, and paging refusals as 400 problem responses; verify by running the tour and every request of `Zooper.Ant.Example.http`.
- [x] 9.2 Point the README at the example; remove the separate testing package and every compiled-README artefact.

## Workflow follow-up

- Archive the change after review; sync the five delta specs into `openspec/specs/`.
- Open the next change for the AspNetCore binding, then one per adapter (EF Core, Marten, Npgsql).
