<#
.SYNOPSIS
  将 _共享资源/prompts/ 同步到 .github/prompts/
  注意：%APPDATA%\Code\User\prompts\ 和 .github/prompts/ 已是 Junction，
       修改 _shared/prompts/ 后自动反映到所有位置，本脚本不再需要。
.DESCRIPTION
  本脚本保留用于手动触发同步，但正常情况下已不需要使用。
#>

$src = "A:\通用工作区模板\_共享资源\prompts"
$localDst = "A:\通用工作区模板\.github\prompts"

Copy-Item "$src\*" $localDst -Recurse -Force

Write-Host "✅ prompt 同步完成"
Write-Host "   $src"
Write-Host "     -> $localDst"
Write-Host "   %APPDATA%\Code\User\prompts\ 是 Junction，无需操作"
