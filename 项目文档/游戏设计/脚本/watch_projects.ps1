<#
.SYNOPSIS
  监听项目目录的文件变化，自动触发索引更新。
.DESCRIPTION
  后台运行，检测到 .md/.py/.json/.canvas 文件新增/删除/重命名时，
  自动运行 generate_index.py --all 更新所有索引。

用法:
  .\_共享资源\脚本\watch_projects.ps1          # 启动监听（前台）
  Start-Job -FilePath .\_共享资源\脚本\watch_projects.ps1  # 后台运行
#>

$ROOT = "A:\通用工作区模板"
$SCRIPT = "$ROOT\_共享资源\脚本\generate_index.py"
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $ROOT
$watcher.IncludeSubdirectories = $true
$watcher.NotifyFilter = [System.IO.NotifyFilters]::FileName -bor [System.IO.NotifyFilters]::DirectoryName
$watcher.Filter = "*.*"

# 忽略的目录
$ignore = @('.git', '.venv', 'node_modules', '__pycache__', 'graphify-out', '.obsidian', '_backups')

# 防抖
$lastRun = Get-Date
$debounce = 5  # 秒

Register-ObjectEvent $watcher "Created" -Action {
    $path = $Event.SourceEventArgs.FullPath
    $ext = [System.IO.Path]::GetExtension($path)
    if ($ext -in '.md', '.py', '.json', '.canvas') {
        $now = Get-Date
        if (($now - $script:lastRun).TotalSeconds -ge $script:debounce) {
            $script:lastRun = $now
            Start-Sleep -Seconds 1
            Write-Host "📂 文件变更: $path"
            & "python" "$ROOT\_共享资源\脚本\generate_index.py" --all
        }
    }
} | Out-Null

Register-ObjectEvent $watcher "Deleted" -Action {
    $path = $Event.SourceEventArgs.FullPath
    $ext = [System.IO.Path]::GetExtension($path)
    if ($ext -in '.md', '.py', '.json', '.canvas') {
        $now = Get-Date
        if (($now - $script:lastRun).TotalSeconds -ge $script:debounce) {
            $script:lastRun = $now
            Start-Sleep -Seconds 1
            Write-Host "🗑️ 文件删除: $path"
            & "python" "$ROOT\_共享资源\脚本\generate_index.py" --all
        }
    }
} | Out-Null

Register-ObjectEvent $watcher "Renamed" -Action {
    $path = $Event.SourceEventArgs.FullPath
    $ext = [System.IO.Path]::GetExtension($path)
    if ($ext -in '.md', '.py', '.json', '.canvas') {
        $now = Get-Date
        if (($now - $script:lastRun).TotalSeconds -ge $script:debounce) {
            $script:lastRun = $now
            Start-Sleep -Seconds 1
            Write-Host "📝 文件重命名: $path"
            & "python" "$ROOT\_共享资源\脚本\generate_index.py" --all
        }
    }
} | Out-Null

Write-Host "✅ 文件监听已启动（防抖 $debounce 秒）"
Write-Host "   按 Ctrl+C 停止"

# 保持运行
while ($true) { Start-Sleep -Seconds 10 }
