$ErrorActionPreference = 'Stop'
$j = Invoke-RestMethod -Uri 'https://mtgjson.com/api/v5/Keywords.json'
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$rows = [System.Collections.Generic.List[string]]::new()
foreach ($cat in @('abilityWords', 'keywordAbilities', 'keywordActions')) {
    foreach ($k in $j.data.$cat) {
        if ($seen.Add($k)) { $null = $rows.Add("$cat`t$k") }
    }
}
Write-Host "count $($rows.Count)"
$rows | Set-Content -Path (Join-Path $PSScriptRoot 'mtgjson-keywords-dedup.txt') -Encoding utf8
