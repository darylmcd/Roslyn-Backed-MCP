# Shared grammar for formatter diagnostics consumed by the baseline generator and changed-file
# gate. Keep these values here so inventory generation and enforcement cannot drift.
$formatDiagnosticPattern = '^(?<path>.+?)\((?<line>\d+),(?<column>\d+)\): (?<severity>error|warning) (?<id>[A-Za-z0-9_]+): (?<message>.*?)(?: \[(?<project>[^\]]+)\])?$'
$formatTruncationMarker = 'Required references did not load'
# Phase markers are written to STDERR only. STDOUT is the byte-compared determinism payload,
# so anything emitted there would break the generator's determinism contract.
# Line shape: "<prefix> <phase> <start|end> elapsedMs=<n>", e.g. "##format-phase## restore end elapsedMs=41230".
$formatPhaseMarkerPrefix = '##format-phase##'
# Repository path identity follows the host filesystem. Keep the decision shared by the
# generator and changed-file gate so their baseline keys have the same semantics.
$formatPathComparer = if ($IsWindows) {
    [System.StringComparer]::OrdinalIgnoreCase
} else {
    [System.StringComparer]::Ordinal
}
