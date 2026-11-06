# Changelog

Notable changes to Unimetry are documented in this file.

## [Unreleased]

## [0.9.1] - 2026-11-06

### Changed

- Exported `unimetry.fingerprint` is now hashed from the sanitized exception type, message, and stack trace. The sent value does not match 0.9.0 or earlier when a sanitizer rewrites that text.

### Fixed

- Native crashes use the same sanitized fingerprint. In-process deduplication still uses the pre-sanitizer value. A sanitizer fingerprint is kept only when it is non-empty and different from the fingerprint it received, so copying the input fingerprint does not preserve a machine-specific hash.

## [0.9.0] - 2026-11-06

### Added

- `EventScope.SetSeverity`, `EventHandle.SetSeverity`, and `UnimetryEvent.Write(name, severity)` set an event's OpenTelemetry severity without recording an exception. Omitting them still records `Info`. A recorded exception stays at `Error` or higher.

## [0.8.1] - 2026-11-01

### Changed

- Repeated resource captures, updates to an existing metric series, and OTLP JSON export no longer allocate on the steady-state path. An event still keeps the resource identity captured when it was recorded.

## [0.8.0] - 2026-10-30

### Added

- Added `UnimetryOptions.CaptureBuiltInMetrics` and the matching `UnimetrySettings` field. It defaults to enabled; disabling it stops and omits the built-in FPS, managed memory, and startup gauges without disabling application metrics.

[Unreleased]: https://github.com/konnta0/unimetry/compare/v0.9.1...HEAD
[0.9.1]: https://github.com/konnta0/unimetry/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/konnta0/unimetry/compare/v0.8.1...v0.9.0
[0.8.1]: https://github.com/konnta0/unimetry/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/konnta0/unimetry/compare/v0.7.0...v0.8.0
