# Spec Delta

## Purpose

Lets an HTTP host answer every paging refusal with one standard 400 problem response that a client can branch on, without displacing the host's own error handling.

## ADDED Requirements

### Requirement: A paging refusal is answered with a 400 problem response

When the opt-in handler is registered, every paging refusal raised while serving a request SHALL be answered with HTTP 400 and an RFC 9457 problem-details body whose detail is the refusal's message and whose title names the refusal class. No paging refusal SHALL reach the client as a 500.

#### Scenario: Foreign argument

- **WHEN** a request carries `?paging=capped&cap=5&page=2` and the handler is registered
- **THEN** the response is 400 with title `Invalid paging` and a detail that names `page` and capped paging

#### Scenario: Depth refusal

- **WHEN** a counted request exceeds the host's depth bound and the handler is registered
- **THEN** the response is 400 with title `Invalid paging` and the core's depth message as detail

#### Scenario: Invalid token

- **WHEN** a request carries a token that the read refuses and the handler is registered
- **THEN** the response is 400 with title `Invalid page token` and the core's fixed token message as detail

### Requirement: The problem type identifies the refusal class with a stable URN

The problem body's `type` SHALL be `urn:zooper:ant:invalid-page-token` for an invalid-token refusal and `urn:zooper:ant:invalid-paging` for every other paging refusal. The URNs SHALL be fixed; the handler SHALL carry no other machine-readable code and SHALL offer no option to change the URNs.

#### Scenario: Client restarts on a stale token

- **WHEN** a client receives a problem whose `type` is `urn:zooper:ant:invalid-page-token`
- **THEN** it can restart from the first page without parsing the English title or detail

#### Scenario: Host needs its own error convention

- **WHEN** a host must emit its own error code field in every problem response
- **THEN** the host keeps its own handler for paging refusals and does not register this one

### Requirement: The handler is opt-in and handles only paging refusals

The handler SHALL be registered only by an explicit call from the host; no other registration SHALL add it. It SHALL decline every exception that is not a paging refusal, leaving it to the host's remaining handlers.

#### Scenario: Host with its own global handler

- **WHEN** a host takes the binding and the OpenAPI description but never calls the handler registration
- **THEN** no paging handler is active and the host's own handler answers paging refusals

#### Scenario: An unrelated exception

- **WHEN** an endpoint throws an exception that is not a paging refusal and the handler is registered
- **THEN** the handler declines it and the response is whatever the host's other handlers produce
