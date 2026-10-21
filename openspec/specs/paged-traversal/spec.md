# paged-traversal Specification

## Purpose

Defines how a consumer that needs every matching item of a paged read consumes it: one shared traversal that follows continuation tokens page by page, yields items incrementally, and reads live data without a snapshot or checkpoint.

## Requirements

### Requirement: Every item of a paged read is consumed through one shared traversal

The system SHALL offer one traversal that repeatedly invokes a caller-supplied page read with continuable paging and exposes the items as an asynchronous sequence. The caller supplies how to read a page for a given token; the traversal SHALL start without a token, follow each next token and stop when none is returned. The traversal SHALL NOT know any message bus, result wrapper or host; the caller adapts those.

#### Scenario: Multi-page consumption

- **WHEN** a consumer traverses a read matching three pages of items
- **THEN** it receives every item of all three pages in order, and the page read is invoked three times

#### Scenario: Empty read

- **WHEN** a consumer traverses a read matching nothing
- **THEN** the sequence ends with no item after one invocation

#### Scenario: Items arrive incrementally

- **WHEN** a consumer processes the first item
- **THEN** later pages have not yet been fetched, and the full result is never held in memory

#### Scenario: Read arguments are preserved

- **WHEN** the traversal invokes the page read for a later page
- **THEN** only the token differs from the first invocation; the caller's filters and ordering are untouched

### Requirement: Only a continuable page can be traversed

The traversal SHALL require each page read to return a continuable page. A counted or capped page returned to the traversal SHALL end it with an error that names the misuse, because those kinds carry no token to follow.

#### Scenario: A counted page is returned

- **WHEN** the caller's page read returns a counted page
- **THEN** the traversal raises an invalid-operation error and requests no further page

### Requirement: A failed fetch ends the traversal with the read's own error

When a page read fails, the traversal SHALL surface that failure once, unchanged, and end. It SHALL NOT wrap or rename the error, SHALL NOT continue to a further page, and SHALL NOT present the items yielded so far as a complete result. Cancellation and early disposal SHALL follow the platform's asynchronous-stream behaviour.

#### Scenario: A later page fails

- **WHEN** the second of three pages fails with a storage error
- **THEN** the consumer receives the first page's items, then that storage error once, and no third page is requested

#### Scenario: Cancellation

- **WHEN** the consumer cancels during traversal
- **THEN** the pending page read observes the cancellation and no further page is requested

#### Scenario: Early disposal

- **WHEN** the consumer stops enumerating before the end
- **THEN** no further page is requested

### Requirement: Traversal reads live data without a snapshot

Each page SHALL be read from the data current at the time of that fetch. The traversal SHALL NOT freeze an inventory, record a starting boundary or hold a session. An item already yielded MAY change before the traversal ends.

#### Scenario: An earlier item is deleted mid-traversal

- **WHEN** an item behind the bookmark is erased between two fetches
- **THEN** the next fetch starts after the bookmark and the next surviving item is not skipped

#### Scenario: A matching row is inserted ahead of the bookmark

- **WHEN** a row matching the criteria is inserted ahead of the current position during traversal
- **THEN** a later fetch may return it

#### Scenario: A matching row is inserted behind the bookmark

- **WHEN** a row matching the criteria is inserted behind the current position during traversal
- **THEN** this traversal does not return it, and a fresh traversal does

### Requirement: A changed ordering value may omit or repeat an item

When the read is ordered by a value that can change, an item whose value moves behind the bookmark during traversal MAY be omitted, and one whose value moves ahead MAY be returned again. The traversal SHALL keep the caller's ordering and SHALL NOT substitute another. A consumer that needs neither effect SHALL order by an immutable identifier.

#### Scenario: Renamed behind the bookmark

- **WHEN** a name-ordered traversal has passed M and an unread item's name changes from Zoe to Amy
- **THEN** that item may be omitted from this traversal, and this is not a defect

#### Scenario: Renamed ahead of the bookmark

- **WHEN** an item already returned as Ada is renamed to Zara during a name-ordered traversal
- **THEN** it may be returned again, and this is not a defect

#### Scenario: Identifier ordering is stable

- **WHEN** a consumer traverses with identifier ordering and names change meanwhile
- **THEN** no item is omitted or repeated because of the name change

### Requirement: A failed traversal restarts; consumers tolerate repeats

After a failed traversal, a later attempt SHALL start again without a token. The traversal SHALL keep no processing checkpoint and SHALL NOT undo work a consumer completed before the failure. The traversal SHALL NOT retry, buffer or prefetch on its own.

#### Scenario: Restart after failure

- **WHEN** a synchronisation pass fails on a later page and a new pass starts
- **THEN** the new pass begins at the first page and items already processed are delivered again

#### Scenario: No automatic retry

- **WHEN** a fetch fails
- **THEN** the traversal does not re-invoke the failed page read on its own
