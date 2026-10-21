# continuation-tokens Specification

## Purpose

Defines the continuation token a continuable read issues and accepts: an opaque, protected bookmark in the read's ordering over live data, bound to the read and its result-defining arguments, and never expiring by age.

## Requirements

### Requirement: A continuable read is navigated by an opaque token

A continuable read SHALL take an optional token and a size, and SHALL answer with the items, the size used and a next token when more exists. A request without a token SHALL start the read. The absence of a next token SHALL be the only end signal. The caller SHALL pass the token back unchanged and SHALL NOT interpret it; the service MAY change the token's contents at any time.

#### Scenario: First request

- **WHEN** a caller requests a continuable page without a token
- **THEN** the system serves the first page and, if more items match, a next token

#### Scenario: Continuation

- **WHEN** a caller resubmits the same read with the previous response's next token
- **THEN** the system serves the items that follow the last item of the previous page

#### Scenario: End of the read

- **WHEN** a caller receives a page without a next token
- **THEN** no further page exists, regardless of how many items the page carries

#### Scenario: One token form for every host

- **WHEN** the same read is reached from an HTTP endpoint and from code inside the service
- **THEN** both receive and return the same token form, never a URL or a page number as continuation

### Requirement: A token is a bookmark in the ordering over live data

A token SHALL identify the position of the last served item in the read's ordering, including the unique tie-breaker. Continuation SHALL select the items ordered after that position at the time of the request. The system SHALL hold no snapshot, session or server-side state for a token.

#### Scenario: An earlier item is deleted

- **WHEN** a caller holds a token after an identifier-ordered page ending at identifier 30, and identifier 10 is erased before continuation
- **THEN** continuation starts after identifier 30 and no item is skipped

#### Scenario: Continuation needs no offset

- **WHEN** a caller continues a read past the counted depth bound in items
- **THEN** the system serves the page, because no offset is involved

### Requirement: A token continues the same read only

A token SHALL be valid only for the read that issued it. A token presented to a different read SHALL be refused as an invalid token, even when both reads share item type and ordering.

#### Scenario: Same read

- **WHEN** a caller continues a read with a token that read issued
- **THEN** the system serves the next page

#### Scenario: Foreign read

- **WHEN** a caller presents a token issued by one read to another read
- **THEN** the system refuses with an invalid-token error and serves no page

### Requirement: A token continues the same arguments only

A token SHALL be valid only with the same result-defining arguments as the request that issued it: filters, identifier sets, search terms, locations, and the ordering itself. The page size MAY change between continuations within the bounds. A continuation whose arguments differ SHALL be refused as an invalid token and SHALL NOT be served as a new read.

#### Scenario: Same criteria

- **WHEN** a caller continues with the token and the same filters and ordering
- **THEN** the system serves the next page

#### Scenario: Changed criteria

- **WHEN** a caller continues with a token issued for one search term and supplies a different term
- **THEN** the system refuses with an invalid-token error and serves no page

#### Scenario: Changed ordering

- **WHEN** a caller continues with a token issued under one ordering and the read now orders differently
- **THEN** the system refuses with an invalid-token error

#### Scenario: Changed size

- **WHEN** a caller continues with a token and a different size within the bounds
- **THEN** the system serves the next page at the new size

### Requirement: The service, not the read, binds a token to its arguments

The binding of a token to its read and arguments SHALL be computed by the paging system from the read's name and the values the read declares as result-defining, in a form independent of culture and of argument formatting. A read SHALL NOT hand-build the binding, and the declared ordering SHALL be included without the read listing it.

#### Scenario: Culture does not matter

- **WHEN** a token is issued under one thread culture and returned under another
- **THEN** the binding matches and continuation succeeds

#### Scenario: A declared value changes

- **WHEN** a read declares a date filter as result-defining and the date differs on continuation
- **THEN** the system refuses with an invalid-token error

### Requirement: A token is protected against reading and tampering

The payload behind a token SHALL be encrypted and integrity-protected before issue and verified on return. A token that was modified, was issued under another purpose, or cannot be read with the available keys SHALL be refused as an invalid token. Protection SHALL remain valid across restarts and between instances that share the same keys.

#### Scenario: Round trip

- **WHEN** a caller returns a token exactly as issued
- **THEN** the system recovers the position and serves the next page

#### Scenario: Tampered token

- **WHEN** a caller returns a token whose content was altered
- **THEN** the system refuses with an invalid-token error and serves no page

#### Scenario: Token issued before a restart

- **WHEN** the service restarts between issuing a token and its return, with the same keys available
- **THEN** the token is still readable and continuation succeeds

#### Scenario: A host swaps the protection mechanism

- **WHEN** a host replaces the default protection with its own implementation of the protection contract
- **THEN** every read issues and accepts tokens through that implementation with no change to the reads

### Requirement: A token value is validated structurally at the boundary

A supplied token SHALL be non-blank and consist only of URL-safe base64 characters. A blank or malformed value SHALL be refused before any read runs. Structural validity SHALL NOT imply that the protected content is valid. Absence SHALL be expressed by omitting the token, never by an empty value.

#### Scenario: Blank token

- **WHEN** a caller supplies an empty or whitespace token
- **THEN** the system refuses with a paging error

#### Scenario: Well-formed but invalid content

- **WHEN** a caller supplies a URL-safe string the system never issued
- **THEN** the structural check passes and the protected-content check refuses with an invalid-token error

### Requirement: A token whose payload no longer fits the read is refused

A token that decrypts and matches the binding but whose position does not have the shape the read's ordering expects (wrong number of values, a value of the wrong type) SHALL be refused as an invalid token, not served and not raised as an internal error.

#### Scenario: Ordering gained a key

- **WHEN** a read's ordering gains a key after a token was issued, and the binding still matches
- **THEN** the system refuses the token as invalid

#### Scenario: Value of the wrong type

- **WHEN** a position value cannot be read as the type the ordering declares
- **THEN** the system refuses the token as invalid

### Requirement: A token does not expire by age

A token SHALL remain usable regardless of the time elapsed since it was issued. The system SHALL apply no lifetime, expiry timestamp or age limit. Every continuation SHALL read live data at the time of the request.

#### Scenario: An old token

- **WHEN** a caller continues with a token issued long before
- **THEN** the system serves the next page from current data, provided the token passes the integrity and same-arguments checks

#### Scenario: Age is not a refusal reason

- **WHEN** a continuation is refused
- **THEN** the reason is integrity, a foreign read, changed arguments or a payload that no longer fits, never elapsed time

### Requirement: An invalid token is a distinct paging error

An invalid-token refusal SHALL be raised as a specialisation of the paging error so a host can map it to the same response as any other paging refusal, or distinguish it when it wants to.

#### Scenario: Host maps both alike

- **WHEN** a host handles the paging error type
- **THEN** an invalid token is handled by the same handler without extra code

#### Scenario: Host distinguishes

- **WHEN** a host handles the invalid-token error type specifically
- **THEN** only token refusals reach that handler
