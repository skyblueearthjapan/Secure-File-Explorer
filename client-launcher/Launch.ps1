# Secure File Explorer client launcher (Option A: shared folder + auto-update)
# ASCII-only on purpose: Windows PowerShell 5.1 mis-decodes non-ASCII .ps1 without a BOM.
#
# On launch, compare the share's version.txt with the local copy; fetch if newer, then run.
# Each version goes to its own local folder, so a running exe is never overwritten.
# Place this next to version.txt and app\ on the distribution share.

$ErrorActionPreference = 'Stop'

try {
  $base = $PSScriptRoot
  $version = (Get-Content -LiteralPath (Join-Path $base 'version.txt') -Raw).Trim()
  if ([string]::IsNullOrWhiteSpace($version)) { throw 'version.txt is empty' }

  $localBase = Join-Path $env:LOCALAPPDATA 'SecureFileExplorer'
  $target    = Join-Path $localBase $version
  $exe       = Join-Path $target 'SecureFileExplorer.Client.exe'

  if (-not (Test-Path -LiteralPath $exe)) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    # fetch from share to local (mirror)
    robocopy (Join-Path $base 'app') $target /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
  }

  if (-not (Test-Path -LiteralPath $exe)) { throw "failed to fetch app: $exe" }

  # cleanup older versions (skip any that are locked / in use)
  if (Test-Path -LiteralPath $localBase) {
    Get-ChildItem -LiteralPath $localBase -Directory -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -ne $version } |
      ForEach-Object { try { Remove-Item -LiteralPath $_.FullName -Recurse -Force } catch {} }
  }

  Start-Process -FilePath $exe
}
catch {
  Add-Type -AssemblyName System.Windows.Forms
  [System.Windows.Forms.MessageBox]::Show(
    "Failed to start Secure File Explorer.`r`n$($_.Exception.Message)`r`n`r`nPlease check the network and access to the shared folder.",
    "Secure File Explorer", 'OK', 'Error') | Out-Null
}
