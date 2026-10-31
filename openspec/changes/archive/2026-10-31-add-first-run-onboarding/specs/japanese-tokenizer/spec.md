## MODIFIED Requirements

### Requirement: Install a pinned dictionary asset
The system SHALL install the Doki-compatible `unidic-py` dictionary build `3.1.0+2021-08-31` from a fixed URL or a local offline archive. Both paths SHALL verify the expected SHA-256 from the checked-in manifest before making the dictionary available.

#### Scenario: First online installation
- **WHEN** the dictionary is not installed and a caller invokes online installation
- **THEN** the installer downloads the fixed dictionary URL without resolving a `latest` alias
- **AND** it verifies SHA-256, version, `unidic22` format, and the four required runtime files before atomically installing the dictionary under the local cache directory

#### Scenario: Application startup does not wait for installation
- **WHEN** the application starts without an installed default dictionary
- **THEN** the startup window remains responsive while installation runs through the first-run dictionary step, or through a separate preparation/repair surface for a user who has already completed onboarding
- **AND** installation failure is logged without producing an unobserved task exception

#### Scenario: Already installed
- **WHEN** the expected runtime files and installed manifest are already valid
- **THEN** the installer returns without downloading or replacing the dictionary

#### Scenario: Checksum or format mismatch
- **WHEN** a downloaded or offline archive does not match the expected SHA-256, version, or `unidic22` format
- **THEN** the installer throws a user-facing exception carrying the `UniDicDictionaryInvalid` error code
- **AND** the invalid archive is not promoted to the active dictionary directory

#### Scenario: Network or extraction failure
- **WHEN** download or extraction fails for an I/O reason
- **THEN** the installer throws a user-facing exception carrying the `UniDicDownloadFailed` error code
- **AND** partial temporary files are cleaned up so a retry can succeed

#### Scenario: Cancel installation
- **WHEN** installation is cancelled before the atomic promotion step
- **THEN** cancellation is propagated to the caller
- **AND** the previously installed dictionary, if any, remains usable
- **AND** staging files are cleaned up

#### Scenario: Install from an offline archive
- **WHEN** a caller selects a local archive matching the pinned manifest
- **THEN** the installer applies the same hash, version, format, file-set, staging, and atomic-promotion checks used by online installation
- **AND** installation succeeds without network access

#### Scenario: Concurrent installers
- **WHEN** multiple application processes attempt to install the same dictionary concurrently
- **THEN** only one process promotes the active dictionary directory at a time
- **AND** all processes observe either the previous valid installation or the completed new installation, never a partial directory

#### Scenario: Do not construct the recognition runtime before dictionary readiness
- **WHEN** a new installation is still downloading, validating or extracting the dictionary
- **THEN** the application keeps the onboarding shell responsive without resolving the tokenizer-dependent recognition object graph
- **AND** dictionary readiness alone does not bypass the remaining onboarding steps

## ADDED Requirements

### Requirement: Report dictionary installation phases accurately
The installer SHALL expose stable downloading, validating, extracting, installing and ready phases. Percentage progress SHALL describe downloaded bytes only when a valid total length is known. Validation, extraction, installation and unknown-length downloads SHALL use indeterminate progress rather than report installation completion. The ready state SHALL be published only after the pinned asset checks and atomic promotion succeed. Heavy file work SHALL NOT block the UI message loop. Failure and cancellation SHALL preserve the existing cleanup, error-code and retry contracts; retries SHALL NOT claim byte-range resume support.

#### Scenario: Finish downloading an archive
- **WHEN** all archive bytes have been received but checksum validation or extraction is still pending
- **THEN** the UI switches to the appropriate preparation phase and does not treat a download fraction of 1 as installation success

#### Scenario: Download without a known length
- **WHEN** the server does not provide a usable content length
- **THEN** the UI reports downloading with indeterminate progress rather than a fabricated percentage

#### Scenario: Complete atomic installation
- **WHEN** validation, extraction and atomic promotion have succeeded
- **THEN** the installer reports ready and the onboarding coordinator can reassess the dictionary gate

#### Scenario: Fail or close during extraction
- **WHEN** extraction fails or the application closes while extraction is underway
- **THEN** the UI is not blocked by synchronous file work; the operation observes cancellation at supported boundaries and cleans up without declaring ready or requiring immediate mid-extraction interruption
