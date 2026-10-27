$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalMySql.ps1')
Start-XieBoxMySql
Set-XieBoxMySqlEnvironment
Write-Output '本地 MySQL 已就绪，开发数据库连接已载入当前终端。'
