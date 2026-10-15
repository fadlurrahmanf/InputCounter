#requires -Version 5.1

$app = Join-Path $PSScriptRoot 'AuraCode-star-progressive-aura-effects.exe'
if (-not (Test-Path -LiteralPath $app)) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        'AuraCode-star-progressive-aura-effects.exe tidak ditemukan. Jalankan Build-InputCounter.cmd terlebih dahulu.',
        'Aura Code',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    )
    exit 1
}

Start-Process -FilePath $app
