param([switch]$VerifyCopies)

$ErrorActionPreference = 'Stop'
$testRoot = Split-Path $PSScriptRoot -Parent
$workspaceRoot = Split-Path $testRoot -Parent
$baseline = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $testRoot 'Docs/Baseline/original-files.json')))
$expected = @{}
$issues = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $baseline) {
    $expected[$entry.Path] = $true
    $path = Join-Path $workspaceRoot $entry.Path
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        $issues.Add("Missing original: $($entry.Path)")
    } elseif ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) {
        $issues.Add("Changed original: $($entry.Path)")
    }
}
foreach ($root in @('main', 'sub')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $workspaceRoot $root) -File -Recurse -Force) {
        if ($file.FullName -match '\\\.git\\') { continue }
        $relative = $file.FullName.Substring($workspaceRoot.Length + 1)
        if (!$expected.ContainsKey($relative)) { $issues.Add("Added original: $relative") }
    }
}
$copiesChecked = 0
if ($VerifyCopies) {
    $copies = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $testRoot 'Docs/Baseline/copied-files.json')))
    $adjustments = @{}
    $adjustmentsPath = Join-Path $testRoot 'Docs/Baseline/copy-adjustments.json'
    if (Test-Path -LiteralPath $adjustmentsPath) {
        foreach ($item in (ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($adjustmentsPath)))) {
            $adjustments[$item.Destination] = $item.AfterSHA256
        }
    }
    foreach ($entry in $copies) {
        $path = Join-Path $testRoot $entry.Destination
        $expectedHash = $entry.SHA256
        if ($adjustments.ContainsKey($entry.Destination)) { $expectedHash = $adjustments[$entry.Destination] }
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
            $issues.Add("Missing copy: $($entry.Destination)")
        } elseif ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expectedHash) {
            $issues.Add("Changed copy: $($entry.Destination)")
        }
        $copiesChecked++
    }
}
$report = [pscustomobject]@{
    CheckedAt = (Get-Date).ToString('o')
    OriginalFilesChecked = $baseline.Count
    CopiedFilesChecked = $copiesChecked
    Passed = ($issues.Count -eq 0)
    Issues = @($issues.ToArray())
}
$report | ConvertTo-Json -Depth 4
if ($issues.Count -gt 0) { exit 1 }
