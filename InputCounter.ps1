#requires -Version 5.1

$app = Join-Path $PSScriptRoot 'InputCounter-stars-guaranteed.exe'
if (-not (Test-Path -LiteralPath $app)) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        'InputCounter-stars-guaranteed.exe tidak ditemukan. Jalankan Build-InputCounter.cmd terlebih dahulu.',
        'Input Counter',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    )
    exit 1
}

Start-Process -FilePath $app
