# openapi-page-description Specification

## Purpose

Lets API consumers see the three concrete shapes a paged response can take, which an OpenAPI generator cannot infer from the abstract page type on its own.

## Requirements

### Requirement: A paged response is described as one of the three page shapes

When the opt-in transformer is registered, an operation whose successful response is declared as the abstract page type SHALL be described as `oneOf` the continuable, counted and capped page schemas for the same item type, in every media type of that response.

#### Scenario: List endpoint returning a page of players

- **WHEN** an endpoint declares its 200 response as a page of `Player` and the transformer is registered
- **THEN** the generated 200 schema is `oneOf` continuable page of `Player`, counted page of `Player` and capped page of `Player`

#### Scenario: Item schema is shared

- **WHEN** two endpoints return pages of the same item type
- **THEN** both descriptions reference the same three page schemas rather than duplicating them

### Requirement: Every other response is left untouched

The transformer SHALL leave unchanged any operation whose successful response is not the abstract page type, including responses declared as one of the concrete page types, and any non-200 response of a paged operation.

#### Scenario: Endpoint returning a concrete page type

- **WHEN** an endpoint declares its 200 response as a continuable page of `Player`
- **THEN** its schema is the generator's own, not a `oneOf`

#### Scenario: Endpoint returning a plain object

- **WHEN** an endpoint declares its 200 response as `Player`
- **THEN** its schema is unchanged

#### Scenario: Error response of a paged operation

- **WHEN** a paged operation also declares a 400 problem response
- **THEN** the 400 schema is unchanged

### Requirement: Registration is a single call inside the OpenAPI setup

The transformer SHALL be added by one call on the host's OpenAPI options, independent of the error handler registration, so a host can take either without the other.

#### Scenario: Host wants documentation only

- **WHEN** a host adds the transformer and never registers the error handler
- **THEN** paged responses are described and the host's own error handling is unchanged
