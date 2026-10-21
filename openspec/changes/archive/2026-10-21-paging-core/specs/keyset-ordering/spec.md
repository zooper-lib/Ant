# Spec Delta

## Purpose

Defines how a read declares its order once and what seeking over that order guarantees, so that continuation never skips or repeats a row and the order and the seek can never disagree.

## ADDED Requirements

### Requirement: An order is declared once as a chain ending in a unique tie-breaker

A read SHALL declare its order as an ordered chain of keys, each ascending or descending. The last key SHALL be unique across the result set; it is the tie-breaker. The chain SHALL be the only place the order is stated: both the order the store applies and the seek used for continuation SHALL be derived from it.

#### Scenario: Order and seek come from one declaration

- **WHEN** a read declares a chain and serves a continuable page
- **THEN** the items are ordered by the chain and the next page starts strictly after the last item under the same chain

#### Scenario: Equal sort values across a page boundary

- **WHEN** many items share the value of the first key and a page boundary falls among them
- **THEN** each item is served exactly once across the pages, because the tie-breaker orders them

### Requirement: Keys are declared in the natural form of the query technology

For a read expressed as a LINQ query, a key SHALL be a typed member or computed expression over the item type; its name and value type SHALL be inferred, not restated. For a read expressed as SQL text, a key SHALL be a column or SQL expression with its value type stated. Neither form SHALL require the other's notation.

#### Scenario: Typed LINQ key

- **WHEN** a read declares a key as a lambda over a property
- **THEN** no string name or type is supplied and a renamed property is a compile-time change

#### Scenario: Computed SQL key

- **WHEN** a read declares a key as a SQL expression such as a rounded score
- **THEN** the same expression orders the rows and seeks past the last served value

### Requirement: Continuation is keyset, never offset

Continuation SHALL select rows strictly after the position of the last served item by comparing the chain's values in chain order: a row follows the position when its first key is beyond the position's, or equal and its second key beyond, and so on through the tie-breaker. No continuation SHALL skip a number of rows.

#### Scenario: Three-key seek

- **WHEN** the chain is (created ascending, score descending, id ascending) and the position is (T, S, I)
- **THEN** the next page contains rows with created > T, or created = T and score < S, or created = T and score = S and id > I, in chain order

#### Scenario: Deleted rows do not shift the page

- **WHEN** rows before the position are deleted between two pages
- **THEN** the next page is unchanged

### Requirement: Mixed directions are supported

A chain MAY mix ascending and descending keys. "Beyond" for a descending key SHALL mean less than, and the seek SHALL honour each key's own direction.

#### Scenario: Descending first key

- **WHEN** the chain is (score descending, id ascending)
- **THEN** the next page starts at rows with a lower score, or the same score and a higher id

### Requirement: Nulls sort last by default and a null bookmark seeks correctly

A nullable key SHALL place null values after every non-null value by default, in both directions, with an explicit per-key option to place them first. When the position's value for a nulls-last key is null, continuation SHALL seek only among rows whose value is also null, compared on the remaining keys; no non-null row SHALL follow a null bookmark on that key.

#### Scenario: Non-null bookmark on a nullable key

- **WHEN** the position holds a non-null value for a nulls-last key
- **THEN** the next page may contain rows with greater values and then rows with null values

#### Scenario: Null bookmark

- **WHEN** the position holds a null value for a nulls-last key
- **THEN** the next page contains only rows whose value is null, ordered by the remaining keys after the bookmark

### Requirement: A timestamp bookmark is truncated to the store's precision

When a key's stored precision is coarser than the value's in-memory precision, the position SHALL carry the value truncated to the stored precision, so an item whose stored value equals the bookmark is never placed on the wrong side of it. The default rule for date and time keys SHALL be microsecond truncation.

#### Scenario: Microsecond truncation

- **WHEN** two items share a stored timestamp that differs from the in-memory value only below a microsecond
- **THEN** continuation after the first item serves the second item exactly once

### Requirement: A chain with a changed shape invalidates earlier positions

A position SHALL be readable only against a chain with the same number of keys and compatible value types. A position issued under another shape SHALL be refused as an invalid token (see `continuation-tokens`).

#### Scenario: Key added to the chain

- **WHEN** a read's chain gains a key and a caller continues with a position issued before the change
- **THEN** the position is refused as invalid

### Requirement: An ordering can be reversed without a second declaration

A read MAY serve the declared chain reversed as a whole. Reversal SHALL flip every key's direction and nulls placement together and SHALL keep the seek consistent with the reversed order.

#### Scenario: Reversed chain

- **WHEN** a read serves the chain (created ascending, id ascending) reversed
- **THEN** the items come in (created descending, id descending) order and continuation seeks under that order
