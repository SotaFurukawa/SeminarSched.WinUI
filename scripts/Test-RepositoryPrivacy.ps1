$ErrorActionPreference = 'Stop'

$forbiddenExtensions = @(
    '.jukuschedule', '.db', '.sqlite', '.sqlite3',
    '.csv', '.xls', '.xlsx', '.xlsb', '.pdf', '.pfx'
)

$trackedFiles = git ls-files
$violations = $trackedFiles | Where-Object {
    $forbiddenExtensions -contains [System.IO.Path]::GetExtension($_).ToLowerInvariant()
}

if ($violations) {
    Write-Error "Privacy-sensitive file types are tracked:`n$($violations -join "`n")"
}

Write-Host 'Privacy gate passed.'
