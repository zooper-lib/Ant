# http-paging-binding Specification

## Purpose

Lets an HTTP host receive the paging arguments of a list request through one fixed set of query parameters, the same in MVC controllers and Minimal APIs, and turn them into a bounded paging instruction without re-implementing the kind rules.

## Requirements

### Requirement: Five fixed query parameters form the wire contract

An HTTP host SHALL receive paging arguments through exactly five query parameters: `paging` (the kind), `pageToken`, `page`, `size` and `cap`. The names SHALL be fixed; the host SHALL NOT be offered a way to rename them. Every Ant host SHALL therefore speak the same five parameters.

#### Scenario: A continuable request

- **WHEN** a list request carries `?paging=continuable&size=20&pageToken=abc`
- **THEN** the host binds a continuable instruction with size 20 continuing after token `abc`

#### Scenario: A counted request

- **WHEN** a list request carries `?paging=counted&page=3&size=10`
- **THEN** the host binds a counted instruction for page 3 of size 10

#### Scenario: A capped request

- **WHEN** a list request carries `?paging=capped&cap=5`
- **THEN** the host binds a capped instruction with cap 5

#### Scenario: A host wants other names

- **WHEN** a host's public API already uses different parameter names
- **THEN** the host writes its own binding against the core's paging factories; the shipped binding is not reconfigured

### Requirement: The kind defaults to continuable

When `paging` is absent, the host SHALL bind a continuable instruction. The kind value SHALL be matched without regard to letter case, and a value that is not one of the three kinds SHALL be refused before the endpoint runs.

#### Scenario: No kind named

- **WHEN** a list request carries `?size=20` and no `paging`
- **THEN** the host binds a continuable instruction with size 20 and no token

#### Scenario: Kind in another case

- **WHEN** a list request carries `?paging=Counted&page=1`
- **THEN** the host binds a counted instruction

#### Scenario: Unknown kind

- **WHEN** a list request carries `?paging=infinite`
- **THEN** the request is answered with 400 and the endpoint does not run

### Requirement: Each kind accepts only its own arguments

The host SHALL refuse a request that carries an argument of another kind, with a paging refusal naming the first foreign argument and the kind: continuable refuses `page` and `cap`; counted refuses `pageToken` and `cap`; capped refuses `page`, `size` and `pageToken`. The host SHALL NOT silently ignore the argument.

#### Scenario: A page number on a continuable request

- **WHEN** a list request carries `?page=2&size=20` with no kind, or with `paging=continuable`
- **THEN** the request is refused with a paging refusal that names `page` and continuable paging

#### Scenario: A token on a counted request

- **WHEN** a list request carries `?paging=counted&page=1&pageToken=abc`
- **THEN** the request is refused with a paging refusal that names `pageToken` and counted paging

#### Scenario: A size on a capped request

- **WHEN** a list request carries `?paging=capped&cap=5&size=20`
- **THEN** the request is refused with a paging refusal that names `size` and capped paging

### Requirement: Bounds come from the host's configured limits

The binding SHALL apply the host's configured limits (default size, maximum size, maximum depth) to every instruction it builds, through the core's factories. It SHALL NOT default, clamp or depth-check on its own, and it SHALL NOT fall back to the built-in default limits when the host configured others.

#### Scenario: Size above a custom maximum

- **WHEN** the host configured a maximum size of 100 and a request carries `?size=500`
- **THEN** the bound instruction has size 100, not the built-in maximum

#### Scenario: Size omitted under a custom default

- **WHEN** the host configured a default size of 25 and a request carries no `size`
- **THEN** the bound instruction has size 25

#### Scenario: Counted page beyond the depth bound

- **WHEN** the host configured a maximum depth of 1000 and a request carries `?paging=counted&page=200&size=10`
- **THEN** the request is refused with the core's depth refusal, unchanged by the binding

### Requirement: A malformed token is refused before the endpoint runs

A `pageToken` value that is blank or not base64url text SHALL fail binding with a 400 before the endpoint runs. A structurally valid token SHALL be bound as-is; whether it is genuine is decided later by the paged read, not by the binding.

#### Scenario: Token with padding characters

- **WHEN** a list request carries `?pageToken=abc==`
- **THEN** the request is answered with 400 and the endpoint does not run

#### Scenario: Structurally valid but foreign token

- **WHEN** a list request carries a base64url token issued by another read
- **THEN** binding succeeds and the paged read raises the invalid-token refusal

### Requirement: The same binding serves controllers and Minimal APIs

The binding SHALL work as an action parameter in an MVC controller and as an endpoint parameter in a Minimal API, with identical parameter names, defaults and refusals. A host SHALL declare its own filter arguments separately, beside the paging arguments.

#### Scenario: Controller endpoint

- **WHEN** a controller action declares the paging arguments as a query-bound parameter beside its filter parameters
- **THEN** `?paging=counted&page=2&size=10` binds a counted instruction for page 2

#### Scenario: Minimal API endpoint

- **WHEN** a Minimal API endpoint declares the paging arguments as a parameter beside its filter parameters
- **THEN** `?paging=counted&page=2&size=10` binds a counted instruction for page 2

#### Scenario: API explorer lists the parameters

- **WHEN** the host generates an API description for a list endpoint that uses the binding
- **THEN** the description lists `paging`, `pageToken`, `page`, `size` and `cap` as optional query parameters
