# Example Your Launcher plugin: "uuid" -> fresh GUIDs in several formats; Enter copies the chosen one.
# Protocol: one JSON line on stdin ({"query": "...", "version": 1}); answer with one JSON object on stdout.

# The launcher talks UTF-8 (no BOM) in both directions; PowerShell would otherwise use the OEM code page.
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$count = 1
if ($request.query -match '^\d+$') { $count = [Math]::Min([int]$request.query, 10) }   # "uuid 3" -> three GUIDs

$items = @()
for ($i = 0; $i -lt $count; $i++) {
    $guid = [guid]::NewGuid().ToString()
    $items += @{ title = $guid; subtitle = 'lowercase, dashes'; action = @{ type = 'copy'; text = $guid } }
    $items += @{ title = $guid.ToUpper(); subtitle = 'UPPERCASE, dashes'; action = @{ type = 'copy'; text = $guid.ToUpper() } }
    $items += @{ title = $guid.Replace('-', ''); subtitle = 'no dashes'; action = @{ type = 'copy'; text = $guid.Replace('-', '') } }
}

@{ items = $items } | ConvertTo-Json -Depth 5 -Compress
