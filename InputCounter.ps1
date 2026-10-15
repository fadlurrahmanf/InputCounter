#requires -Version 5.1

$app = Join-Path $PSScriptRoot 'AuraCode-idle-decay-star-hud.exe'
if (-not (Test-Path -LiteralPath $app)) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        'AuraCode-idle-decay-star-hud.exe tidak ditemukan. Jalankan Build-InputCounter.cmd terlebih dahulu.',
        'Aura Code',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    )
    exit 1
}

Start-Process -FilePath $app
