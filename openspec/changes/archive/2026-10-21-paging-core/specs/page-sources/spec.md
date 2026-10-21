# Spec Delta

## Purpose

Defines the contract between the paging core and a store: what a store must provide to be paged, what it receives in return, and how a third-party store proves it conforms.

## ADDED Requirements

### Requirement: A store is paged through one small contract

A store SHALL be paged by implementing one source contract over one ordered, filtered result set: count the rows, read a range of rows by offset and limit, read rows strictly after a position, give the position of a served row, and read a position back from its serialised form. Nothing else SHALL be required of a store for the core to serve it.

#### Scenario: A new store with no shipped adapter

- **WHEN** a developer implements the source contract over a store the package does not ship an adapter for
- **THEN** the core serves every page kind from it with no change to the core

#### Scenario: One source, one result set

- **WHEN** a source is created for a filtered read
- **THEN** every call on it (count, read, read after) refers to the same filtered result set and the same order

### Requirement: The core, not the store, implements the page kinds

The kind switch, the clamping of sizes, the issuing of a next token, the refusal of a foreign or tampered token and the reading of a position from a token SHALL be implemented once in the core. A source SHALL NOT be told which kind is being served and SHALL NOT handle a token.

#### Scenario: Counted kind

- **WHEN** a counted page is requested from any source
- **THEN** the core asks the source for the count and for one offset range, and builds the counted page

#### Scenario: Continuable kind

- **WHEN** a continuable page is requested with a token
- **THEN** the core opens the token, asks the source to parse the position and read after it, and issues the next token from the source's position of the last served row

#### Scenario: Capped kind

- **WHEN** a capped page is requested
- **THEN** the core asks the source for one more row than the cap and reports truncation from that

### Requirement: The position is the source's own

A position SHALL be an ordered list of serialisable values that the source defines and reads back. The core SHALL protect it, carry it, and hand it back unchanged; it SHALL NOT interpret the values.

#### Scenario: Keyset source

- **WHEN** a source orders by a chain
- **THEN** its position is the chain values of the served row, tie-breaker last

#### Scenario: Native-cursor source

- **WHEN** a source wraps a store that issues its own continuation (for example a key-value store's query API)
- **THEN** its position is that native continuation as one value, and the core issues a protected token around it

### Requirement: A source that cannot count or offset is served continuable-only

A source MAY declare that it cannot count or read by offset. A counted request against such a source SHALL be refused with a paging error that names the kind as unavailable on this read. Continuable and capped requests SHALL be served.

#### Scenario: Counted on a native-cursor source

- **WHEN** a counted page is requested from a source that cannot count
- **THEN** the system refuses with a paging error naming the counted kind as unavailable

#### Scenario: Continuable on the same source

- **WHEN** a continuable page is requested from that source
- **THEN** the system serves it

### Requirement: LINQ-queryable stores need only an executor

For a store that exposes its data as a LINQ queryable, the package SHALL provide the source: order and seek are built from the declared chain as an expression the provider translates. The store adapter SHALL supply only how to execute a query to a list and a count asynchronously. The consumer SHALL select the adapter by importing it and SHALL NOT see the executor.

#### Scenario: EF Core, Marten or in-memory

- **WHEN** a consumer ends a LINQ query with the page call and the adapter's namespace is imported
- **THEN** the page is served with that adapter's executor and no further code

#### Scenario: A new LINQ provider

- **WHEN** a developer writes an executor for a LINQ provider the package does not ship
- **THEN** every kind and every ordering guarantee applies without reimplementing the seek

### Requirement: The in-memory source is the reference implementation

The in-memory source SHALL be the package's reference implementation of the contract and SHALL use the same LINQ order-and-seek builder a database provider receives, executed in memory. The package's own tests SHALL verify it for every kind, mixed directions, a null bookmark, reversal, token refusals, the depth bound, timestamp truncation, and a full traversal that sees every row exactly once.

#### Scenario: Builder defect

- **WHEN** the shared order-and-seek builder has a defect
- **THEN** the in-memory source's tests fail before any database adapter is run

#### Scenario: Seek and order disagree

- **WHEN** a source orders rows one way and seeks another
- **THEN** the full-traversal check fails, naming the row skipped or repeated

### Requirement: No test suite is shipped

The package SHALL ship no test package or test-only protector. An adapter author SHALL test their own adapter.

#### Scenario: Adapter author

- **WHEN** a developer writes an adapter for a store
- **THEN** they depend only on `Zooper.Ant` and write their own tests
