# Shared grammar for formatter diagnostics consumed by the baseline generator and changed-file
# gate. Keep these values here so inventory generation and enforcement cannot drift.
$formatDiagnosticPattern = '^(?<path>.+?)\((?<line>\d+),(?<column>\d+)\): (?<severity>error|warning) (?<id>[A-Za-z0-9_]+): (?<message>.*?)(?: \[(?<project>[^\]]+)\])?$'
$formatTruncationMarker = 'Required references did not load'
