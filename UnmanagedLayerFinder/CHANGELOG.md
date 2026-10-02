# Changelog

All notable changes to Unmanaged Layer Finder. Versions follow [Semantic Versioning](https://semver.org/).

## [1.3.0] - 2026-10-02
### Added
- Component type filter with per-type totals and unmanaged counts.
- "Select all" and "Check layers" respect the component type filter.

## [1.2.0] - 2026-10-02
### Added
- Process categories: Cloud Flow, Classic Workflow, Business Rule, Action, Business Process Flow, Desktop Flow.
- Environment Variable Definition and Value shown separately, with the current value / default value in a Details column.
- Support for org-specific component types (connection references and others) via `solutioncomponentdefinition`.
- Modified by / on for environment variables, canvas apps and connectors.
### Fixed
- Placeholder layer time (1900-01-01) is no longer shown as a real date.

## [1.1.0] - 2026-10-02
### Added
- Solution filters: managed / unmanaged / both, publisher multi-select, exclude Microsoft solutions.
- Solution list shows version, managed state and publisher.

## [1.0.2] - 2026-10-02
### Changed
- Solutions load automatically when the tool opens or the connection changes.

## [1.0.0] - 2026-10-02
### Added
- First version: load a solution's components, detect unmanaged (Active) layers, show layer stack, changed attributes, modified by, optional audit lookup, CSV export.
