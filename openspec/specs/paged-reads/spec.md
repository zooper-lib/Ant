# paged-reads Specification

## Purpose

Lets the caller of any list read choose, per request, one of three page kinds, and makes the service bound, order and answer every kind the same way regardless of the store behind it.

## Requirements

### Requirement: Three page kinds are distinguished by what a client can render

The system SHALL offer exactly three page kinds, each a generic container over an item type: a counted page (page number, size, total count, items), a continuable page (size, next token when more exists, items) and a capped page (cap, truncated flag, items). The caller SHALL choose one per request. No kind SHALL be a bare wrapper around a list.

#### Scenario: A client needs a total and arbitrary page jumps

- **WHEN** a request asks for a counted page
- **THEN** the result carries the page number, the size used, the total number of matching items and the items

#### Scenario: A client needs only "load more"

- **WHEN** a request asks for a continuable page
- **THEN** the result carries the size used, the items, and a next token only when a further page exists

#### Scenario: A client needs one screen of best results

- **WHEN** a request asks for a capped page
- **THEN** the result carries the cap used, the items, and whether more items matched than the cap

### Requirement: A continuable page reports whether more exists, not how many matched

A continuable page SHALL report that a further page exists only by carrying a next token. It SHALL NOT carry a total count or a separate boolean, and the next token SHALL be determined without evaluating the full matching set.

#### Scenario: Further page exists

- **WHEN** more items match than fit on the requested continuable page
- **THEN** the result carries a next token and exactly the requested page of items

#### Scenario: No further page

- **WHEN** the requested continuable page is the last one
- **THEN** the result carries no next token

### Requirement: The service bounds page size and clamps rather than trusting the caller

Page size and cap SHALL be bounded by a configured default size and maximum size, with defaults of 20 and 50. A missing or non-positive size SHALL become the default. A size or cap above the maximum SHALL be clamped to the maximum and served without error, because a smaller page is still a correct answer. The bounds SHALL be applied once, where the paging instruction is built, so every caller is bounded alike.

#### Scenario: No size supplied

- **WHEN** a caller requests a page without a size
- **THEN** the system returns at most the default size of items and reports the size it used

#### Scenario: Oversized page requested

- **WHEN** a caller requests a page size above the maximum
- **THEN** the system returns at most the maximum, reports the maximum as the size, and raises no error

#### Scenario: Oversized cap requested

- **WHEN** a caller requests a capped page with a cap above the maximum
- **THEN** the system returns at most the maximum, reports the maximum as the cap, and raises no error

#### Scenario: A non-web caller is bounded alike

- **WHEN** code inside the service builds a paging instruction with a size above the maximum
- **THEN** the instruction carries the maximum before any store is asked

### Requirement: Counted depth is bounded and exceeding it is an error

A counted read SHALL refuse a request whose page number multiplied by its size exceeds the configured maximum depth, default 1000, and SHALL refuse a page number below one. Depth SHALL NOT be clamped or normalised, because serving a different page under the requested number is a wrong answer presented as a right one. Continuable and capped reads have no offset and SHALL NOT be subject to the depth bound.

#### Scenario: Depth within the bound

- **WHEN** a caller requests counted page 50 at size 20 under the default bounds
- **THEN** the system serves the page

#### Scenario: Depth beyond the bound

- **WHEN** a caller requests counted page 51 at size 20 under the default bounds
- **THEN** the system refuses with a paging error naming the depth bound, and no page is served

#### Scenario: Page below one

- **WHEN** a caller requests counted page 0 or a negative page
- **THEN** the system refuses with a paging error, and no page is served as page 1

#### Scenario: Continuation beyond the depth

- **WHEN** a caller continues a continuable read by token past the depth bound in items
- **THEN** the system serves the page

### Requirement: Bounds are configuration validated at startup

The default size, maximum size and maximum depth SHALL be configurable per host. The system SHALL refuse at startup a configuration where the default size is below one, the default size exceeds the maximum size, or the maximum depth is below the maximum size.

#### Scenario: Valid custom bounds

- **WHEN** a host configures default 25, maximum 100, depth 5000
- **THEN** every paging instruction in that host is bounded by those values

#### Scenario: Default above maximum

- **WHEN** a host configures default 60 and maximum 50
- **THEN** startup fails with an error naming the invalid bound

### Requirement: The continuable kind is the default

When a caller does not name a kind, the paging instruction SHALL be continuable. The caller SHALL name the counted or capped kind explicitly to receive it.

#### Scenario: No kind named

- **WHEN** a caller supplies only a size
- **THEN** the system serves a continuable page

#### Scenario: Counted named

- **WHEN** a caller names the counted kind with a page number
- **THEN** the system serves a counted page

### Requirement: Paging arguments must belong to the requested kind

A paging instruction SHALL carry only the arguments of its own kind: a token for continuable, a page number for counted, a cap for capped. A host that receives an argument of another kind SHALL refuse the request rather than ignore the argument, because a silently ignored argument is a wrong answer presented as a right one.

#### Scenario: A page number on a capped request

- **WHEN** a caller names the capped kind and also supplies a page number
- **THEN** the system refuses the request and serves no page

#### Scenario: A token on a counted request

- **WHEN** a caller names the counted kind and also supplies a token
- **THEN** the system refuses the request and serves no page

### Requirement: Every page kind is served by one read over one total order

One read SHALL answer all three kinds over the same filtered result set and the same total order (see `keyset-ordering`). A consumer SHALL be able to map the items of a page to another type without knowing its kind, and the mapped page SHALL keep its kind and its page-level values.

#### Scenario: Same order across kinds

- **WHEN** the same filtered read is requested as counted page 1, as the first continuable page and as a capped page of equal size
- **THEN** the three results contain the same items in the same sequence

#### Scenario: Mapping keeps the kind

- **WHEN** a consumer maps a counted page's items to a view type
- **THEN** the result is a counted page with the same page number, size and total count

### Requirement: A paging refusal is one distinguishable error

Every paging refusal (a page below one, depth beyond the bound, foreign arguments, an invalid token) SHALL be raised as one paging error type that a host can recognise and map to its own response, with a message naming the bound or argument refused. No read SHALL raise a paging refusal of its own shape.

#### Scenario: The same refusal on every read

- **WHEN** the same invalid paging arguments reach two different reads
- **THEN** both raise the same paging error type with a message naming the same bound

#### Scenario: A host maps the error

- **WHEN** a host catches the paging error type
- **THEN** it can distinguish it from any other failure of the read without inspecting the message
