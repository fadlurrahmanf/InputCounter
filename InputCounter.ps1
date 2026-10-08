#requires -Version 5.1

$app = Join-Path $PSScriptRoot 'InputCounter-core-bottom-right.exe'
if (-not (Test-Path -LiteralPath $app)) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        'InputCounter-core-bottom-right.exe tidak ditemukan. Jalankan Build-InputCounter.cmd terlebih dahulu.',
        'Input Counter',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    )
    exit 1
}

Start-Process -FilePath $app
