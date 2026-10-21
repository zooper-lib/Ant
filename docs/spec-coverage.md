# Spec coverage: `paging-core`

Every scenario of the five specs in `openspec/changes/paging-core/specs`, mapped to the test that verifies it. Test names are `Class.Method` in `Zooper.Ant.Tests` unless prefixed with `Contract:` (a case of the in-memory conformance suite, `Contract/PageSourceContractTests.cs`, run by `InMemoryPageSourceContractTests`). `Example:` marks behaviour also shown live in `Zooper.Ant.Example` (`Zooper.Ant.Example.http`). Scenarios that only a host or a later change can exercise say so.

## continuation-tokens

| Scenario | Covered by |
| --- | --- |
| First request | `PagerTests.ReadAsync_Continuable_IssuesTheTokenFromTheSizeThItem_AndContinuesAfterIt` |
| Continuation | same; `InMemoryPagingTests.Continuable_FullWalk_SeesEveryItemExactlyOnceInOrder` |
| End of the read | `PagerTests.ReadAsync_Continuable_OnTheLastPage_IssuesNoToken` |
| One token form for every host | Core: `PageToken` is the only token type, carried by `ContinuablePage` and accepted by `Paging.Continuable` (`PageTokenTests`). The HTTP form is host-level, AspNetCore change. |
| An earlier item is deleted | `LiveDataTests.AnEarlierItemDeleted_DoesNotShiftTheNextPage` |
| Continuation needs no offset | `LiveDataTests.ContinuationPastTheDepthBound_IsServed` |
| Same read | `InMemoryPagingTests.Continuable_FullWalk_SeesEveryItemExactlyOnceInOrder` |
| Foreign read | `Contract: ForeignRead_IsRefused`; `DataProtectionPageTokenProtectorTests.Unprotect_WhenTokenWasIssuedByAnotherRead_Throws`; `PagerTests.ReadAsync_WhenTheTokenIsForeign_RefusesAndReadsNothing` |
| Same criteria | `InMemoryPagingTests.Continuable_FullWalk_SeesEveryItemExactlyOnceInOrder` |
| Changed criteria | `Contract: ChangedArguments_AreRefused`; `DataProtectionPageTokenProtectorTests.Unprotect_WhenFingerprintDiffers_Throws`; Example: `team=blue` with a red token |
| Changed ordering | `PagerTests.ReadAsync_WhenTheOrderingChanged_RefusesTheToken`; `PageScopeTests.Bind_WhenTheOrderSignatureChanges_GivesAnotherFingerprint` |
| Changed size | `LiveDataTests.TheSizeMayChangeBetweenPages`; `PageScopeTests.Bind_KeepsTheReadName_AndTakesNoSize` |
| Culture does not matter | `PageScopeTests.Bind_UnderTwoCultures_GivesTheSameFingerprint` |
| A declared value changes | `PageScopeTests.Bind_WhenAValueChanges_GivesAnotherFingerprint` |
| Round trip | `DataProtectionPageTokenProtectorTests.Unprotect_WhenTokenIsUnchanged_ReturnsThePosition` |
| Tampered token | `DataProtectionPageTokenProtectorTests.Unprotect_WhenOneCharacterIsAltered_Throws`; `Contract: TamperedToken_IsRefused` |
| Token issued before a restart | `DataProtectionPageTokenProtectorTests.Unprotect_WhenTokenWasIssuedByAnEarlierProtector_Succeeds` (same key ring, new protector). Shared key ring across instances is host configuration, documented in README "Tokens". |
| A host swaps the protection mechanism | `AddAntTests.AddAnt_KeepsAProtectorRegisteredEarlier`; `InMemoryPagingTests` (reads through `PlainTextPageTokenProtector`) |
| Blank token | `PageTokenTests.Parse_WhenValueIsBlankOrNotBase64Url_Throws` |
| Well-formed but invalid content | `DataProtectionPageTokenProtectorTests.Unprotect_WhenTokenIsNotProtectedData_Throws` |
| Ordering gained a key | `SeekExpressionBuilderTests.ParsePosition_WhenThePayloadIsShort_Refuses`; `Contract: TamperedToken_IsRefused` |
| Value of the wrong type | `SeekExpressionBuilderTests.ParsePosition_WhenAValueHasTheWrongType_Refuses`; `PagerTests.ReadAsync_WhenTheSourceCannotParseThePosition_RefusesTheToken` |
| An old token | `DataProtectionPageTokenProtectorTests.Unprotect_WhenTokenWasIssuedByAnEarlierProtector_Succeeds` (the payload carries no time) |
| Age is not a refusal reason | same; the payload is `{ v, q, p }` with no timestamp (`DataProtectionPageTokenProtectorTests.Protect_UsesThePurposeRootAndTheReadName`) |
| Host maps both alike | `PagingExceptionTests.InvalidPageTokenException_IsAPagingException_WithAMessage` |
| Host distinguishes | `PagerTests.ReadAsync_Counted_WhenTheSourceCannotCount_RefusesNamingTheKind` (a non-token refusal is not an `InvalidPageTokenException`) |

## keyset-ordering

| Scenario | Covered by |
| --- | --- |
| Order and seek come from one declaration | `SeekExpressionBuilderTests.BuildSeek_FromEveryRow_SelectsExactlyTheRowsAfterIt`; `Contract: FullTraversal_SeesEveryRowExactlyOnce` |
| Equal sort values across a page boundary | `Contract: FullTraversal_SeesEveryRowExactlyOnce` (duplicate names, size 2) |
| Typed LINQ key | `OrderTests.Keys_InferTypeAndNullability` (the key is a lambda; renaming the property is a compile error) |
| Computed SQL key | SQL-text builder change; no SQL key exists in this change. |
| Three-key seek | `SeekExpressionBuilderTests.BuildSeek_ThreeKeyChain_IsTheExpandedForm` |
| Deleted rows do not shift the page | `LiveDataTests.AnEarlierItemDeleted_DoesNotShiftTheNextPage` |
| Descending first key | `SeekExpressionBuilderTests.BuildSeek_FromEveryRow_SelectsExactlyTheRowsAfterIt` ("score desc …"); `Contract: MixedDirections_TraverseInChainOrder` |
| Non-null bookmark on a nullable key | `SeekExpressionBuilderTests.BuildSeek_FromEveryRow_SelectsExactlyTheRowsAfterIt` (nullable orders) |
| Null bookmark | `SeekExpressionBuilderTests.BuildSeek_NullBookmarkUnderNullsLast_SeeksOnlyAmongNulls`; `Contract: NullBookmark_SeeksOnlyAmongNulls` |
| Microsecond truncation | `SeekExpressionBuilderTests.PositionOf_TruncatesDateTimeToMicroseconds`; `Contract: TimestampTruncation_ServesEachRowOnce` |
| Key added to the chain | `SeekExpressionBuilderTests.ParsePosition_WhenThePayloadIsShort_Refuses`; `OrderTests.OrderSignature_ChangesOnDirection_Nulls_AndKeys` |
| Reversed chain | `OrderTests.Reverse_FlipsEveryDirectionAndNullsPlacement`; `Contract: Reversal_TraversesInReversedOrder` |

## paged-reads

| Scenario | Covered by |
| --- | --- |
| A client needs a total and arbitrary page jumps | `PagerTests.ReadAsync_Counted_CountsAndReadsOneOffsetRange` |
| A client needs only "load more" | `PagerTests.ReadAsync_Continuable_IssuesTheTokenFromTheSizeThItem_AndContinuesAfterIt` |
| A client needs one screen of best results | `PagerTests.ReadAsync_Capped_ReadsOneBeyondTheCapAndReportsTruncation` |
| Further page exists | `PagerTests.ReadAsync_Continuable_IssuesTheTokenFromTheSizeThItem_AndContinuesAfterIt` |
| No further page | `PagerTests.ReadAsync_Continuable_OnTheLastPage_IssuesNoToken` |
| No size supplied | `PagingTests.Continuable_WhenSizeIsMissingOrNonPositive_DefaultsToTwenty` |
| Oversized page requested | `PagingTests.Continuable_WhenSizeExceedsMaximum_ClampsToFifty` |
| Oversized cap requested | `PagingTests.Capped_BoundsCapLikeASize` |
| A non-web caller is bounded alike | `PagingTests.Continuable_WhenSizeExceedsMaximum_ClampsToFifty` (the factory bounds before any store is asked) |
| Depth within the bound | `PagingTests.Counted_WhenDepthIsAtBound_Serves` |
| Depth beyond the bound | `PagingTests.Counted_WhenDepthExceedsBound_ThrowsNamingTheBound`; `Contract: DepthBeyondBound_IsRefused` |
| Page below one | `PagingTests.Counted_WhenPageIsBelowOne_Throws` |
| Continuation beyond the depth | `LiveDataTests.ContinuationPastTheDepthBound_IsServed` |
| Valid custom bounds | `PagingTests.Factories_WithCustomLimits_ApplyThem`; `AddAntTests.AddAnt_WithValidCustomLimits_ExposesThemOnThePager` |
| Default above maximum | `AddAntTests.AddAnt_WithInvalidLimits_FailsAtStartupNamingTheBound`; `PagingLimitsTests.Validate_WhenBoundsAreInconsistent_ThrowsNamingTheBound` |
| No kind named | Host-level, AspNetCore change (request binding chooses the kind). Example: `PagingQuery.ToPaging` defaults to continuable. |
| Counted named | Host-level, AspNetCore change. Core: `PagerTests.ReadAsync_Counted_CountsAndReadsOneOffsetRange`. |
| A page number on a capped request | Host-level, AspNetCore change (core `Paging` kinds cannot carry another kind's argument by construction). Example: `kind=capped&page=2` is a 400. |
| A token on a counted request | Host-level, AspNetCore change (as above). |
| Same order across kinds | `Contract: EveryKind_ServesTheSameRowsInTheSameOrder`; `InMemoryPagingTests.EveryKind_ServesTheSameFirstItems` |
| Mapping keeps the kind | `PageTests.Map_OnCountedPage_KeepsPageSizeAndTotalCount` (and the continuable and capped counterparts) |
| The same refusal on every read | `PagingTests.Counted_WhenDepthExceedsBound_ThrowsNamingTheBound`; `Contract: DepthBeyondBound_IsRefused` |
| A host maps the error | `PagingExceptionTests.InvalidPageTokenException_IsAPagingException_WithAMessage` |

## paged-traversal

| Scenario | Covered by |
| --- | --- |
| Multi-page consumption | `TraverseAsyncTests.TraverseAsync_OverThreePages_YieldsEveryItemInOrder_WithThreeReads` |
| Empty read | `TraverseAsyncTests.TraverseAsync_OverAnEmptyRead_EndsAfterOneRead` |
| Items arrive incrementally | `TraverseAsyncTests.TraverseAsync_DoesNotReadTheSecondPage_UntilTheFirstPageIsConsumed` |
| Read arguments are preserved | `TraverseAsyncTests.TraverseAsync_OverThreePages_YieldsEveryItemInOrder_WithThreeReads` (only the token passed changes) |
| A counted page is returned | `TraverseAsyncTests.TraverseAsync_WhenAPageIsCounted_RaisesInvalidOperationAndReadsNoFurther` |
| A later page fails | `TraverseAsyncTests.TraverseAsync_WhenTheSecondPageFails_SurfacesThatErrorOnceAndReadsNoThirdPage` |
| Cancellation | `TraverseAsyncTests.TraverseAsync_WhenCancelled_PassesTheTokenToThePendingReadAndReadsNoFurther` |
| Early disposal | `TraverseAsyncTests.TraverseAsync_WhenDisposedEarly_ReadsNoFurtherPage` |
| An earlier item is deleted mid-traversal | `LiveDataTests.AnEarlierItemDeleted_DoesNotShiftTheNextPage` |
| A matching row is inserted ahead of the bookmark | `LiveDataTests.ARowInsertedAheadOfTheBookmark_IsReturnedLater_AndOneBehindIsNot` |
| A matching row is inserted behind the bookmark | same |
| Renamed behind the bookmark | `LiveDataTests.AnUnreadItemRenamedBehindTheBookmark_MayBeOmitted` |
| Renamed ahead of the bookmark | `LiveDataTests.AReadItemRenamedAheadOfTheBookmark_MayBeReturnedAgain` |
| Identifier ordering is stable | `LiveDataTests.IdentifierOrdering_IsStableUnderRenames` |
| Restart after failure | `LiveDataTests.AfterAFailedTraversal_ANewOneStartsAtTheFirstPage` |
| No automatic retry | `TraverseAsyncTests.TraverseAsync_WhenTheSecondPageFails_SurfacesThatErrorOnceAndReadsNoThirdPage` (two reads, the failed one not repeated) |

## page-sources

| Scenario | Covered by |
| --- | --- |
| A new store with no shipped adapter | `PagerTests` (every kind from a mocked `IPageSource`); Example: `ActivityLogPageSource` |
| One source, one result set | `QueryablePageSourceTests` (count, read and read-after all run over the same filtered query) |
| Counted kind | `PagerTests.ReadAsync_Counted_CountsAndReadsOneOffsetRange` |
| Continuable kind | `PagerTests.ReadAsync_Continuable_IssuesTheTokenFromTheSizeThItem_AndContinuesAfterIt` |
| Capped kind | `PagerTests.ReadAsync_Capped_ReadsOneBeyondTheCapAndReportsTruncation` |
| Keyset source | `QueryablePageSourceTests.PositionOf_TruncatesTimestampsToTheGivenPrecision`; `SeekExpressionBuilderTests.ParsePosition_ReadsTheKeyTypesBack` |
| Native-cursor source | `PagerTests.ReadAsync_Continuable_IssuesTheTokenFromTheSizeThItem_AndContinuesAfterIt` (the position is whatever the source returns); Example: `/activity` |
| Counted on a native-cursor source | `PagerTests.ReadAsync_Counted_WhenTheSourceCannotCount_RefusesNamingTheKind`; Example: `/activity?kind=counted` |
| Continuable on the same source | Example: `/activity`, `/activity?kind=capped` |
| EF Core, Marten or in-memory | In-memory: `InMemoryPagingTests`. EF Core and Marten: their adapter changes. |
| A new LINQ provider | `QueryablePageSourceTests` (a mocked `IQueryExecutor`) |
| Builder defect | `InMemoryPageSourceContractTests` (the in-memory source runs the shared builder through every case) |
| Seek and order disagree | `BrokenFixtureTests.ASeekIgnoringTheTieBreaker_FailsOnlyTheFullTraversal_NamingTheRow` |
| Adapter author | Repository shape: `dotnet pack` produces only `Zooper.Ant` (task 8.1); test helpers live in `Zooper.Ant.Tests`. |
