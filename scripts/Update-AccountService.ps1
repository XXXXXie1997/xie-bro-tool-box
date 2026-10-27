$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskPackage = Join-Path $taskRoot 'artifacts/account-service-update.tar.gz'
if (-not (Test-Path -LiteralPath $taskPackage)) { throw 'Account service update package is missing.' }
Write-Host 'Update account service: unified login, public UID, session policy and concise emails.'
Write-Host 'Enter the server SSH password when prompted. Input is hidden; do not paste it into chat.'
$taskStage = '/tmp/xiebox-account-update-' + [Guid]::NewGuid().ToString('N')
$taskCommand = "exec 2>&1; umask 077; mkdir -m 700 $taskStage && base64 -di | tar -xz -C $taskStage && bash -n $taskStage/update-api.sh && bash $taskStage/update-api.sh"
$taskLog = Join-Path $taskRoot 'artifacts/account-service-deploy.log'
[Convert]::ToBase64String([IO.File]::ReadAllBytes($taskPackage)) | & "$env:WINDIR\System32\OpenSSH\ssh.exe" -o StrictHostKeyChecking=yes -o ConnectTimeout=15 root@60.205.128.183 $taskCommand 2>&1 | Tee-Object -FilePath $taskLog
$taskExit = $LASTEXITCODE
@{ exitCode = $taskExit; completedAt = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts/account-service-deploy-status.json')
Write-Host "Update finished with exit code $taskExit. Log: $taskLog"
Read-Host 'Press Enter to close'
exit $taskExit
