param([string]$ExePath = 'artifacts/UhFiveMOptimizer.exe')
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('uh-update-probe-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$target = Join-Path $root 'UhFiveMOptimizer.exe'
Copy-Item -LiteralPath $ExePath -Destination $target
$probe = Start-Process -FilePath $target -ArgumentList @('--begin-update-probe', ('"' + $root + '"')) -WindowStyle Hidden -PassThru
if (!$probe.WaitForExit(60000)) { throw 'The installer probe did not close its old app in time.' }
$resultPath = Join-Path $root 'probe-result.json'
for ($i = 0; $i -lt 60 -and !(Test-Path -LiteralPath $resultPath); $i++) { Start-Sleep -Milliseconds 500 }
if (!(Test-Path -LiteralPath $resultPath)) { throw "The installer did not complete. Inspect $root" }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
Write-Output ($result | ConvertTo-Json)
if (!$result.Success) { throw 'Installer integration probe failed.' }
Write-Output 'The actual published EXE completed a self-update in a disposable folder. No installed user app or Windows settings changed.'
