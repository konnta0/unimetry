# Changelog

Notable changes to Unimetry are documented in this file.

## [Unreleased]

## [0.8.1] - 2026-11-01

### Changed

- Repeated resource captures, updates to an existing metric series, and OTLP JSON export no longer allocate on the steady-state path. An event still keeps the resource identity captured when it was recorded.

## [0.8.0] - 2026-10-30

### Added

- Added `UnimetryOptions.CaptureBuiltInMetrics` and the matching `UnimetrySettings` field. It defaults to enabled; disabling it stops and omits the built-in FPS, managed memory, and startup gauges without disabling application metrics.

[Unreleased]: https://github.com/konnta0/unimetry/compare/v0.8.1...HEAD
[0.8.1]: https://github.com/konnta0/unimetry/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/konnta0/unimetry/compare/v0.7.0...v0.8.0
